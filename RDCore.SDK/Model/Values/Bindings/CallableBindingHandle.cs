using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Model.Values.Bindings;

/// <summary>
/// A handle to a binding to a procedure declared in the workspace - a <c>Sub</c>, a <c>Function</c> or a property accessor -
/// whose source code runs when the binding is invoked.
/// </summary>
/// <remarks>
/// <para>
/// This is the binding of an <em>invocable</em> entity: it supports <see cref="BindingCapabilities.Invoke"/> and nothing else.
/// A procedure is not a value that can be read or assigned, so <see cref="GetValue"/>, <see cref="SetValue"/> and <see cref="Value"/>
/// are not supported; a reference to a parameterless <c>Function</c> that evaluates to its result is an invocation, made with no arguments.
/// </para>
/// <para>
/// The handle identifies the procedure and, for a member of a class, the object it is bound to; the <see cref="IProcedureInvoker"/> runs it.
/// </para>
/// </remarks>
/// <param name="Procedure">The <c>Sub</c>, <c>Function</c> or property accessor this handle invokes.</param>
/// <param name="Invoker">The execution engine that runs the source code of <paramref name="Procedure"/>.</param>
/// <param name="Receiver">
/// The object a member of a class is bound to, <see langword="null"/> for a procedure of a standard module. It is the <c>Me</c> of
/// the call: passed as the first argument (<strong>RD-VBAL</strong>: <c>Me</c> is the implicit parameter at index <c>0</c> of every member).
/// </param>
public record class CallableBindingHandle(VBTypeMemberSymbol Procedure, IProcedureInvoker Invoker, IRuntimeValue? Receiver = null) : IBindingHandle
{
    /// <inheritdoc/>
    public BindingCapabilities BindingCapabilities => BindingCapabilities.Invoke;

    /// <summary>
    /// Invokes the procedure and returns the outcome of the call as a result, the way the semantics of the language do:
    /// an error raised in the procedure, that nothing handled, is in the result rather than thrown.
    /// </summary>
    /// <param name="resolver">A read-only interface over the current execution context.</param>
    /// <param name="args">The arguments of the call, without the <c>Me</c> of a member of a class, which the handle passes itself.</param>
    /// <returns>The result of the call: the value the procedure returns (the <c>Void</c> value for a <c>Sub</c>), or the run-time error it raised.</returns>
    public RuntimeSemanticsEvaluationResult Call(ISymbolResolver resolver, IRuntimeValue[] args)
        => Invoker.Invoke(Procedure, resolver, Receiver is null ? args : [Receiver, .. args]);

    /// <summary>
    /// Invokes the procedure and returns the runtime value it returns - the <c>HRESULT</c> <c>S_OK</c>, for a <c>Sub</c>.
    /// </summary>
    /// <param name="resolver">A read-only interface over the current execution context.</param>
    /// <param name="args">The arguments of the call, without the <c>Me</c> of a member of a class, which the handle passes itself.</param>
    /// <remarks>
    /// 👉 A caller that handles the errors of the program, or needs the <em>typed</em> value of the call, uses <see cref="Call"/>, which
    /// returns them instead of throwing.
    /// </remarks>
    /// <exception cref="VBRuntimeErrorException">A run-time error was raised in the procedure and nothing handled it.</exception>
    /// <exception cref="InvalidOperationException">The invoker could not run the procedure, and has no error to say why.</exception>
    public IRuntimeValue Invoke(ISymbolResolver resolver, IRuntimeValue[] args)
    {
        var result = Call(resolver, args);
        if (result.ErrorInfo is { } error)
        {
            throw new VBRuntimeErrorException(error);
        }

        return result.Result?.RuntimeValue
            ?? throw new InvalidOperationException($"The invocation of '{Procedure.Name}' yielded neither a value nor an error.");
    }

    /// <summary>
    /// Not supported: a procedure has no value to read; <see cref="Invoke"/> it.
    /// </summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public IRuntimeValue GetValue(ISymbolResolver resolver)
        => throw new NotSupportedException($"'{Procedure.Name}' is a procedure: it is invoked, it has no value to read.");

    /// <summary>
    /// Not supported: a procedure cannot be assigned.
    /// </summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public void SetValue(ISymbolResolver resolver, IRuntimeValue value)
        => throw new NotSupportedException($"'{Procedure.Name}' is a procedure: it cannot be assigned a value.");

    /// <summary>
    /// Not supported: a procedure has no value to read; <see cref="Invoke"/> it.
    /// </summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public IRuntimeValue Value
        => throw new NotSupportedException($"'{Procedure.Name}' is a procedure: it is invoked, it has no value to read.");
}
