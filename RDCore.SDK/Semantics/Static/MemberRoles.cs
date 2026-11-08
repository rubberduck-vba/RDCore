using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace.Forms;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// Tells which members of a class module are there to implement a member of an interface it <c>Implements</c>, or to handle an event of one of its
/// <c>WithEvents</c> variables (<strong>MS-VBAL §5.3.1.8</strong>, <strong>§5.3.1.9</strong>).
/// </summary>
/// <remarks>
/// A member is one or the other by its name alone: <c>InterfaceName_MemberName</c> for the member of an interface that the module implements, and
/// <c>VariableName_EventName</c> for the event of a variable it declares <c>WithEvents</c> - the same rules <see cref="ImplementsSemantics"/> and
/// <see cref="ClassModuleEventSemantics"/> check the members by. A name that only begins like one, because the interface has no such member or the variable's class
/// has no such event, is the name of a member like any other. An interface or a class that does not resolve gives no member a role: a role is only stated when it is
/// known.
/// <para>
/// A form handles the events of its controls the same way, and its own events by procedures named for the class its designer hosts
/// (<see cref="FormClasses.HandlerPrefixOf"/>): <c>UserForm_Initialize</c> for a UserForm, <c>Form_Load</c> for a Visual Basic 6.0 form. The handler of an event
/// of a control of a Visual Basic 6.0 control array takes the control's index first (<c>Command1_Click(Index As Integer)</c>).
/// </para>
/// </remarks>
public static class MemberRoles
{
    /// <summary>
    /// The role of each member of <paramref name="module"/> that has one.
    /// </summary>
    /// <param name="module">The class module, with its members.</param>
    /// <param name="resolver">What finds the interface classes and the classes of the <c>WithEvents</c> variables, as they are now.</param>
    /// <param name="designer">What the module's designer declares when it is a form's (<see cref="Model.AST.ModuleParseResult.Designer"/>); <see langword="null"/>
    /// for any other class module.</param>
    /// <returns>The role of each procedure and property accessor that implements an interface member or handles an event; a member that has none is not among them.</returns>
    public static IReadOnlyDictionary<SemanticId, DeclarationRole> Of(VBClassModuleSymbol module, ISymbolResolver resolver, FormDescriptor? designer)
    {
        var roles = new Dictionary<SemanticId, DeclarationRole>();

        foreach (var name in module.ImplementedInterfaceNames)
        {
            // a Uri's fragment is where a symbol's identity lives, and Uri equality ignores it.
            if (resolver.ResolveType(name, ScopeKind.Global, StaticSymbol.GlobalUri).Symbol is not VBClassModuleSymbol implemented
                || implemented.Uri.AbsoluteUri == module.Uri.AbsoluteUri)
            {
                continue;
            }

            foreach (var member in ImplementsSemantics.InterfaceMembersOf(implemented))
            {
                Mark(module, $"{implemented.Name}_{member.Name}", DeclarationRole.InterfaceImplementation, roles);
            }
        }

        // a UserForm's control raises the events of the extender it is wrapped in too (Enter, Exit, BeforeUpdate, AfterUpdate)
        var extender = designer is null ? null : FormClasses.ExtenderOf(designer, resolver, module.Uri);
        var controlArrays = (designer?.AllControls() ?? []).Where(control => control.Index is not null)
            .Select(control => control.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in module.WithEventsVariables)
        {
            if (variable.ResolvedType is not VBClassType declared)
            {
                continue;
            }

            var source = VBProjectSymbol.ResolveClass(resolver, declared.Symbol) ?? declared.Symbol;
            if (source.Uri.AbsoluteUri == module.Uri.AbsoluteUri)
            {
                continue;
            }

            var isControl = variable.GetProperty(SymbolProperties.DesignerControl);
            foreach (var handled in FormControlExtender.EventsOf(variable, source, extender))
            {
                // the handler of an event of a form's control that does not match the event is never called by it: MS-VBA compiles it (oracle\s30 C06)
                if (isControl)
                {
                    var required = controlArrays.Contains(variable.Name) ? WithIndex(handled) : handled.Parameters;
                    MarkHandlers(ClassModuleEventSemantics.HandlersOf(module, variable, handled), required, roles);
                    continue;
                }

                Mark(module, $"{variable.Name}_{handled.Name}", DeclarationRole.EventHandler, roles);
            }
        }

        // a form handles its own events by procedures named for the class its designer hosts, whatever its name: UserForm_EventName for the events of the
        // Microsoft Forms UserForm and of the VBA form object (FormObject), Form_EventName for those of a Visual Basic 6.0 Form. One that does not match its
        // event is never called by it, as for a control's.
        if (designer is not null && FormClasses.HandlerPrefixOf(designer) is { } prefix)
        {
            foreach (var handled in FormClasses.Of(designer, resolver, module.Uri, module.WorkspaceRoot).SelectMany(form => form.Events))
            {
                MarkHandlers(module.Members.Where(member => member is VBProcedureMemberSymbol or VBFunctionMemberSymbol
                    && string.Equals(member.Name, $"{prefix}_{handled.Name}", StringComparison.OrdinalIgnoreCase)), handled.Parameters, roles);
            }
        }

        return roles;
    }

    private static void MarkHandlers(
        IEnumerable<VBTypeMemberSymbol> candidates, ImmutableArray<VBParameterSymbol> required, Dictionary<SemanticId, DeclarationRole> roles)
    {
        foreach (var candidate in candidates)
        {
            roles.TryAdd(candidate.SemanticId,
                ClassModuleEventSemantics.Matches(candidate, required) ? DeclarationRole.EventHandler : DeclarationRole.UnmatchedEventHandler);
        }
    }

    // the controls of a Visual Basic 6.0 control array share one handler of each event, which is told which control raised it by an Index As Integer before
    // the event's own parameters.
    private static ImmutableArray<VBParameterSymbol> WithIndex(VBEventMemberSymbol handled)
        => handled.Parameters.Insert(0, new VBParameterSymbol(
            handled.WorkspaceRoot, handled.Uri, "Index", SourceRange.Empty, SourceRange.Empty, ParameterKind.ImplicitByRef, VBIntegerType.TypeInfo));

    // the accessors of a property are procedures that derive from the subroutine's and the function's symbols.
    private static void Mark(VBClassModuleSymbol module, string name, DeclarationRole role, Dictionary<SemanticId, DeclarationRole> roles)
    {
        foreach (var candidate in module.Members.Where(member =>
            member is VBProcedureMemberSymbol or VBFunctionMemberSymbol && string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            roles.TryAdd(candidate.SemanticId, role);
        }
    }
}
