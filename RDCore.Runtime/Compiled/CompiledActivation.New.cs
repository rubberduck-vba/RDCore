using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.Values.Bindings;

namespace RDCore.Runtime.Compiled;

// a New of the activation, whose object compiled code makes as the interpreter makes it.
public sealed partial class CompiledActivation
{
    /// <summary>
    /// Makes the object of the <c>New</c> numbered <paramref name="made"/> of the body (MS-VBAL §5.6.8), at the instruction at <paramref name="pc"/>, as the
    /// interpreter makes it (<c>RuntimeExpressionEvaluator.CreateObject</c>): held by the statement's temporaries until the statement is over, and its
    /// <c>Initialize</c> run before it is given to anything (MS-VBAL §5.3.1.10). The frame says the instruction is the one being run while
    /// <c>Initialize</c> runs, as the interpreter's does.
    /// </summary>
    /// <param name="pc">The offset of the instruction that makes the object.</param>
    /// <param name="made">The number of the <c>New</c> among the body's (<see cref="CompiledBody.News"/>).</param>
    /// <param name="cell">A cell that refers to the object, as an object variable's does; the reference is the temporary's, and not counted again.</param>
    /// <returns>
    /// The object, which the interpreter is given when the statement is handed to it (<see cref="KnowObject"/>); <see langword="null"/> when making it, or
    /// its <c>Initialize</c>, raised an error, which the interpreter is given at the instruction (<see cref="AfterFailedCall"/>): the object is not made
    /// again.
    /// </returns>
    public object? New(int pc, int made, out object? cell)
    {
        SetPc(pc);
        _failed = null;
        _suspensionsBeforeCall = Suspensions;
        var created = _body.News[made];
        var result = RuntimeExpressionEvaluator.CreateObject(_session, created.New, created.Class);
        if (!result.IsSuccess || result.Result is not { } value)
        {
            _failed = result.IsSuccess || result.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(result.ErrorInfo!);
            cell = null;
            return null;
        }

        cell = new ValueBindingHandle(value.RuntimeValue);
        return value;
    }
}
