using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Semantics;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace RDCore.LanguageServer.Services.Workspace;

// two parts, because they invalidate different things: Declarations what other modules bind against, References the reference counts of their members. Neither
// says where anything is written, so moving code within its #If branch changes neither.
// MS-VBAL §3.4
public sealed record class ModuleSurface(string Declarations, string References)
{
    // the properties of a node that say where it is, or which node it is, and not what it says.
    private static readonly HashSet<string> Placement = ["Identity", "SourceLocation", "Location", "NameRange", "Children", "EqualityContract"];

    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Properties = new();

    public static ModuleSurface? Of(ModuleParseResult parse)
    {
        if (parse.SyntaxTree is not { } tree)
        {
            return null;
        }

        // which branch of which #If outside of a procedure each declaration is written in: one #If inside a procedure says which of its statements are compiled,
        // which is its body's.
        var procedures = tree.Descendants().OfType<MemberDeclarationNode>().Where(IsProcedure).Select(member => member.SourceLocation.Range).ToArray();
        var branches = new Branches(ConditionalCompilationBlocks.Of(parse.PrecompilerTrivia),
            block => !procedures.Any(range => range.Start < block.Start && block.Start <= range.End));

        var (declarations, references) = (new StringBuilder(), new StringBuilder());
        Append(declarations, references, tree, inBody: false, branches);

        // an annotation is trivia, and what a module says of itself: '@PredeclaredId, '@Exposed.
        foreach (var annotation in parse.Trivia.OfType<AnnotationTriviaNode>())
        {
            Append(declarations, references, annotation, inBody: false, branches);
        }

        // the directives say which of the declarations written in #If branches are the module's: each #Const, wherever it is, sets a constant of the whole module,
        // and each #If outside of a procedure says which branch is live - of the branches each declaration is told to be written in.
        foreach (var directive in parse.PrecompilerTrivia)
        {
            AppendDirective(declarations, directive, procedures, inProcedure: false);
        }

        return new ModuleSurface(declarations.ToString(), references.ToString());
    }

    private static bool IsProcedure(SyntaxNode node)
        => node is MemberDeclarationNode { MemberKind: MemberKind.Procedure or MemberKind.Function or MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet };

    // a directive, and those nested in it: the directives of the module are held in the code they are written in (PrecompilerTriviaNode), which is the syntax
    // tree's, and is not told. An #If inside a procedure, and what its condition is made of, is that procedure's body's; a #Const is told wherever it is.
    private static void AppendDirective(StringBuilder surface, SyntaxNode directive, SourceRange[] procedures, bool inProcedure)
    {
        // a stack of what is left to tell, not a call per node: a module nested past the parser's limit still has a tree, and its surface is made on an
        // ordinary thread. The children are pushed last first, so the nodes are told in the order a recursive walk would tell them.
        var pending = new Stack<(SyntaxNode Node, bool InProcedure)>();
        pending.Push((directive, inProcedure));
        while (pending.TryPop(out var next))
        {
            var (node, nodeInProcedure) = next;
            if (node is PrecompilerIfBlockStatementNode or PrecompilerConstantDeclarationNode)
            {
                var start = node.SourceLocation.Range.Start;
                nodeInProcedure = node is not PrecompilerConstantDeclarationNode && procedures.Any(range => range.Start < start && start <= range.End);
            }

            if (node is not PrecompilerTriviaNode && !nodeInProcedure)
            {
                Describe(surface, node);
            }

            var children = NodesOf(node).ToList();
            for (var index = children.Count - 1; index >= 0; index--)
            {
                pending.Push((children[index], nodeInProcedure));
            }
        }
    }

    // the #If blocks of the module that are counted, and the branches of them a declaration is written in: the ordinal of each, and not where it is.
    private sealed record class Branches(ConditionalCompilationBlocks Blocks, Func<SourceRange, bool> Counted)
    {
        public string At(SyntaxNode node)
            => Blocks.BranchesAt(node.SourceLocation.Range.Start, Counted) is { Count: > 0 } path
                ? " @" + string.Join("/", path.Select(branch => $"{branch.Block}.{branch.Branch}"))
                : string.Empty;
    }

    private static void Append(StringBuilder declarations, StringBuilder references, SyntaxNode root, bool inBody, Branches branches)
    {
        // a stack of what is left to tell, not a call per node, as for the directives: the children are pushed last first, in the order of a recursive walk.
        var pending = new Stack<(SyntaxNode Node, bool InBody)>();
        pending.Push((root, inBody));
        while (pending.TryPop(out var next))
        {
            var (node, nodeInBody) = next;
            if (!nodeInBody)
            {
                Describe(declarations, node, branches.At(node));
            }
            else if (node is SimpleNameExpressionNode name)
            {
                // a name is a name however it is cased.
                references.Append("n:").Append(name.IdentifierName.ToUpperInvariant()).Append('\n');
            }
            else if (node is VariableDeclarationNode or ConstantDeclarationNode or VBTypedDeclarationExpressionNode or RedimDeclarationNode)
            {
                // what a body declares is no other module's to see, and decides what the body's names mean.
                Describe(references, node);
            }

            var isProcedure = IsProcedure(node);
            var children = NodesOf(node).ToList();
            for (var index = children.Count - 1; index >= 0; index--)
            {
                // the statements of a procedure, the variables and constants it declares and its labels and line numbers, which nothing outside of it can go to,
                // are its body; its parameters, the type it returns and its attributes are its signature.
                var child = children[index];
                pending.Push((child,
                    nodeInBody || (isProcedure && child is StatementNode or VariableDeclarationNode or ConstantDeclarationNode or LineLabelNode)));
            }
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

    // a record or a struct of the syntax tree's own that is no node: what it holds is a node's, as a DefType's letters or an array's bounds are. The tree's own
    // types are those of the namespace of the parse result.
    private static bool IsOfTheTree(object? value)
        => value is not (null or SyntaxNode or SourceRange or SourceLocation or SyntaxNodeId)
            && value.GetType().Namespace?.StartsWith(typeof(ModuleParseResult).Namespace!, StringComparison.Ordinal) == true;

    // what the node is, and what it says that is not a node of its own - and of a declaration, the branches it is written in.
    private static void Describe(StringBuilder surface, SyntaxNode node, string branches = "")
    {
        surface.Append(node.GetType().Name).Append(Told(node)).Append(branches).Append('\n');
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
