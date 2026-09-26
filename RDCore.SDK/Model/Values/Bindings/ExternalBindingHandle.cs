using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Model.Values.Bindings;

/// <summary>
/// A handle to a binding to a member whose code is <em>not</em> the workspace's — a standard-library
/// member, a referenced library's, a host application's object model, or a native export a
/// <c>Declare</c> named.
/// </summary>
/// <remarks>
/// The sibling of <see cref="CallableBindingHandle"/>, and deliberately the same shape: the handle says
/// <em>which</em> member and, for a member of a class, <em>which object</em>; something else runs it. The
/// only difference is which engine that is — an <see cref="IProcedureInvoker"/> for code the workspace
/// declares, an <see cref="IExternalDispatcher"/> for everything else.
/// <para>
/// Like its sibling it only ever supports <see cref="BindingCapabilities.Invoke"/>. A property of a host's
/// object model is an accessor that can validate, have side effects and raise errors just as a workspace
/// property can, so no value-slot shortcut may bypass it; reading <c>Err.Number</c> is an invocation made
/// with no arguments. <see cref="Value"/> is never supported, because it reads without a resolver and an
/// invocation needs one.
/// </para>
/// </remarks>
/// <param name="Member">The external member this handle invokes.</param>
/// <param name="Dispatcher">The engine that reaches whatever really implements <paramref name="Member"/>.</param>
/// <param name="Receiver">
/// The object a member of a class is bound to, <see langword="null"/> for a member of a module. It is
/// passed as the argument at index <c>0</c>, the same as the <c>Me</c> of a workspace member — an external
/// object is an opaque handle, so this travels as a reference to something the provider owns rather than as
/// anything marshalled.
/// </param>
/// <param name="CallSite">Where the call is written, for an error that would otherwise have no location.</param>
public record class ExternalBindingHandle(
    VBTypeMemberSymbol Member,
    IExternalDispatcher Dispatcher,
    IRuntimeValue? Receiver = null,
    SourceLocation CallSite = default) : ICallableBinding
{
    /// <inheritdoc/>
    public BindingCapabilities BindingCapabilities => BindingCapabilities.Invoke;

    /// <summary>
    /// Invokes the member and returns the outcome as a result, the way the semantics of the language do:
    /// an error it raised, that nothing handled, is in the result rather than thrown.
    /// </summary>
    /// <param name="resolver">A read-only interface over the current execution context.</param>
    /// <param name="args">The arguments of the call, without the receiver, which the handle passes itself.</param>
    public RuntimeSemanticsEvaluationResult Call(ISymbolResolver resolver, IRuntimeValue[] args)
        => Dispatcher.Invoke(
            new ExternalCallRequest(Member, Receiver is null ? args : [Receiver, .. args], CallSite), resolver);

    /// <inheritdoc/>
    /// <exception cref="VBRuntimeErrorException">
    /// The member raised a run-time error and nothing handled it. A caller that handles the errors of the
    /// program, or wants the typed value, uses <see cref="Call"/>, which returns them instead.
    /// </exception>
    public IRuntimeValue Invoke(ISymbolResolver resolver, IRuntimeValue[] args)
    {
        var result = Call(resolver, args);
        if (result.ErrorInfo is { } error)
        {
            throw new VBRuntimeErrorException(error);
        }

        return result.Result?.RuntimeValue
            ?? throw new InvalidOperationException($"The invocation of '{Member.Name}' yielded neither a value nor an error.");
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always: an external member is reached by invoking it.</exception>
    public IRuntimeValue GetValue(ISymbolResolver resolver)
        => throw new NotSupportedException($"'{Member.Name}' is an external member: it is read by invoking it.");

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always: an external member is reached by invoking it.</exception>
    public void SetValue(ISymbolResolver resolver, IRuntimeValue value)
        => throw new NotSupportedException($"'{Member.Name}' is an external member: it is written by invoking its accessor.");

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always: an invocation needs a resolver.</exception>
    public IRuntimeValue Value
        => throw new NotSupportedException($"'{Member.Name}' is an external member: reading it is an invocation.");
}
