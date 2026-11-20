using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using System.Collections.Immutable;
using RDCore.SDK.Semantics;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.3.1.8, §5.3.1.9, §5.3.1.10
public static class MemberRoles
{
    public static IReadOnlyDictionary<SemanticId, DeclarationRole> Of(
        VBClassModuleSymbol module, ISymbolResolver resolver, FormDescriptor? designer, DocumentModuleFact? document = null)
        => BindingsOf(module, resolver, designer, document).ToDictionary(binding => binding.Key, binding => binding.Value.Role);

    // the procedure a run of the project starts with (DeclarationRole.EntryPoint, ProgramEntryPoint.InSession): the one the session's build section names, or
    // the Sub Main of the one standard module that declares one, as the session defined them. None for a library, or when no standard module declares it, or
    // more than one does.
    public static SemanticId? EntryPointOf(IRuntimeSession session, IEnumerable<VBModuleSymbol> project)
        => ProgramEntryPoint.InSession(session, project) is { IsFound: true } found ? found.Procedure!.SemanticId : null;

    // what each handler and implementation of a class is bound to: its source and the event or interface member, by the symbols that declare them. A member
    // that is named like one and is bound to nothing has no entry.
    public static IReadOnlyDictionary<SemanticId, DeclarationBinding> BindingsOf(
        VBClassModuleSymbol module, ISymbolResolver resolver, FormDescriptor? designer, DocumentModuleFact? document = null)
    {
        var bindings = new Dictionary<SemanticId, DeclarationBinding>();

        // the language raises a class's Initialize and Terminate events, which a subroutine with no parameters handles; a form's are the UserForm's, below,
        // and its Class_Initialize and Class_Terminate never run.
        if (designer is null)
        {
            foreach (var handled in (VBTypeMemberSymbol[])[ClassLifecycleInterface.Initialize, ClassLifecycleInterface.Terminate])
            {
                foreach (var handler in module.Members.Where(member => ClassModuleEventSemantics.Matches(member, [])
                    && string.Equals(member.Name, $"{ClassLifecycleInterface.InterfaceName}_{handled.Name}", StringComparison.OrdinalIgnoreCase)))
                {
                    bindings.TryAdd(handler.SemanticId, new(handler.SemanticId, DeclarationRole.EventHandler, module.SemanticId, handled.SemanticId));
                }
            }
        }

        // a document module handles the events of its host's class by procedures named for that class, as a form does its own.
        if (document is { HandlerPrefix: { } documentPrefix } && VBProjectSymbol.ResolveHostClass(resolver, module) is { } host)
        {
            foreach (var handled in host.Members.OfType<VBEventMemberSymbol>().Where(declared => document.HostEvents.Contains(declared.Name)))
            {
                Mark(module, $"{documentPrefix}{handled.Name}", DeclarationRole.EventHandler, host, handled, bindings);
            }
        }

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
                Mark(module, $"{implemented.Name}_{member.Name}", DeclarationRole.InterfaceImplementation, implemented, member, bindings);
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
                    MarkHandlers(ClassModuleEventSemantics.HandlersOf(module, variable, handled), required, variable, handled, bindings);
                    continue;
                }

                Mark(module, $"{variable.Name}_{handled.Name}", DeclarationRole.EventHandler, variable, handled, bindings);
            }
        }

        // a form handles its own events by procedures named for the class its designer hosts, whatever its name: UserForm_EventName for the events of the
        // Microsoft Forms UserForm and of the VBA form object (FormObject), Form_EventName for those of a Visual Basic 6.0 Form. One that does not match its
        // event is never called by it, as for a control's.
        if (designer is not null && FormClasses.HandlerPrefixOf(designer) is { } prefix)
        {
            foreach (var form in FormClasses.Of(designer, resolver, module.Uri, module.WorkspaceRoot))
            {
                foreach (var handled in form.Events)
                {
                    MarkHandlers(module.Members.Where(member => member is VBProcedureMemberSymbol or VBFunctionMemberSymbol
                        && string.Equals(member.Name, $"{prefix}_{handled.Name}", StringComparison.OrdinalIgnoreCase)), handled.Parameters, form, handled, bindings);
                }
            }
        }

        return bindings;
    }

    // a handler that does not match its event is bound to its source, and to no event.
    private static void MarkHandlers(IEnumerable<VBTypeMemberSymbol> candidates, ImmutableArray<VBParameterSymbol> required, Symbol source, Symbol handled,
        Dictionary<SemanticId, DeclarationBinding> bindings)
    {
        foreach (var candidate in candidates)
        {
            bindings.TryAdd(candidate.SemanticId, ClassModuleEventSemantics.Matches(candidate, required)
                ? new(candidate.SemanticId, DeclarationRole.EventHandler, source.SemanticId, handled.SemanticId)
                : new(candidate.SemanticId, DeclarationRole.UnmatchedEventHandler, source.SemanticId, null));
        }
    }

    /// <summary>
    /// The parameters of the handler of an event of a Visual Basic 6.0 control array: the controls of the array share one handler of each event, which is told
    /// which control raised it by an <c>Index As Integer</c> before the event's own parameters.
    /// </summary>
    /// <param name="handled">The event of the controls' class.</param>
    public static ImmutableArray<VBParameterSymbol> WithIndex(VBEventMemberSymbol handled)
        => handled.Parameters.Insert(0, new VBParameterSymbol(
            handled.WorkspaceRoot, handled.Uri, "Index", SourceRange.Empty, SourceRange.Empty, ParameterKind.ImplicitByRef, VBIntegerType.TypeInfo));

    // a property's Let and Set are subroutine symbols, and its Get a returning one, as a function's is: each accessor is marked, the Get that stands for the
    // property included.
    private static void Mark(VBClassModuleSymbol module, string name, DeclarationRole role, Symbol source, Symbol bound,
        Dictionary<SemanticId, DeclarationBinding> bindings)
    {
        foreach (var candidate in module.Members.Where(member =>
            member is VBProcedureMemberSymbol or VBReturningMemberSymbol && string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            bindings.TryAdd(candidate.SemanticId, new(candidate.SemanticId, role, source.SemanticId, bound.SemanticId));
        }
    }
}
