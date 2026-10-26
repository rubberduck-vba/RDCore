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
/// <c>UserForm_Terminate</c>), and its <c>Class_Initialize</c> and <c>Class_Terminate</c> do not run (Excel-measured, <c>oracle\s41</c> L50). Creating one loads it,
/// and its <c>Terminate</c> runs once: when it is let go of, unless <c>Unload</c> unloaded it before, which ran it then (<see cref="External.Forms.SessionForms"/>).
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
            External.Forms.SessionForms.Of(session).Load(instance);
            return External.Forms.FormHandlers.Raise(session, bindings, instance, "Initialize");
        }

        return Raise(instance, ClassLifecycleInterface.Initialize);
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Terminate(VBRuntimeObjectId instance)
    {
        if (IsForm(instance))
        {
            // what the form listened to goes with it; one that was unloaded stopped listening then.
            External.Forms.SessionForms.Of(session).StopListening(instance);
            return External.Forms.SessionForms.Of(session).Release(instance)
                ? External.Forms.FormHandlers.Raise(session, bindings, instance, "Terminate")
                : RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
        }

        return Raise(instance, ClassLifecycleInterface.Terminate);
    }

    private bool IsForm(VBRuntimeObjectId instance)
        => session.Symbols.TryGetInstance(instance, out var live) && SDK.Workspace.Forms.FormObject.IsFormClass(live.ClassModule);

    private RuntimeSemanticsEvaluationResult Raise(VBRuntimeObjectId instance, VBTypeMemberSymbol interfaceMember)
    {
        if (!session.Symbols.TryGetInstance(instance, out var live))
        {
            return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
        }

        foreach (var implemented in live.ClassModule.ImplementedInterfaces)
        {
            // the member is the interface's own: a Uri's fragment is where a symbol's identity lives.
            if (implemented.Uri.AbsoluteUri != interfaceMember.ParentUri.AbsoluteUri)
            {
                continue;
            }

            if (live.ClassModule.FindImplementation(implemented, interfaceMember) is not { } handler)
            {
                // the class has no procedure for the member, so it is implemented by the member's own default, which is
                // empty (SymbolProperties.DefaultImplementation): dispatching to it runs nothing.
                break;
            }

            var target = new VBObjectValue(instance).RuntimeValue;
            return bindings.ForMember(handler).Call(session.Symbols.Resolver, [target]);
        }

        return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
    }
}
