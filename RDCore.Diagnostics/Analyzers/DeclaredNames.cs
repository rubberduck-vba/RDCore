using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// A name that a module declares, and where it is written.
/// </summary>
/// <param name="Name">The name, without its type-declaration character.</param>
/// <param name="Range">Where the name is written.</param>
/// <param name="IsModule">Whether it is the name of the module itself, which a rename does not change.</param>
internal readonly record struct DeclaredName(string Name, SourceRange Range, bool IsModule = false);

/// <summary>
/// The names a module chooses: the ones a rule about names applies to.
/// </summary>
/// <remarks>
/// Not every name is chosen by the module. A procedure of an external library is named by the library; the implementation of an interface member, the handler of
/// an event and a lifecycle handler (<c>Class_Initialize</c>) are named by the interface, the event or the language. They are left out. That a member is one of
/// them is a fact the host vouches for (<see cref="SDK.Semantics.DeclarationFact.Role"/>), and not the underscore in its name: <c>Test_1</c> is the module's
/// choice. The parameters of a handler are named by the event, as Rubberduck's inspections take them; those of an implementation are the module's. Without the
/// facts, no member is taken for one. The accessors of a property are one declaration, and its name is reported once. A line label is a name the procedure
/// chooses; a line number is a number, and is left out. The module's own name, where its <c>Attribute VB_Name</c> says it, is a name the module chooses.
/// </remarks>
internal static class DeclaredNames
{
    /// <summary>
    /// Every name <paramref name="module"/> chooses, in the order they are written.
    /// </summary>
    /// <param name="module">The syntax tree of the module.</param>
    /// <param name="declarations">What the host states of the module's declarations, if anything.</param>
    public static IEnumerable<DeclaredName> In(ModuleNode module, IEnumerable<DeclarationFactDto>? declarations = null)
    {
        // the members named by an interface, an event or the language; and of them, the handlers, whose parameters the event names too.
        var facts = (declarations ?? [])
            .Where(declaration => declaration is
                { Role: DeclarationRole.InterfaceImplementation or DeclarationRole.EventHandler, Kind: DeclarationKind.Procedure or DeclarationKind.Property })
            .ToArray();
        var members = module.Children.OfType<MemberDeclarationNode>().ToArray();
        var namedElsewhere = members.Where(member => facts.Any(fact => Within(member.SourceLocation.Range, fact.Location.Range.Start))).ToHashSet();
        var handlers = members.Where(member => facts.Any(fact => fact.Role is DeclarationRole.EventHandler && Within(member.SourceLocation.Range, fact.Location.Range.Start)))
            .Select(member => member.SourceLocation.Range)
            .ToArray();
        bool IsNamedElsewhere(MemberDeclarationNode member) => namedElsewhere.Contains(member);

        var properties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in module.Descendants())
        {
            switch (node)
            {
                case AttributeDirectiveNode { Binding: null } attribute when string.Equals(attribute.Name, Tokens.VB_Name, StringComparison.OrdinalIgnoreCase):
                    if (module.GetDeclaredName() is { Length: > 0 } name)
                    {
                        yield return new(name, attribute.Location.Range, IsModule: true);
                    }

                    break;

                case VariableDeclarationNode variable:
                    yield return new(variable.Name, variable.NameRange ?? variable.SourceLocation.Range);
                    break;

                case ConstantDeclarationNode constant:
                    yield return new(constant.Name, constant.NameRange ?? constant.Location.Range);
                    break;

                case ParameterDeclarationNode parameter when !handlers.Any(handler => Within(handler, parameter.Location.Range.Start)):
                    yield return new(parameter.Name, parameter.NameRange ?? parameter.Location.Range);
                    break;

                case LineLabelNode label when label is not LineNumberNode:
                    yield return new(label.Name, label.NameRange ?? label.SourceLocation.Range);
                    break;

                case ExternalMemberDeclarationNode:
                    break;

                case MemberDeclarationNode { MemberKind: MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet } property:
                    if (!IsNamedElsewhere(property) && properties.Add(property.Name))
                    {
                        yield return new(property.Name, property.NameRange ?? property.SourceLocation.Range);
                    }

                    break;

                case MemberDeclarationNode { MemberKind: MemberKind.Procedure or MemberKind.Function } procedure:
                    if (!IsNamedElsewhere(procedure))
                    {
                        yield return new(procedure.Name, procedure.NameRange ?? procedure.SourceLocation.Range);
                    }

                    break;

                case MemberDeclarationNode member:
                    yield return new(member.Name, member.NameRange ?? member.SourceLocation.Range);
                    break;
            }
        }
    }

    private static bool Within(SourceRange range, SourcePosition position)
        => range.Start.CompareTo(position) <= 0 && position.CompareTo(range.End) <= 0;
}
