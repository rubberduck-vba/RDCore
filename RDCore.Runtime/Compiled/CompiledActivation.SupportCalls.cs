using RDCore.Runtime.Execution;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Compiled;

// a statement of the activation the interpreter runs for compiled code, as a call compiled code makes.
public sealed partial class CompiledActivation
{
    /// <summary>
    /// Runs the statement at <paramref name="pc"/> as the interpreter runs it, as a support call, with every variable written into the frame: a <c>Set</c>
    /// (MS-VBAL §5.4.3.9) given what compiled code does not evaluate (a call, a member of an object, <c>New</c>) or of what it does not assign, or a call
    /// statement compiled code does not make (MS-VBAL §5.4.2.1). Each call it makes is made once. What the statement held while it ran is let go of when it
    /// is over, as the interpreter lets go of it before the next instruction: an object's <c>Terminate</c> runs there. The frame says the instruction is the
    /// one being run, as it does of a call.
    /// </summary>
    /// <param name="pc">The offset of the statement.</param>
    /// <returns>
    /// Whether the statement completed. When not, the error it raised is the interpreter's to give the activation's handler, as a call's is
    /// (<see cref="AfterFailedCall"/>): the statement is not run again.
    /// </returns>
    public bool RunStatement(int pc)
    {
        // the frame the statement resolves its names in: an activation without one is given it now.
        Frame.Pc = pc;
        _failed = null;
        _suspensionsBeforeCall = Suspensions;
        var temporaries = _session.Objects.TemporaryCount;
        var outcome = _executor.Statements.Execute(_session, _context, Body.Items[pc].Node!);

        // a program that was stopped does not unwind (ProcedureExecutor.Run).
        if (outcome.Kind is not (RuntimeExecutionOutcomeKind.Break or RuntimeExecutionOutcomeKind.Halt))
        {
            ObjectReferences.ReleaseTemporaries(_session, temporaries);
        }

        if (outcome.Kind is RuntimeExecutionOutcomeKind.Next)
        {
            return true;
        }

        // the program stopped in a callee is the interpreter's to say, from the halt it was asked for (ProcedureExecutor.RunAfter).
        _failed = outcome.Kind is RuntimeExecutionOutcomeKind.Error ? outcome : RuntimeExecutionOutcome.InternalError;
        return false;
    }
}
