using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// The execution engine's side of a procedure call: runs the source code of a procedure declared in the workspace.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="Model.Values.Bindings.CallableBindingHandle"/> knows <em>which</em> procedure it stands for, not how to run it: running
/// it takes an activation of the procedure, a <see cref="ICallStackFrame"/> pushed on the session's call stack, and the executor that
/// steps through the procedure's statements. An invoker is what a runtime provides for that, once, for the procedures of a session.
/// </para>
/// </remarks>
public interface IProcedureInvoker
{
    /// <summary>
    /// Runs <paramref name="procedure"/> with the specified arguments, and returns the outcome of the call.
    /// </summary>
    /// <param name="procedure">The <c>Sub</c>, <c>Function</c> or property accessor to run.</param>
    /// <param name="resolver">A read-only interface over the current execution context.</param>
    /// <param name="arguments">
    /// The arguments of the call, in the order of the parameter list of the procedure. For a member of a class, the first argument is the
    /// object the member is invoked on, which is the procedure's <c>Me</c> (<strong>RD-VBAL</strong>: <c>Me</c> is the implicit
    /// parameter at index <c>0</c> of the parameter list of every member). An argument passed <c>ByRef</c> is a <see cref="VBRuntimeReference"/>.
    /// </param>
    /// <returns>
    /// A successful result with the value the procedure returns - the <c>Void</c> value, for a <c>Sub</c> - or the run-time error that was
    /// raised in the procedure, and that nothing handled. Never throws for an error of the running program.
    /// </returns>
    RuntimeSemanticsEvaluationResult Invoke(VBTypeMemberSymbol procedure, ISymbolResolver resolver, IRuntimeValue[] arguments);
}
