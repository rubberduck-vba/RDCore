using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.Execution;

/// <summary>
/// The <strong>RDCore.Runtime</strong> implementation of <see cref="IObjectLifecycle"/> (<strong>MS-VBAL §5.3.1.10</strong>).
/// </summary>
/// <remarks>
/// A class does not declare that it handles its lifecycle: every class module implements
/// <see cref="ClassLifecycleInterface"/> implicitly, so raising an event is dispatching an interface member
/// (<strong>MS-VBAL §5.3.1.9</strong>) to what the object's class implements it with, with the object as the target.
/// The handler is never looked up by the caller and never called by name.
/// <para>
/// A form's class is the exception: its lifecycle is the UserForm's, whose event procedures are named after it (<c>UserForm_Initialize</c>,
/// <c>UserForm_Terminate</c>), and its <c>Class_Initialize</c> and <c>Class_Terminate</c> do not run (observed in Excel). Creating one loads it,
/// and its <c>Terminate</c> runs once: when it is let go of, unless <c>Unload</c> unloaded it before, which ran it then (<see cref="External.Forms.SessionForms"/>).
/// </para>
/// <para>
/// A Visual Basic 6.0 form's handlers are <c>Form_Initialize</c> and <c>Form_Terminate</c>. Creating one does not load it, and its <c>Terminate</c> runs when its
/// last reference goes, whether it was ever loaded (measured in VB6).
/// </para>
/// </remarks>
/// <param name="session">The session whose objects' events are raised.</param>
/// <param name="bindings">What binds a handler to the code that runs it, as at any other call.</param>
public sealed class ClassLifecycle(IRuntimeSession session, ICallableBindingFactory bindings) : IObjectLifecycle
{
    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Initialize(VBRuntimeObjectId instance)
    {
        if (IsForm(instance))
        {
            // a Visual Basic 6.0 form's instance is not loaded yet: a built-in member, a control, Load or Show loads it.
            if (!External.Forms.SessionForms.Of(session).IsVisualBasic6(session, instance))
            {
                External.Forms.SessionForms.Of(session).Load(instance);
            }

            return External.Forms.FormHandlers.Raise(session, bindings, instance, "Initialize");
        }

        return Raise(instance, terminate: false);
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Terminate(VBRuntimeObjectId instance)
    {
        if (IsForm(instance))
        {
            // what the form listened to goes with it; one that was unloaded stopped listening then.
            External.Forms.SessionForms.Of(session).StopListening(instance);

            // a Visual Basic 6.0 form's Terminate runs when its instance goes, whether it was ever loaded: the loaded forms hold a loaded one until its unload.
            if (External.Forms.SessionForms.Of(session).IsVisualBasic6(session, instance))
            {
                return External.Forms.FormHandlers.Raise(session, bindings, instance, "Terminate");
            }

            return External.Forms.SessionForms.Of(session).Release(instance)
                ? External.Forms.FormHandlers.Raise(session, bindings, instance, "Terminate")
                : RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
        }

        return Raise(instance, terminate: true);
    }

    private bool IsForm(VBRuntimeObjectId instance)
        => session.Symbols.TryGetInstance(instance, out var live) && SDK.Workspace.Forms.FormObject.IsFormClass(live.ClassModule);

    // whether letting go of the object runs code of its class: a form's Terminate is the UserForm's, whatever its class has.
    internal static bool HandlesTerminate(IObjectInstance live)
        => SDK.Workspace.Forms.FormObject.IsFormClass(live.ClassModule) || HandlerOf(live, terminate: true) is not null;

    // runs the handler the object's class has for its Terminate event, or for its Initialize event.
    private RuntimeSemanticsEvaluationResult Raise(VBRuntimeObjectId instance, bool terminate)
    {
        if (!session.Symbols.TryGetInstance(instance, out var live) || HandlerOf(live, terminate) is not { } handler)
        {
            return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
        }

        var target = new VBObjectValue(instance).RuntimeValue;
        return bindings.ForMember(handler).Call(session.Symbols.Resolver, [target]);
    }

    // the procedure of the object's class that implements the member; none when the class has none, and the member is implemented by its own default,
    // which is empty (SymbolProperties.DefaultImplementation): dispatching to it runs nothing.
    private static VBTypeMemberSymbol? HandlerOf(IObjectInstance live, bool terminate)
    {
        var handlers = _handlers.GetValue(live.ClassModule, static classModule => new Handlers(classModule));
        return terminate ? handlers.Terminate : handlers.Initialize;
    }

    // the handlers of each class, found once for the class: what they are found from (its members, the interfaces it implements) is the class symbol's own and
    // does not change, and an edit of the class makes another symbol. Each object of the class is created and let go of with the same ones.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VBClassModuleSymbol, Handlers> _handlers = [];

    private sealed class Handlers(VBClassModuleSymbol classModule)
    {
        public VBTypeMemberSymbol? Initialize { get; } = Find(classModule, ClassLifecycleInterface.Initialize);

        public VBTypeMemberSymbol? Terminate { get; } = Find(classModule, ClassLifecycleInterface.Terminate);
    }

    private static VBTypeMemberSymbol? Find(VBClassModuleSymbol classModule, VBTypeMemberSymbol interfaceMember)
    {
        foreach (var implemented in classModule.ImplementedInterfaces)
        {
            // the member is the interface's own: a Uri's fragment is where a symbol's identity lives.
            if (implemented.Uri.AbsoluteUri == interfaceMember.ParentUri.AbsoluteUri)
            {
                return classModule.FindImplementation(implemented, interfaceMember);
            }
        }

        return null;
    }
}
