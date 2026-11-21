using System.Globalization;
using RDCore.Runtime.Execution;
using RDCore.Runtime.LanguageCore.Errors;
using RDCore.SDK.Model.Errors;

namespace RDCore.Runtime.Compiled;

// an error compiled code finds in an operation it evaluates, raised as the interpreter raises it: the activation's On Error Resume Next is given the error
// where compiled code is, or the interpreter at the instruction, which does not evaluate the instruction again to find it.
public sealed partial class CompiledActivation
{
    /// <summary>
    /// The error an arithmetic operation of the body found (MS-VBAL §5.6.9.3): runtime error 6 (Overflow) of a result its effective type does not hold, or
    /// 11 (Division by zero) of a <c>/</c>, a <c>\</c> or a <c>Mod</c> by 0, at the operation's expression, as the interpreter's operator raises it. The
    /// interpreter is given it at the instruction (<see cref="Recover"/>), as it is given the error of a call, unless the activation's <c>On Error Resume
    /// Next</c> is given it where compiled code is (<see cref="TryResumeNext"/>). Any other status the operation gave is not an error, and the interpreter
    /// evaluates the instruction as it always does.
    /// </summary>
    /// <param name="status">The status the operation gave (<see cref="PayloadStatus"/>).</param>
    /// <param name="site">The number of the operation among the body's (<see cref="CompiledBody.ArithmeticSites"/>).</param>
    public void RaiseArithmetic(int status, int site)
    {
        if (status is (int)PayloadStatus.Overflow or (int)PayloadStatus.DivisionByZero)
        {
            var id = (VBRuntimeErrorId)status;
            var verbose = VerboseTexts.Of(id);
            _failed = RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(id, _body.ArithmeticSites[site], verbose));
            _body.CountRaised();
        }
    }

    // the verbose text of error 11 and of error 6, as the interpreter's operators give it, in the language of the user interface at the call: what the
    // thread read in that language is kept for the next error, so that an error raised in a loop is looked up once.
    private sealed class VerboseTexts(CultureInfo culture)
    {
        [ThreadStatic]
        private static VerboseTexts? t_read;

        private readonly CultureInfo _culture = culture;
        private readonly string _divisionByZero = Exceptions.VBDivisionOp_DivisionByZero;
        private readonly string _overflow = Exceptions.VBRuntimeError_ArithmeticOverflow;

        public static string Of(VBRuntimeErrorId id)
        {
            var culture = Exceptions.Culture ?? CultureInfo.CurrentUICulture;
            if (t_read is not { } read || !ReferenceEquals(read._culture, culture))
            {
                t_read = read = new VerboseTexts(culture);
            }

            return id is VBRuntimeErrorId.DivisionByZero ? read._divisionByZero : read._overflow;
        }
    }

    /// <summary>
    /// Gives the error <see cref="RaiseArithmetic"/> found at the instruction at <paramref name="pc"/> to the activation's <c>On Error Resume Next</c> where
    /// compiled code is, as the interpreter gives it (<c>ProcedureExecutor.TryResumeNext</c>): Err is given the error, and compiled code goes on at the next
    /// instruction with its variables and its loops as they are, none of them written into the frame and read back. Where the interpreter is to have the
    /// error - another handler, a debugger that waits at it, the program stopping, the interpreter wanted - nothing is given, and the interpreter is given
    /// the error at the instruction (<see cref="Recover"/>).
    /// </summary>
    /// <param name="pc">The offset of the instruction.</param>
    /// <returns>Whether compiled code goes on at the instruction after the one at <paramref name="pc"/>.</returns>
    public bool TryResumeNext(int pc)
    {
        if (_failed is not { } failed || WantsInterpreterAfterCall)
        {
            return false;
        }

        var frame = Frame;
        var list = Body;
        if (!ReferenceEquals(frame.Body ?? list, list) || !HandKnowns(frame, list, pc, inside: false)
            || !ProcedureExecutor.TryResumeNext(_session, frame, list, pc, failed))
        {
            return false;
        }

        // the statement ends as the interpreter ends one whose error its handler was given: what it held is let go of (ProcedureExecutor.Step).
        _failed = null;
        if (HandStatement() is { } held)
        {
            ObjectReferences.ReleaseTemporaries(_session, held);
        }

        _body.CountRecovered();
        return true;
    }
}
