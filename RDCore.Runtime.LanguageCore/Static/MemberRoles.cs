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
using RDCore.SDK.Semantics;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.3.1.8, §5.3.1.9
public static class MemberRoles
{
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
        foreach (var variable in module.WithEventsVariables)
        {
            // the events of a control array are those of its controls' class
            var array = FormControlArray.ElementOf(variable.ResolvedType);
            if ((array ?? variable.ResolvedType) is not VBClassType declared)
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
                // the handler of an event of a form's control that does not match the event is never called by it: MS-VBA compiles it (observed in Excel)
                if (isControl)
                {
                    var required = array is not null ? WithIndex(handled) : handled.Parameters;
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
