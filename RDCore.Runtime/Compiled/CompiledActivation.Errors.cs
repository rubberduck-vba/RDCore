using RDCore.Runtime.Execution;
using RDCore.Runtime.LanguageCore.Errors;
using RDCore.SDK.Model.Errors;

namespace RDCore.Runtime.Compiled;

// an error compiled code finds in an operation it evaluates, raised as the interpreter raises it: the interpreter is given the error at the instruction, and
// does not evaluate the instruction again to find it.
public sealed partial class CompiledActivation
{
    /// <summary>
    /// The error an arithmetic operation of the body found (MS-VBAL §5.6.9.3): runtime error 6 (Overflow) of a result its effective type does not hold, or
    /// 11 (Division by zero) of a <c>/</c>, a <c>\</c> or a <c>Mod</c> by 0, at the operation's expression, as the interpreter's operator raises it. The
    /// interpreter is given it at the instruction (<see cref="Recover"/>), as it is given the error of a call. Any other status the operation gave is not an
    /// error, and the interpreter evaluates the instruction as it always does.
    /// </summary>
    /// <param name="status">The status the operation gave (<see cref="PayloadStatus"/>).</param>
    /// <param name="site">The number of the operation among the body's (<see cref="CompiledBody.ArithmeticSites"/>).</param>
    public void RaiseArithmetic(int status, int site)
    {
        if (status is (int)PayloadStatus.Overflow or (int)PayloadStatus.DivisionByZero)
        {
            var id = (VBRuntimeErrorId)status;
            var verbose = id is VBRuntimeErrorId.DivisionByZero ? Exceptions.VBDivisionOp_DivisionByZero : Exceptions.VBRuntimeError_ArithmeticOverflow;
            _failed = RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(id, _body.ArithmeticSites[site], verbose));
            _body.CountRaised();
        }
    }
}
