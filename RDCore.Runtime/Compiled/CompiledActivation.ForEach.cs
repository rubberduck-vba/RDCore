using RDCore.Runtime.Execution;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Compiled;

// a For Each of the activation, whose statement and Next the interpreter runs for compiled code.
public sealed partial class CompiledActivation
{
    /// <summary>
    /// Runs the <c>For Each</c> statement or the <c>Next</c> of one at <paramref name="pc"/> as the interpreter runs it (MS-VBAL §5.4.2.4), with the state the
    /// frame holds of the loop: the enumerator of an object, or the array and the position in it, and the control variable, which the frame holds too. What
    /// the instruction held while it ran is let go of when it is over, and so is what the loop held when it ends, as the interpreter lets go of them before
    /// the next instruction: an object's <c>Terminate</c> runs there. The frame says the instruction is the one being run, as it does of a call.
    /// </summary>
    /// <param name="pc">The offset of the instruction.</param>
    /// <returns>
    /// The offset of the instruction the program goes on at: the body, or the one after the loop. -1 when the instruction raised an error, which is the
    /// interpreter's to give the activation's handler, as a call's is (<see cref="AfterFailedCall"/>): the instruction is not run again.
    /// </returns>
    public int StepForEach(int pc)
    {
        var frame = Frame;
        frame.Pc = pc;
        _failed = null;
        _suspensionsBeforeCall = Suspensions;
        var temporaries = _session.Objects.TemporaryCount;
        var outcome = _executor.ExecuteForEach(_session, _context, Body.Items[pc], frame);

        // a program that was stopped does not unwind (ProcedureExecutor.Run).
        if (outcome is { } stopped)
        {
            if (stopped.Kind is not (RuntimeExecutionOutcomeKind.Break or RuntimeExecutionOutcomeKind.Halt))
            {
                ObjectReferences.ReleaseTemporaries(_session, temporaries);
            }

            frame.Pc = pc;
            _failed = stopped.Kind is RuntimeExecutionOutcomeKind.Error ? stopped : RuntimeExecutionOutcome.InternalError;
            return -1;
        }

        var next = frame.Pc;
        ObjectReferences.ReleaseTemporaries(_session, temporaries);
        LeaveBlocks(next);
        return next;
    }
}
