using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// A name that a module declares, and where it is written.
/// </summary>
/// <param name="Name">The name, without its type-declaration character.</param>
/// <param name="Range">Where the name is written.</param>
/// <param name="IsModule">Whether it is the name of the module itself, which a rename does not change.</param>
/// <param name="IsBound">
/// Whether it is the name of a handler or an implementation, which is renamed with the event, the control or the interface member it is bound to, and not
/// by a rename of its own.
/// </param>
internal readonly record struct DeclaredName(string Name, SourceRange Range, bool IsModule = false, bool IsBound = false);

/// <summary>
/// The names a module chooses: the ones a rule about names applies to.
/// </summary>
/// <remarks>
/// Not every name is chosen by the module. A procedure of an external library is named by the library; a lifecycle handler (<c>Class_Initialize</c>) by the
/// language; the implementation of a member of a library's interface and the handler of a library object's event (<c>WithEvents app As Excel.Application</c>,
/// a form's own <c>UserForm_Click</c>, a worksheet's <c>Worksheet_Change</c>) by the library. They are left out. The implementation of an interface member and
/// the handler of an event declared by a project of the workspace are renamed with the member or the event, and the handler of a form's control
/// (<c>txtName_Change</c>) with the control: their names are judged. That a member is one of them is a fact the host vouches for
/// (<see cref="SDK.Semantics.DeclarationFact.Role"/>, <see cref="SDK.Semantics.DeclarationFact.Binding"/>: its <c>BoundLibrary</c>,
/// <c>IsBoundInSourceProject</c> and <c>IsSourceAControl</c>), and not the underscore in its name: <c>Test_1</c> is the module's choice. The
/// parameters of a handler are named by the event, as Rubberduck's inspections take them; those of an implementation are the module's. Without the facts,
/// no member is taken for one. The accessors of a property are one declaration, and its name is reported once. A line label is a name the procedure
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
        // the members bound to an interface member, an event or the language; of them, those named by what the workspace cannot rename; and the handlers,
        // whose parameters the event names.
        var facts = (declarations ?? [])
            .Where(declaration => declaration is
                { Role: DeclarationRole.InterfaceImplementation or DeclarationRole.EventHandler, Kind: DeclarationKind.Procedure or DeclarationKind.Property })
            .ToArray();
        var fixedNames = facts.Where(IsNamedOutsideTheWorkspace).ToArray();
        var members = module.Children.OfType<MemberDeclarationNode>().ToArray();
        var namedElsewhere = members.Where(member => fixedNames.Any(fact => Within(member.SourceLocation.Range, fact.Location.Range.Start))).ToHashSet();
        var handlers = members.Where(member => facts.Any(fact => fact.Role is DeclarationRole.EventHandler && Within(member.SourceLocation.Range, fact.Location.Range.Start)))
            .Select(member => member.SourceLocation.Range)
            .ToArray();
        var bound = members.Where(member => facts.Any(fact => Within(member.SourceLocation.Range, fact.Location.Range.Start))).ToHashSet();
        bool IsNamedElsewhere(MemberDeclarationNode member) => namedElsewhere.Contains(member);

        var properties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nameDirective = module.GetDeclaredNameDirective();
        foreach (var node in module.Descendants())
        {
            switch (node)
            {
                case AttributeDirectiveNode attribute when ReferenceEquals(attribute, nameDirective):
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
                        yield return new(property.Name, property.NameRange ?? property.SourceLocation.Range, IsBound: bound.Contains(property));
                    }

                    break;

                case MemberDeclarationNode { MemberKind: MemberKind.Procedure or MemberKind.Function } procedure:
                    if (!IsNamedElsewhere(procedure))
                    {
                        yield return new(procedure.Name, procedure.NameRange ?? procedure.SourceLocation.Range, IsBound: bound.Contains(procedure));
                    }

                    break;

                case MemberDeclarationNode member:
                    yield return new(member.Name, member.NameRange ?? member.SourceLocation.Range);
                    break;
            }
        }
    }

    // a lifecycle handler is named by the language, and a handler or an implementation by a type library or a compiled library, which the workspace cannot
    // rename. A member bound to an event or an interface of the workspace's projects, a referenced source project's included, is renamed with it; a handler of
    // a form's control is renamed with the control, whatever library its events are of. A member whose binding is not stated is left out, as its role says.
    private static bool IsNamedOutsideTheWorkspace(DeclarationFactDto fact)
    {
        if (fact.Binding is not { } binding)
        {
            return true;
        }

        if (SymbolUri.SameRoot(binding.Source, fact.Symbol)
            && string.Equals(SymbolUri.PathOf(binding.Source), SymbolUri.ModuleNameOf(fact.Symbol), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return binding.BoundLibrary is not null && !binding.IsBoundInSourceProject && !binding.IsSourceAControl;
    }

    private static bool Within(SourceRange range, SourcePosition position)
        => range.Start.CompareTo(position) <= 0 && position.CompareTo(range.End) <= 0;
}
