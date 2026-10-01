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
/// </remarks>
/// <param name="session">The session whose objects' events are raised.</param>
/// <param name="bindings">What binds a handler to the code that runs it, as at any other call.</param>
public sealed class ClassLifecycle(IRuntimeSession session, ICallableBindingFactory bindings) : IObjectLifecycle
{
    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Initialize(VBRuntimeObjectId instance)
        => Raise(instance, ClassLifecycleInterface.Initialize);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Terminate(VBRuntimeObjectId instance)
        => Raise(instance, ClassLifecycleInterface.Terminate);

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
