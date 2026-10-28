using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Values.Abstract;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace RDCore.SDK.Model.AST;

/// <summary>
/// What the other modules of a project can be told by a module: a text that is the same for two parses of a module whenever no other module can tell them
/// apart, so that a change of the module that leaves it as it was cannot change what is found out about any other.
/// </summary>
/// <remarks>
/// <para>
/// The other modules see what a module declares: its options and attributes, its module-level declarations, and the signature of each of its procedures - its
/// name, its parameters, the type it returns - with every value written in them (a constant, an <c>Optional</c> parameter's default, an <c>Enum</c> member's
/// value, an array's bounds). What a procedure does is its own: what an analysis of one procedure knows of another is what it declares. Of a procedure's body,
/// the other modules see only the names it uses, which are what makes it reference a member of theirs (a code lens counts it, an analysis of unused members is
/// answered by it), and what it declares.
/// </para>
/// <para>
/// None of it is where it is written: an edit that moves a declaration leaves the surface as it was, and so does one to a comment, to whitespace, or to a body
/// that declares and names nothing it did not before - a literal, an operator, a statement that uses the same names. The surface errs on the side of a change:
/// a local variable is told, though no other module sees it.
/// </para>
/// </remarks>
public static class ModuleSurface
{
    // the properties of a node that say where it is, or which node it is, and not what it says.
    private static readonly HashSet<string> Placement = ["Identity", "SourceLocation", "Location", "NameRange", "Children", "EqualityContract"];

    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Properties = new();

    /// <summary>
    /// The surface of a parsed module; <see langword="null"/> when it has no syntax tree, which no other module can be told anything by.
    /// </summary>
    /// <param name="parse">The module, as it was parsed.</param>
    /// <remarks>Two surfaces are compared as strings: a module whose surface is not what it was may change what is found out about the other modules.</remarks>
    public static string? Of(ModuleParseResult parse)
    {
        if (parse.SyntaxTree is not { } tree)
        {
            return null;
        }

        var surface = new StringBuilder();
        Append(surface, tree, inBody: false);

        // an annotation is trivia, and what a module says of itself: '@PredeclaredId, '@Exposed.
        foreach (var annotation in parse.Trivia.OfType<AnnotationTriviaNode>())
        {
            Append(surface, annotation, inBody: false);
        }

        return surface.ToString();
    }

    private static void Append(StringBuilder surface, SyntaxNode node, bool inBody)
    {
        if (!inBody)
        {
            Describe(surface, node);
        }
        else if (node is SimpleNameExpressionNode name)
        {
            // a name is a name however it is cased.
            surface.Append("n:").Append(name.IdentifierName.ToUpperInvariant()).Append('\n');
        }
        else if (node is VariableDeclarationNode or ConstantDeclarationNode or VBTypedDeclarationExpressionNode or RedimDeclarationNode)
        {
            Describe(surface, node);
        }

        var isProcedure = node is MemberDeclarationNode { MemberKind: MemberKind.Procedure or MemberKind.Function or MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet };
        foreach (var child in NodesOf(node))
        {
            // the statements of a procedure are its body; its parameters and the type it returns are its signature.
            Append(surface, child, inBody || (isProcedure && child is StatementNode));
        }
    }

    // the nodes a node is made of: those the walk finds, and any it holds that the walk does not.
    private static IEnumerable<SyntaxNode> NodesOf(SyntaxNode node)
    {
        var walked = node.ChildNodes().ToList();
        var seen = new HashSet<SyntaxNode>(walked, ReferenceEqualityComparer.Instance);
        foreach (var child in walked)
        {
            yield return child;
        }

        foreach (var property in PropertiesOf(node.GetType()))
        {
            foreach (var held in HeldBy(property.GetValue(node)))
            {
                if (seen.Add(held))
                {
                    yield return held;
                }
            }
        }
    }

    // the nodes a value holds: itself, those of a collection, and those of a record of the syntax tree's own (an array's bounds hold their expressions).
    private static IEnumerable<SyntaxNode> HeldBy(object? value) => value switch
    {
        SyntaxNode node => [node],
        string => [],
        IEnumerable values => values.Cast<object?>().SelectMany(HeldBy),
        _ when IsOfTheTree(value) => PropertiesOf(value!.GetType()).SelectMany(property => HeldBy(property.GetValue(value))),
        _ => [],
    };

    // a record or a struct of the syntax tree's own that is no node: what it holds is a node's, as a DefType's letters or an array's bounds are.
    private static bool IsOfTheTree(object? value)
        => value is not (null or SyntaxNode or SourceRange or SourceLocation or SyntaxNodeId)
            && value.GetType().Namespace?.StartsWith(typeof(ModuleSurface).Namespace!, StringComparison.Ordinal) == true;

    // what the node is, and what it says that is not a node of its own.
    private static void Describe(StringBuilder surface, SyntaxNode node)
    {
        surface.Append(node.GetType().Name).Append(Told(node)).Append('\n');
    }

    // what a value of the tree says, its nodes and where it is written left out: " Name=value" for each of what it says.
    private static string Told(object value)
    {
        var told = new StringBuilder();
        foreach (var property in PropertiesOf(value.GetType()))
        {
            if (TextOf(property.GetValue(value)) is { } text)
            {
                told.Append(' ').Append(property.Name).Append('=').Append(text);
            }
        }

        return told.ToString();
    }

    private static string? TextOf(object? value) => value switch
    {
        null => null,
        SyntaxNode or SourceRange or SourceLocation or SyntaxNodeId => null,
        string or bool or char or Enum => value.ToString(),
        IFormattable number => number.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        VBTypedValue typed => JsonSerializer.Serialize(typed, SyntaxNodeJson.Options),
        IEnumerable values => string.Join(",", values.Cast<object?>().Select(TextOf).Where(text => text is not null)),
        _ when IsOfTheTree(value) => "{" + Told(value) + " }",

        // a value of any other type is not told, lest where it is written - a record that holds a location - be told with it.
        _ => null,
    };

    private static PropertyInfo[] PropertiesOf(Type type)
        => Properties.GetOrAdd(type, static type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0 && !Placement.Contains(property.Name))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray());
}
