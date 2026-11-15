using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.Statements;
using RDCore.SDK;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.Runtime.LanguageCore.Binding;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Drives one activation's program counter through its <see cref="InstructionList"/>
/// (<strong>RD-VBAL §3.5.1</strong>): fetch, decode by <see cref="InstructionKind"/>, react to the
/// outcome, repeat.
/// </summary>
/// <remarks>
/// <c>Simple</c> instructions delegate to <see cref="IStatementRuntimeSemanticsProvider"/>; every other
/// kind's control effect is pre-resolved on the <see cref="Instruction"/> itself by lowering, so the loop
/// decides <em>whether</em> to branch without any statement semantics needing to know about the program
/// counter at all - the same split RD-VBAL §3.5.2's own design note describes.
/// <para>
/// Wired here: <c>Simple</c>, <c>Jump</c>, <c>ConditionalBranch</c> for <c>If</c>/<c>ElseIf</c>/inline
/// <c>If</c> (a Boolean condition, evaluated via <see cref="ConditionEvaluator"/>) and, separately, a
/// <c>Select Case</c>'s own <c>Case</c> header (<see cref="Instruction.Matching"/> set: matches its range
/// clauses against the enclosing <c>Select</c>'s own stashed selector via <see cref="CaseMatchEvaluator"/>
/// instead of a condition) — both react to the same shape of result identically (see <c>Branch</c>).
/// <c>Select</c> and <c>With</c> alike evaluate their own header expression once, stash the result on the
/// activation keyed by their own offset (<see cref="CallStackFrame.SetBlockState"/>), then fall through
/// into the body — there is no separate closer instruction to pop the stash on exit. A block's own inner
/// instructions carry <see cref="Instruction.EnclosingWith"/> (a <c>Case</c> header reads its own
/// <see cref="Instruction.Matching"/> directly instead, since it needs the value, not a resolved
/// expression context), resolved back into <see cref="RuntimeEvaluationContext.EnclosingWithTarget"/> per
/// instruction before every <c>Simple</c>/<c>ConditionalBranch</c> dispatch below, so a with-relative
/// <c>.Member</c> read resolves correctly however control reached that instruction. <c>ExitProcedure</c>,
/// <c>Halt</c>, <c>Break</c> are wired too, and so are five of the six loop shapes: a pre-test loop
/// (<c>While…Wend</c>/<c>Do While</c>/<c>Do Until</c>) is a <c>ConditionalBranch</c> like an <c>If</c>
/// header, a post-test loop (<c>Do…Loop While</c>/<c>Do…Loop Until</c>) is <c>LoopBack</c>, and a bare
/// <c>Do…Loop</c> is already just an unconditional <c>Jump</c> back to its own body — none of the three
/// need any new per-activation state, only a Boolean condition and, for the <c>Until</c> half of each
/// pair, a polarity flip (see <c>GetCondition</c>). <c>Exit For</c>/<c>Exit Do</c> (<c>ExitLoop</c>) is
/// handled identically to <c>Jump</c>. A <c>For</c> loop's own shape is wired too: <c>ForOpener</c>
/// evaluates start/end/step once (<see cref="ForLoopEvaluator"/>) and stashes them — the counter itself
/// is a real, addressable variable, Let-assigned through <see cref="ICallStackFrame.TryGetForLoopState"/>'s
/// own state rather than shadowed — then either falls through into the body or skips it entirely when
/// already out of range; <c>ForNext</c> reads that same state back via <see cref="Instruction.Matching"/>,
/// increments, and re-tests. <c>For Each</c> over an array works the same shape, via
/// <see cref="ForEachEvaluator"/>/<see cref="ICallStackFrame.TryGetForEachState"/> — a <c>For Each</c>
/// over a live object whose class exposes an enumeration member (<c>VB_UserMemId = -4</c>, commonly
/// named <c>_NewEnum</c>) is recognized but not yet runnable (actually enumerating one needs real
/// procedure invocation, which doesn't exist yet); anything else the collection expression could
/// evaluate to is a real, reportable run-time error, not a deferred gap. <c>On…GoTo</c>/<c>On…GoSub</c>
/// (<c>JumpTable</c>/<c>GoSubTable</c>) share one selector algorithm (<see cref="JumpTableEvaluator"/>):
/// evaluate once, Let-coerce to <c>Integer</c>, branch to the n'th label — zero or out-of-range falls
/// through, negative or over 255 is error 5. <c>GoSub</c> pushes the offset right after itself onto the
/// activation's own GoSub Resumption List before branching (<see cref="CallStackFrame.PushGoSubReturn"/>);
/// <c>On…GoSub</c>'s successful branch does the same. <c>Return</c> pops that list
/// (<see cref="CallStackFrame.TryPopGoSubReturn"/>) and branches there — an empty list is error 3.
/// </para>
/// <para>
/// <c>On Error</c>/<c>Resume</c> (<strong>MS-VBAL §5.4.4</strong>) work by interception, not by their own
/// dedicated branch logic: every dispatch above that could yield an <c>Error</c> outcome routes it through
/// <c>InterceptError</c> before the loop decides whether to actually stop, so EVERY runtime error this
/// executor can already raise — from any subsystem, present or future — becomes catchable for free, with
/// no per-error-site changes. <c>activation.ErrorHandler</c> (<see cref="ErrorHandlerState"/>) is a single
/// mutable value per activation, like <c>Pc</c>, not per-offset hidden state: an <c>On Error</c> statement
/// changes the policy going forward, it isn't scoped to one block. <c>On Error Resume Next</c> catches
/// silently and keeps catching every later error the same way; <c>On Error GoTo</c> &lt;label&gt; branches
/// there and resets the policy to disabled, so an unhandled second error inside the handler body itself
/// propagates rather than re-entering it — real VBA's own asymmetry between the two, not a simplification.
/// <c>Resume</c>/<c>Resume Next</c>/<c>Resume</c> &lt;label&gt; all require an active error (error 20
/// otherwise) and clear it; <c>Error</c> &lt;number&gt; raises a run-time error directly, "as if
/// <c>Err.Raise</c> were invoked." What this does NOT model: the <c>Err</c> object itself as
/// something VBA source can read/call (<c>Err.Number</c>, <c>Err.Raise(...)</c> — needs member-dispatch
/// machinery, S9's job) and a <c>Default</c>-policy error actually propagating to a real CALLER activation
/// (no multi-frame call stack exists yet — today, "propagate" means this <c>Run</c> call's own return
/// value, which is exactly what a future caller's <c>Run</c> would see and apply its own policy to, so the
/// mechanism generalizes for free once S9 exists).
/// </para>
/// </remarks>
public sealed class ProcedureExecutor(IStatementRuntimeSemanticsProvider statements, ConditionEvaluator conditions, WithTargetEvaluator withTargets, CaseMatchEvaluator cases, ForLoopEvaluator forLoop, ForEachEvaluator forEach, JumpTableEvaluator jumpTable, ErrorHandlingEvaluator errorHandling, CancellationToken cancellation = default, AnalysisObservation? observation = null)
{
    // set once a program that waited for the cancellation was resumed: a cancelled token stays cancelled, and it was one break.
    private bool _cancellationAcknowledged;

    /// <summary>
    /// Whether the program is to stop between two instructions: what stops it is cancelled, and that is not a break the program was already
    /// resumed from. What compiled code polls, and hands the program to this executor for.
    /// </summary>
    internal bool IsCancellationPending => cancellation.IsCancellationRequested && !_cancellationAcknowledged;

    /// <summary>The observation of the analysis the instructions are evaluated for, if they are: an analysis runs no compiled code.</summary>
    internal AnalysisObservation? Observation => observation;

    /// <summary>The evaluator the expressions of the instructions are evaluated with.</summary>
    internal RuntimeExpressionEvaluator Expressions => conditions.Expressions;

    /// <summary>
    /// The semantics a <c>Simple</c> instruction's statement is executed with: what compiled code runs a statement as a support call with.
    /// </summary>
    internal IStatementRuntimeSemanticsProvider Statements => statements;

    // above zero while a breakpoint's condition is evaluated: a procedure the condition calls runs on this executor, and neither waits at a breakpoint nor ends a step.
    private int _conditionDepth;

    // Whether the program waits at a breakpoint that was reached: always for one with no condition, and for one with a condition when it is True. A condition that
    // raises an error waits the program as well - a breakpoint that is passed over because its condition is wrong is one the person never learns is wrong - and the
    // failure says what the error was, for the program to be said to wait on it (ISessionHalt.TrySuspendOnCondition).
    private bool IsMet(IRuntimeSession session, ExpressionNode? condition, Uri scope, RuntimeEvaluationContext context, out BreakpointConditionFailure? failure)
    {
        failure = null;
        if (condition is null)
        {
            return true;
        }

        RuntimeSemanticsEvaluationResult result;
        _conditionDepth++;
        try
        {
            result = conditions.EvaluateBoolean(session, condition, context);
        }
        finally
        {
            _conditionDepth--;
        }

        if (!result.IsSuccess)
        {
            failure = new BreakpointConditionFailure(condition, scope, result.ErrorInfo);
            return true;
        }

        return ((VBBooleanValue)result.Result!).Value.StoredValue != 0;
    }

    /// <summary>
    /// Runs <paramref name="frame"/> against <paramref name="list"/> from its current <c>Pc</c> until
    /// the outcome is anything other than <c>Next</c>/<c>Branch</c>.
    /// </summary>
    public RuntimeExecutionOutcome Run(IRuntimeSession session, ICallStackFrame frame, InstructionList list, RuntimeEvaluationContext context)
        => Run(session, frame, list, context, once: false);

    /// <summary>
    /// Runs <paramref name="frame"/> as <see cref="Run(IRuntimeSession, ICallStackFrame, InstructionList, RuntimeEvaluationContext)"/> does, the instruction
    /// at its <c>Pc</c> one compiled code handed over in the middle of: the temporaries its statement holds are those held since <paramref name="held"/>
    /// (<see cref="ISessionObjects.TemporaryCount"/> when compiled code began the statement), which the instruction lets go of when it is over, as any.
    /// </summary>
    /// <param name="session">The session the activation runs in.</param>
    /// <param name="frame">The activation.</param>
    /// <param name="list">Its instructions.</param>
    /// <param name="context">The context it is evaluated in.</param>
    /// <param name="held">The temporary count when compiled code began the statement; <see langword="null"/> when it began none.</param>
    internal RuntimeExecutionOutcome Run(IRuntimeSession session, ICallStackFrame frame, InstructionList list, RuntimeEvaluationContext context, int? held)
        => Run(session, frame, list, context, once: false, held);

    /// <summary>
    /// Runs the one instruction of <paramref name="list"/> at <paramref name="frame"/>'s <c>Pc</c>, as <see cref="Run(IRuntimeSession, ICallStackFrame,
    /// InstructionList, RuntimeEvaluationContext)"/> runs each - its error given the activation's handler - or, given the <paramref name="failed"/> outcome
    /// of that instruction, which a caller other than this executor ran, the outcome alone. What compiled code hands over of an activation that has an error
    /// handler.
    /// </summary>
    /// <returns>
    /// <see cref="RuntimeExecutionOutcome.Next"/> when the activation goes on, at the frame's <c>Pc</c>; otherwise what it returns, or what stopped it.
    /// </returns>
    internal RuntimeExecutionOutcome Step(IRuntimeSession session, CallStackFrame frame, InstructionList list, RuntimeEvaluationContext context,
        RuntimeExecutionOutcome? failed, int? held = null)
    {
        if (failed is not { } outcome)
        {
            return Run(session, frame, list, context, once: true, held);
        }

        if (InterceptError(session, frame, list, frame.Pc, outcome) is { } unhandled)
        {
            var propagated = unhandled.Kind is RuntimeExecutionOutcomeKind.InternalError && session.Halt.Pending is { } halt
                ? halt is RuntimeHaltKind.End ? RuntimeExecutionOutcome.Halt : RuntimeExecutionOutcome.Break
                : unhandled;
            LetGoOfStatement(session, held, propagated);
            return propagated;
        }

        LetGoOfStatement(session, held, outcome);
        return RuntimeExecutionOutcome.Next;
    }

    // the statement compiled code began (held), which an error a caller other than this executor ran ended: what its temporaries hold is let go of before the
    // handler runs, as an instruction this executor runs lets go of its own when it ends, unless the program stopped.
    private static void LetGoOfStatement(IRuntimeSession session, int? held, RuntimeExecutionOutcome outcome)
    {
        if (held is { } mark && outcome.Kind is not (RuntimeExecutionOutcomeKind.Break or RuntimeExecutionOutcomeKind.Halt))
        {
            ObjectReferences.ReleaseTemporaries(session, mark);
        }
    }

    private RuntimeExecutionOutcome Run(IRuntimeSession session, ICallStackFrame frame, InstructionList list, RuntimeEvaluationContext context, bool once,
        int? held = null)
    {
        // the temporaries of this activation's statements are those made from here on (ObjectReferences.HoldTemporarily): each statement lets go of
        // its own before the next runs, and the last one before the activation returns. A program that was stopped does not unwind (an End runs no
        // Terminate; a break keeps everything as it is), and what it leaves is the session's to clear. An instruction compiled code handed over in the
        // middle of holds the temporaries made since compiled code began it (held), which are this activation's too.
        var temporaries = held ?? session.Objects.TemporaryCount;
        RuntimeExecutionOutcome outcome;
        try
        {
            outcome = RunInstructions(session, frame, list, context, temporaries, once);
        }
        finally
        {
            // what compiled code had evaluated of the instruction it handed over is of that instruction alone, however the run of it ended.
            ((CallStackFrame)frame).Forget();
        }

        if (outcome.Kind is not (RuntimeExecutionOutcomeKind.Break or RuntimeExecutionOutcomeKind.Halt))
        {
            ObjectReferences.ReleaseTemporaries(session, temporaries);
        }

        // a statement can stop the program without being one of the two that are for it: a Debug.Assert that fails breaks.
        if (outcome.Kind is RuntimeExecutionOutcomeKind.Break or RuntimeExecutionOutcomeKind.Halt)
        {
            session.Halt.Request(outcome.Kind is RuntimeExecutionOutcomeKind.Halt ? RuntimeHaltKind.End : RuntimeHaltKind.Break);
        }

        // A program that was stopped inside a call has no answer to give the statement that made the call, and a call without one fails the way a call
        // fails that the interpreter cannot answer. It was not that: the session says what stopped it, and the procedure that made the call is stopped by it too.
        return outcome.Kind is RuntimeExecutionOutcomeKind.InternalError && session.Halt.Pending is { } halt
            ? halt is RuntimeHaltKind.End ? RuntimeExecutionOutcome.Halt : RuntimeExecutionOutcome.Break
            : outcome;
    }

    /// <summary>
    /// Evaluates every instruction of <paramref name="list"/> once, in the order they are written, and follows no jump.
    /// </summary>
    /// <returns>
    /// Whether every instruction was evaluated: <see langword="false"/> if the evaluation of one failed for a reason that is not the code's
    /// (<see cref="RuntimeExecutionOutcomeKind.InternalError"/>), or the sweep was cancelled.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is what an analysis of the code as a whole does, as opposed to a run of it: a run follows one path for one set of arguments, and
    /// the branch nobody took is never seen. Each instruction is evaluated by the same evaluator that evaluates it when the code runs, so
    /// the facts the pipeline's observer is told of are the language's own answer at each place, and not a second reading of the code.
    /// </para>
    /// <para>
    /// A <c>Select Case</c>, a <c>With</c> and a <c>For</c> are opened before they are closed, and so find their state where they look for
    /// it. An error is the answer of the instruction that raised it and does not stop the sweep, and no handler catches it: the
    /// next instruction is the one that follows, as it is for the next sweep of the next procedure.
    /// </para>
    /// </remarks>
    public bool Sweep(IRuntimeSession session, ICallStackFrame frame, InstructionList list, RuntimeEvaluationContext context)
    {
        var activation = (CallStackFrame)frame;
        var complete = true;

        // each instruction lets go of what its expressions made before the next is evaluated, as it does in a run.
        var temporaries = session.Objects.TemporaryCount;
        foreach (var instruction in list.Items)
        {
            ObjectReferences.ReleaseTemporaries(session, temporaries);
            if (cancellation.IsCancellationRequested)
            {
                return false;
            }

            activation.Pc = instruction.Offset;
            var assumedBefore = observation?.Assumptions ?? 0;
            RuntimeExecutionOutcome? outcome;
            try
            {
                var instructionContext = ResolveContext(context, activation, list, instruction);
                if (ReliesOnAnotherInstruction(instruction, instructionContext))
                {
                    observation?.Assume();
                }

                outcome = SweepInstruction(session, instructionContext, instruction, activation);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // an analysis is the one thing that evaluates code nobody asked to run, so a defect of the platform that a program would never reach in
                // a run is reached here, in a process that also holds the session the program runs in. It is that instruction's, not the procedure's:
                // the instructions that follow are still evaluated, and the procedure says it was not all.
                outcome = RuntimeExecutionOutcome.InternalError;
            }

            complete &= outcome is not { Kind: RuntimeExecutionOutcomeKind.InternalError };

            // the error of an instruction whose evaluation assumed nothing is the code's: the instruction raises it whenever it runs. One that assumed
            // something may have raised the assumption's error instead, and says nothing about the code - but for an error the types decide, which is
            // raised whatever was assumed (VBRuntimeErrorInfo.IsDecidedByTypes).
            if (observation is not null && outcome is { Kind: RuntimeExecutionOutcomeKind.Error, ErrorInfo: { } error }
                && (observation.Assumptions == assumedBefore || error is VBRuntimeErrorInfo { IsDecidedByTypes: true }))
            {
                observation.OnError(error);
            }
        }

        ObjectReferences.ReleaseTemporaries(session, temporaries);
        return complete;
    }

    // what an instruction evaluates with is the state another instruction left behind, which a sweep that follows no path does not know to be the one it
    // would find: the counter and the bounds of a loop are its opener's, and the target of a With block is the With statement's. The block variable holds
    // the object without saying whether it was assumed, so every instruction in a block is taken to rely on an assumption.
    // TODO an instruction of a With block whose target is known (With New Collection) could state its error, once the block variable says it is known.
    private static bool ReliesOnAnotherInstruction(Instruction instruction, RuntimeEvaluationContext context)
        => instruction.Kind is InstructionKind.ForNext or InstructionKind.ForEachNext
            || context.EnclosingWithTarget is not null;

    // the outcome of evaluating the instruction, or null when there was nothing to evaluate. What an instruction does to the program counter is of
    // no interest here, and a Return, a Resume or an On Error evaluates no expression of the code.
    private RuntimeExecutionOutcome? SweepInstruction(IRuntimeSession session, RuntimeEvaluationContext context, Instruction instruction, CallStackFrame activation)
        => instruction.Kind switch
        {
            InstructionKind.Simple => statements.Execute(session, context, instruction.Node!),
            InstructionKind.ConditionalBranch => ExecuteConditionalBranch(session, context, instruction, activation),
            InstructionKind.LoopBack => ExecuteLoopBack(session, context, instruction, activation),
            InstructionKind.With => ExecuteWith(session, context, instruction, activation),
            InstructionKind.EndWith => EndWithInSweep(session, instruction, activation),
            InstructionKind.Select => ExecuteSelect(session, context, instruction, activation),
            InstructionKind.ForOpener => ExecuteForOpener(session, context, instruction, activation),
            InstructionKind.ForNext => ExecuteForNext(session, context, instruction, activation),
            InstructionKind.ForEachOpener => ExecuteForEachOpener(session, context, instruction, activation),
            InstructionKind.ForEachNext => ExecuteForEachNext(session, context, instruction, activation),
            InstructionKind.RaiseError => ExecuteRaiseError(session, context, instruction),
            InstructionKind.JumpTable => ExecuteJumpTable(session, context, instruction, activation, pushReturn: false),
            InstructionKind.GoSubTable => ExecuteJumpTable(session, context, instruction, activation, pushReturn: true),
            InstructionKind.Jump or InstructionKind.ExitLoop or InstructionKind.GoSub or InstructionKind.Return or InstructionKind.ExitProcedure
                or InstructionKind.Halt or InstructionKind.Break or InstructionKind.OnErrorGoTo or InstructionKind.OnErrorDisable
                or InstructionKind.OnErrorReset or InstructionKind.OnErrorResumeNext or InstructionKind.ResumeCurrentStatement or InstructionKind.ResumeNext
                or InstructionKind.ResumeLabel => null,
            _ => RuntimeExecutionOutcome.InternalError,
        };

    private RuntimeExecutionOutcome RunInstructions(IRuntimeSession session, ICallStackFrame frame, InstructionList list, RuntimeEvaluationContext context, int temporaries,
        bool once)
    {
        var activation = (CallStackFrame)frame;

        // an activation that runs its procedure's body goes on in the edited body it is moved into while the program waits (CallStackFrame.TryRebase): after
        // it waited, and after a call it was in the middle of returns. Code run in it that is not its body - a statement typed at the break - is not moved.
        var follows = ReferenceEquals(activation.Body, list);

        // an activation compiled code handed over in the middle of an instruction goes on inside it: what was evaluated of it is known (CallStackFrame.Known),
        // and what was asked of the program before it - a break, a step, a breakpoint - is for the instruction after it, as it is after a call statement.
        var inside = activation.Known is not null;

        while (activation.Pc < list.Items.Length || (follows && !ReferenceEquals(activation.Body, list)))
        {
            if (follows && !ReferenceEquals(activation.Body, list))
            {
                list = activation.Body!;
                continue;
            }

            // the instruction before this one is over: what its expressions made is let go of. What a block holds is not: a With's target is let go of at
            // its End With, and a For Each's enumerator when the loop is over (HoldForBlock). What compiled code evaluated of an instruction it handed
            // over is let go of with its temporaries, never after them: an object it knew is held by them alone. The instruction it handed over in the middle
            // of is not over: it goes on, holding what compiled code made of it.
            var resumed = inside;
            inside = false;
            if (!resumed)
            {
                activation.Forget();
                ObjectReferences.ReleaseTemporaries(session, temporaries);
            }

            // a program waits only before an instruction that is on a line (a statement, a block's closer). The jump that ends a branch is on none: it is
            // gone through to the End If it goes to, which is where the program is shown.
            var location = BreakpointTable.LocationOf(list.Items[activation.Pc]);

            // between instructions, never inside one: a half-executed statement would leave the
            // session in a state no VBA program could have produced. This is what stops a program
            // whose own control flow never would - an empty Do…Loop, a GoTo cycle. It is a break
            // like a Stop is, which is why it is requested of the session the same way.
            if (!resumed && location is not null && cancellation.IsCancellationRequested && !_cancellationAcknowledged)
            {

                // A program that can be resumed waits here, before the instruction it was about to run, and runs it when it is resumed. The cancellation is then
                // answered: it was one break, and a token that is cancelled stays cancelled, so it is not asked about again.
                if (session.Halt.TrySuspend(RuntimeHaltKind.Break, location))
                {
                    _cancellationAcknowledged = true;
                    _ = activation.TakeMove();
                    activation.WaitedAt = activation.Pc;
                    continue;
                }

                session.Halt.Request(RuntimeHaltKind.Break, location);
                return RuntimeExecutionOutcome.Break;
            }

            // a step the program was resumed with ends here, before the instruction it was asked to stop at - and not in a procedure a breakpoint's condition calls,
            // which is the debugger's own evaluation and not the program's.
            if (!resumed && location is not null && _conditionDepth == 0 && session.Halt.TakeStep(session.CallStack.Depth))
            {
                if (!session.Halt.TrySuspend(RuntimeHaltKind.Break, location))
                {
                    session.Halt.Request(RuntimeHaltKind.Break, location);
                    return RuntimeExecutionOutcome.Break;
                }

                // waiting before the instruction, the program counter is where the program goes on from, moved or not - and in the body it was edited into.
                _ = activation.TakeMove();
                if (follows && !ReferenceEquals(activation.Body, list))
                {
                    list = activation.Body!;
                }

                if (activation.Pc >= list.Items.Length)
                {
                    continue;
                }

                activation.WaitedAt = activation.Pc;
            }

            // a breakpoint is waited at before its instruction, once: the program that is resumed from it does not wait there again. A table that cannot say
            // which instructions its lines are has no breakpoint the interpreter can wait at.
            var waitedHere = resumed || activation.WaitedAt == activation.Pc;
            activation.WaitedAt = -1;
            if (!waitedHere && _conditionDepth == 0 && session.Halt.Gate is not null
                && session.Halt.Breakpoints is IInstructionBreakpointTable { HasAny: true } breakpoints && activation.Procedure is { } owner
                && breakpoints.IsAt(owner.ParentUri.AbsoluteUri, list, activation.Pc, out var breakpointCondition)
                && IsMet(session, breakpointCondition, owner.Uri, ResolveContext(context, activation, list, list.Items[activation.Pc]), out var conditionFailure))
            {
                var breakpointLocation = BreakpointTable.LocationOf(list.Items[activation.Pc]);
                var waited = conditionFailure is null
                    ? session.Halt.TrySuspend(RuntimeHaltKind.Break, breakpointLocation)
                    : session.Halt.TrySuspendOnCondition(conditionFailure, breakpointLocation);
                if (!waited)
                {
                    session.Halt.Request(RuntimeHaltKind.Break, breakpointLocation);
                    return RuntimeExecutionOutcome.Break;
                }

                _ = activation.TakeMove();
                if (follows && !ReferenceEquals(activation.Body, list))
                {
                    list = activation.Body!;
                }

                if (activation.Pc >= list.Items.Length)
                {
                    continue;
                }

                activation.WaitedAt = activation.Pc;
            }

            // an error a Class_Terminate left unhandled stops the program after the statement that let go of the object, and no handler handles it
            // (ISessionErrorState.Untrappable).
            if (session.Errors.Untrappable is { } untrappable)
            {
                return RuntimeExecutionOutcome.Error(untrappable);
            }

            var instruction = list.Items[activation.Pc];
            var instructionContext = ResolveContext(context, activation, list, instruction);

            switch (instruction.Kind)
            {
                case InstructionKind.Simple:
                    var outcome = statements.Execute(session, instructionContext, instruction.Node!);

                    // a call the statement made returned into the edited body of this procedure: the statement is the one that made the call, where it now is.
                    if (follows && !ReferenceEquals(activation.Body, list))
                    {
                        list = activation.Body!;
                        instruction = list.Items[activation.Pc];
                    }

                    if (outcome.Kind != RuntimeExecutionOutcomeKind.Next)
                    {
                        // the statement has completed, and what it reports is that the program is to stop after it: a program that can be resumed waits here, between
                        // this instruction and the next, and goes on from the next.
                        if (outcome.Kind is RuntimeExecutionOutcomeKind.Break && WaitAfter(session, activation, instruction))
                        {
                            // the program counter may have been moved while the program waited, and then it goes on from where it was moved to.
                            if (!activation.TakeMove())
                            {
                                activation.Pc = instruction.Offset + 1;
                            }

                            break;
                        }

                        // a statement can stop the program without being one of the two that are for it - a Debug.Assert that fails breaks - and it
                        // stopped it at itself.
                        if (outcome.Kind is RuntimeExecutionOutcomeKind.Break or RuntimeExecutionOutcomeKind.Halt)
                        {
                            session.Halt.Request(
                                outcome.Kind is RuntimeExecutionOutcomeKind.Halt ? RuntimeHaltKind.End : RuntimeHaltKind.Break, instruction.Node?.SourceLocation);
                        }

                        if (InterceptError(session, activation, list, instruction.Offset, outcome) is { } unhandledSimple)
                        {
                            return unhandledSimple;
                        }
                        break; // handled - activation.Pc already redirected by InterceptError.
                    }
                    activation.Pc = instruction.Offset + 1;
                    break;

                case InstructionKind.Jump:
                case InstructionKind.ExitLoop:
                    if (instruction.Target is not { } target)
                    {
                        // an unresolved label already reported its own VBC09309 at lowering time; an
                        // Exit For/Do with no enclosing loop of the matching kind is a real static gap
                        // (no VBC id wired yet) that lowering also leaves unresolved rather than guess.
                        return RuntimeExecutionOutcome.InternalError;
                    }

                    LeaveLoops(session, activation, instruction.Offset, target);
                    activation.Pc = target;
                    break;

                case InstructionKind.ConditionalBranch:
                    var branchOutcome = ExecuteConditionalBranch(session, instructionContext, instruction, activation);
                    if (branchOutcome is { } stop && InterceptError(session, activation, list, instruction.Offset, stop) is { } unhandledBranch)
                    {
                        return unhandledBranch;
                    }
                    break;

                case InstructionKind.LoopBack:
                    var loopBackOutcome = ExecuteLoopBack(session, instructionContext, instruction, activation);
                    if (loopBackOutcome is { } stopLoopBack && InterceptError(session, activation, list, instruction.Offset, stopLoopBack) is { } unhandledLoopBack)
                    {
                        return unhandledLoopBack;
                    }
                    break;

                case InstructionKind.With:
                    var withOutcome = ExecuteWith(session, instructionContext, instruction, activation);
                    if (withOutcome is { } stopWith && InterceptError(session, activation, list, instruction.Offset, stopWith) is { } unhandledWith)
                    {
                        return unhandledWith;
                    }
                    break;

                case InstructionKind.EndWith:
                    ExecuteEndWith(session, instruction, activation);
                    activation.Pc = instruction.Offset + 1;
                    break;

                case InstructionKind.Select:
                    var selectOutcome = ExecuteSelect(session, instructionContext, instruction, activation);
                    if (selectOutcome is { } stopSelect && InterceptError(session, activation, list, instruction.Offset, stopSelect) is { } unhandledSelect)
                    {
                        return unhandledSelect;
                    }
                    break;

                case InstructionKind.ForOpener:
                    var forOpenerOutcome = ExecuteForOpener(session, instructionContext, instruction, activation);
                    if (forOpenerOutcome is { } stopForOpener && InterceptError(session, activation, list, instruction.Offset, stopForOpener) is { } unhandledForOpener)
                    {
                        return unhandledForOpener;
                    }
                    break;

                case InstructionKind.ForNext:
                    var forNextOutcome = ExecuteForNext(session, instructionContext, instruction, activation);
                    if (forNextOutcome is { } stopForNext && InterceptError(session, activation, list, instruction.Offset, stopForNext) is { } unhandledForNext)
                    {
                        return unhandledForNext;
                    }
                    break;

                case InstructionKind.ForEachOpener:
                    var forEachOpenerOutcome = ExecuteForEachOpener(session, instructionContext, instruction, activation);
                    if (forEachOpenerOutcome is { } stopForEachOpener && InterceptError(session, activation, list, instruction.Offset, stopForEachOpener) is { } unhandledForEachOpener)
                    {
                        return unhandledForEachOpener;
                    }
                    break;

                case InstructionKind.ForEachNext:
                    var forEachNextOutcome = ExecuteForEachNext(session, instructionContext, instruction, activation);
                    if (forEachNextOutcome is { } stopForEachNext && InterceptError(session, activation, list, instruction.Offset, stopForEachNext) is { } unhandledForEachNext)
                    {
                        return unhandledForEachNext;
                    }
                    break;

                case InstructionKind.OnErrorGoTo:
                case InstructionKind.OnErrorDisable:
                case InstructionKind.OnErrorReset:
                case InstructionKind.OnErrorResumeNext:
                    if (!TryApplyOnError(session, activation, instruction))
                    {
                        return RuntimeExecutionOutcome.InternalError;
                    }
                    activation.Pc = instruction.Offset + 1;
                    break;

                case InstructionKind.ResumeCurrentStatement:
                case InstructionKind.ResumeNext:
                case InstructionKind.ResumeLabel:
                    var resumeOutcome = ExecuteResume(list, instruction, activation);
                    if (resumeOutcome is null)
                    {
                        // a Resume that actually resumed; §6.1.3.2.1 clears Err with it.
                        session.Errors.Clear();
                    }
                    if (resumeOutcome is { } stopResume && InterceptError(session, activation, list, instruction.Offset, stopResume) is { } unhandledResume)
                    {
                        return unhandledResume;
                    }
                    break;

                case InstructionKind.RaiseError:
                    var raiseOutcome = ExecuteRaiseError(session, instructionContext, instruction);
                    if (InterceptError(session, activation, list, instruction.Offset, raiseOutcome) is { } unhandledRaise)
                    {
                        return unhandledRaise;
                    }
                    break;

                case InstructionKind.JumpTable:
                    var jumpTableOutcome = ExecuteJumpTable(session, instructionContext, instruction, activation, pushReturn: false);
                    if (jumpTableOutcome is { } stopJumpTable && InterceptError(session, activation, list, instruction.Offset, stopJumpTable) is { } unhandledJumpTable)
                    {
                        return unhandledJumpTable;
                    }
                    break;

                case InstructionKind.GoSubTable:
                    var goSubTableOutcome = ExecuteJumpTable(session, instructionContext, instruction, activation, pushReturn: true);
                    if (goSubTableOutcome is { } stopGoSubTable && InterceptError(session, activation, list, instruction.Offset, stopGoSubTable) is { } unhandledGoSubTable)
                    {
                        return unhandledGoSubTable;
                    }
                    break;

                case InstructionKind.GoSub:
                    if (instruction.Target is not { } goSubTarget)
                    {
                        // an unresolved label already reported its own VBC09309 at lowering time.
                        return RuntimeExecutionOutcome.InternalError;
                    }
                    activation.PushGoSubReturn(instruction.Offset + 1);
                    activation.Pc = goSubTarget;
                    break;

                case InstructionKind.Return:
                    var returnOutcome = ExecuteReturn(instruction, activation);
                    if (returnOutcome is { } stopReturn && InterceptError(session, activation, list, instruction.Offset, stopReturn) is { } unhandledReturn)
                    {
                        return unhandledReturn;
                    }
                    break;

                // an Exit goes through the End statement, where a breakpoint or a step stops before the procedure returns.
                case InstructionKind.ExitProcedure when instruction.Target is { } end:
                    activation.Pc = end;
                    break;

                case InstructionKind.ExitProcedure:
                    ClearHandledError(session, activation);
                    return RuntimeExecutionOutcome.ExitProcedure;

                case InstructionKind.Halt:
                    session.Halt.Request(RuntimeHaltKind.End, instruction.Node?.SourceLocation);
                    return RuntimeExecutionOutcome.Halt;

                case InstructionKind.Break:
                    if (WaitAfter(session, activation, instruction))
                    {
                        if (!activation.TakeMove())
                        {
                            activation.Pc = instruction.Offset + 1;
                        }

                        break;
                    }

                    session.Halt.Request(RuntimeHaltKind.Break, instruction.Node?.SourceLocation);
                    return RuntimeExecutionOutcome.Break;

                default:
                    return RuntimeExecutionOutcome.InternalError;
            }

            // one instruction run (Step): the activation goes on at its Pc, wherever the instruction or its error's handler left it.
            if (once)
            {
                return RuntimeExecutionOutcome.Next;
            }
        }

        // fell off the end of the list - MS-VBAL §5.4.2.17: "completes as if execution had reached
        // the end of the body" is the same outcome as an explicit Exit.
        ClearHandledError(session, activation);
        return RuntimeExecutionOutcome.ExitProcedure;
    }

    // waits after the instruction, which has run: unless the program is moved while it waits, it goes on from the instruction after it.
    private static bool WaitAfter(IRuntimeSession session, CallStackFrame activation, Instruction instruction)
    {
        activation.WaitsAfterPc = true;
        try
        {
            return session.Halt.TrySuspend(RuntimeHaltKind.Break, instruction.Node?.SourceLocation);
        }
        finally
        {
            activation.WaitsAfterPc = false;
        }
    }

    // what an On Error statement makes the activation's policy. In a handler it is the policy once the handler is over, and nothing more: the error is still the
    // one being handled, which a Resume still resumes, and a second error in the handler still goes to the caller (MS-VBA, observed in Excel).
    // Anywhere else, it is the policy from now on.
    private static ErrorHandlerState PolicyOf(ErrorHandlerState current, ErrorHandlingMode mode, int? target)
        => current.IsHandling
            ? current with { HandlerTarget = target, AfterHandling = mode }
            : new ErrorHandlerState(mode, target, null, null);

    /// <summary>
    /// What the <c>On Error</c> statement <paramref name="instruction"/> does (MS-VBAL §5.4.4): the activation's error handling policy set, and the Err
    /// object cleared (§6.1.3.2.1). Compiled code does it too.
    /// </summary>
    /// <returns><see langword="false"/> for an <c>On Error GoTo</c> whose label was not resolved, which reported its own compile error.</returns>
    internal static bool TryApplyOnError(IRuntimeSession session, CallStackFrame activation, Instruction instruction)
    {
        switch (instruction.Kind)
        {
            case InstructionKind.OnErrorGoTo:
                if (instruction.Target is not { } handlerTarget)
                {
                    return false;
                }
                activation.ErrorHandler = PolicyOf(activation.ErrorHandler, ErrorHandlingMode.GoTo, handlerTarget);
                break;

            case InstructionKind.OnErrorDisable:
                // On Error GoTo 0 is an On Error statement too, and §6.1.3.2.1 makes no exception for it.
                activation.ErrorHandler = PolicyOf(activation.ErrorHandler, ErrorHandlingMode.Disabled, null);
                break;

            case InstructionKind.OnErrorReset:
                // On Error GoTo -1 ends the error being handled, and keeps the policy: in a handler, the one it is to have after it - the On Error GoTo
                // label it caught the error with, unless it said otherwise - and Resume has nothing to resume (MS-VBA, observed in Excel).
                activation.ErrorHandler = activation.ErrorHandler.IsHandling
                    ? activation.ErrorHandler.HandlingOver()
                    : activation.ErrorHandler with { ActiveError = null, FaultStatementOffset = null };
                break;

            case InstructionKind.OnErrorResumeNext:
                activation.ErrorHandler = PolicyOf(activation.ErrorHandler, ErrorHandlingMode.ResumeNext, null);
                break;

            default:
                return false;
        }

        // MS-VBAL §6.1.3.2.1: an On Error statement clears the Err object.
        session.Errors.Clear();
        return true;
    }

    // MS-VBAL §6.1.3.2.1: Exit Sub, Exit Function and Exit Property clear Err. In MS-VBA, reaching the end of the body does too, and either does only from
    // within an error handler - an error On Error GoTo caught: one On Error Resume Next went on past is still Err when the procedure returns (observed in
    // Excel). Compiled code's Exit does it too.
    internal static void ClearHandledError(IRuntimeSession session, CallStackFrame activation)
    {
        if (activation.ErrorHandler is { ActiveError: not null, Mode: not ErrorHandlingMode.ResumeNext })
        {
            _ = session.Errors.Clear();
        }
    }

    // A purely lexical property of the instruction about to run, not something that should persist from
    // whatever the previous instruction's own context happened to carry - a GoTo into or out of a With
    // block leaves no stale stack, so this is recomputed fresh every fetch rather than pushed/popped.
    // An instruction of a block whose With statement did not set a target - jumped into, its target in error and resumed past, or past its End With and
    // jumped back into - has Nothing for one: a member with no object before the dot is error 91 (MS-VBA, observed in Excel).
    // Compiled code evaluates a statement of a With block in the context this gives it too (CompiledActivation.ContextAt).
    internal static RuntimeEvaluationContext ResolveContext(
        RuntimeEvaluationContext outer, CallStackFrame activation, InstructionList list, Instruction instruction)
        => instruction.EnclosingWith is { } openerOffset
            ? outer with
            {
                EnclosingWithTarget = activation.TryGetBlockState(openerOffset, out var target) ? target : VBObjectValue.Nothing,
                EnclosingWithTargetExpression = (list.Items[openerOffset].Node as WithStatementNode)?.WithExpression,
            }
            : outer with { EnclosingWithTarget = null, EnclosingWithTargetExpression = null };

    // Returns null when the branch was taken and the loop should keep running (activation.Pc is
    // already set correctly); returns a non-null outcome only when execution must stop.
    private RuntimeExecutionOutcome? ExecuteConditionalBranch(IRuntimeSession session, RuntimeEvaluationContext context, Instruction instruction, CallStackFrame activation)
    {
        if (instruction.Matching is { } selectOffset)
        {
            return ExecuteCaseHeader(session, context, instruction, activation, selectOffset);
        }

        var (condition, negate) = GetCondition(instruction.Node);
        if (condition is null)
        {
            // a lowering bug - every ConditionalBranch is either a Case header (Matching set) or one of
            // the If-family/pre-test-loop node kinds GetCondition recognizes.
            return RuntimeExecutionOutcome.InternalError;
        }

        var conditionResult = conditions.EvaluateBoolean(session, condition, context);
        return Branch(activation, instruction, negate ? Negate(conditionResult) : conditionResult);
    }

    // Returns null when the loop should keep running (activation.Pc already set, either back to the
    // body's first instruction or past the loop) and a non-null outcome only when execution must stop.
    private RuntimeExecutionOutcome? ExecuteLoopBack(IRuntimeSession session, RuntimeEvaluationContext context, Instruction instruction, CallStackFrame activation)
    {
        var (condition, negate) = GetCondition(instruction.Node);
        if (condition is null)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        var conditionResult = conditions.EvaluateBoolean(session, condition, context);
        if (negate)
        {
            conditionResult = Negate(conditionResult);
        }

        if (!conditionResult.IsSuccess)
        {
            return conditionResult.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(conditionResult.ErrorInfo!);
        }

        if (((VBBooleanValue)conditionResult.Result!).Value.StoredValue != 0)
        {
            // MS-VBAL §5.4.2.6: the loop continues - branch back to the body's first instruction.
            if (instruction.Target is not { } target)
            {
                // lowering always sets Target on a LoopBack instruction - reaching here is a lowering bug.
                return RuntimeExecutionOutcome.InternalError;
            }
            activation.Pc = target;
        }
        else
        {
            activation.Pc = instruction.Offset + 1; // false - fall through, the loop ends here.
        }
        return null;
    }

    // Do…Loop Until / Do Until…Loop / While…Wend's own opposite polarity (exits on True rather than
    // False) is the only difference from an If/ElseIf/inline-If condition - everything else about
    // evaluating and branching on it is identical, so this inverts the already-evaluated result rather
    // than duplicating Branch's own dispatch for a second polarity.
    private static RuntimeSemanticsEvaluationResult Negate(RuntimeSemanticsEvaluationResult result)
        => result.IsSuccess
            ? RuntimeSemanticsEvaluationResult.Success(((VBBooleanValue)result.Result!).Value.StoredValue != 0 ? VBBooleanValue.False : VBBooleanValue.True)
            : result;

    // Returns null when the target was evaluated and stashed successfully and the loop should keep
    // running (activation.Pc already falls through into the first Case header); returns a non-null
    // outcome only when execution must stop.
    private RuntimeExecutionOutcome? ExecuteSelect(IRuntimeSession session, RuntimeEvaluationContext context, Instruction instruction, CallStackFrame activation)
    {
        if (instruction.Node is not SelectCaseStatementNode selectCase)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        // MS-VBAL §5.4.2.10: "the select-expression is immediately evaluated" - once, ahead of every
        // Case header's own range-clause matching against it. A selector evaluated on payloads is kept as one.
        if (cases.TryEvaluateSelectorUnboxed(session, selectCase.ControlExpression, context, out var payload))
        {
            activation.SetSelectorPayload(instruction.Offset, payload);
            activation.Pc = instruction.Offset + 1;
            return null;
        }

        var selectorResult = cases.EvaluateSelector(session, selectCase.ControlExpression, context);
        if (!selectorResult.IsSuccess)
        {
            return selectorResult.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(selectorResult.ErrorInfo!);
        }

        activation.SetBlockState(instruction.Offset, selectorResult.Result!);
        activation.Pc = instruction.Offset + 1;
        return null;
    }

    // Returns null when the branch was taken and the loop should keep running; returns a non-null
    // outcome only when execution must stop.
    private RuntimeExecutionOutcome? ExecuteCaseHeader(IRuntimeSession session, RuntimeEvaluationContext context, Instruction instruction, CallStackFrame activation, int selectOffset)
    {
        // a selector kept as a payload is compared as one; a Case it is not is compared boxed, with the selector made a value of it.
        VBTypedValue? selector = null;
        if (instruction.Node is CaseExpressionStatementNode payloadCase && activation.TryGetSelectorPayload(selectOffset, out var payload))
        {
            if (cases.TryMatchUnboxed(session, payloadCase, payload, context, out var isMatch))
            {
                return Branch(activation, instruction, RuntimeSemanticsEvaluationResult.Success(isMatch ? VBBooleanValue.True : VBBooleanValue.False));
            }

            selector = payload.Typed();
        }

        if (instruction.Node is not CaseExpressionStatementNode caseBlock || (selector is null && !activation.TryGetBlockState(selectOffset, out selector)))
        {
            // TryGetBlockState failing here means the enclosing Select's own opener never ran - a
            // lowering/executor desync, not a reachable program state.
            return RuntimeExecutionOutcome.InternalError;
        }

        // a Variant selector's own TypeInfo mirrors its wrapped value's, but stays a VBVariantValue
        // instance - unwrap it here (recursively) so a Null selector held in a Variant is recognized the
        // same way a directly-typed one would be. The clauses are compared with the Variant itself: its
        // value is compared as a Variant's, and the value of a clause is not converted to the type of what
        // it holds (RD-VBAL 5.4.2.10).
        var held = selector;
        while (held is VBVariantValue { TypedValue: var wrapped })
        {
            held = wrapped;
        }

        if (held is VBNullValue)
        {
            // MS-VBAL §5.4.2.10: "If select-expression is the data value Null, only the case-else-clause
            // is executed" - every Case header treats itself as non-matching, without evaluating any of
            // its own range clauses, so control falls all the way through to Case Else (or past the
            // whole construct if there is none) via the same Else-chain every header already carries.
            activation.Pc = instruction.Else ?? instruction.Offset + 1;
            return instruction.Else is null ? RuntimeExecutionOutcome.InternalError : null;
        }

        var matchResult = cases.Evaluate(session, caseBlock, selector, context);
        return Branch(activation, instruction, matchResult);
    }

    // Shared by an If-family header and a Case header alike: both resolve to a Boolean "did this branch
    // match" result and react to it identically - fall through into the body on True, go to Else on
    // False, propagate an error/internal error as-is.
    private static RuntimeExecutionOutcome? Branch(CallStackFrame activation, Instruction instruction, RuntimeSemanticsEvaluationResult matchResult)
    {
        if (!matchResult.IsSuccess)
        {
            return matchResult.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(matchResult.ErrorInfo!);
        }

        if (((VBBooleanValue)matchResult.Result!).Value.StoredValue != 0)
        {
            activation.Pc = instruction.Offset + 1; // true - fall through into the branch's own body.
            return null;
        }

        if (instruction.Else is not { } elseTarget)
        {
            // lowering always sets Else on a ConditionalBranch header - reaching here is a lowering bug.
            return RuntimeExecutionOutcome.InternalError;
        }

        activation.Pc = elseTarget;
        return null;
    }

    // Returns null when the target was evaluated, coerced, and stashed successfully and the loop should
    // keep running (activation.Pc already falls through into the body); returns a non-null outcome only
    // when execution must stop.
    private RuntimeExecutionOutcome? ExecuteWith(IRuntimeSession session, RuntimeEvaluationContext context, Instruction instruction, CallStackFrame activation)
    {
        if (instruction.Node is not WithStatementNode withStatement)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        var targetResult = withTargets.Evaluate(session, withStatement, context);
        if (!targetResult.IsSuccess)
        {
            return targetResult.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(targetResult.ErrorInfo!);
        }

        // MS-VBAL §5.4.2.21: "If <expression> is classified as a variable, that variable is the With block variable" - which MS-VBA holds to for a
        // record, whose fields the block writes where they are. An object it holds as a value is held: the reference the variable held when the block
        // began, which a Set of the variable in the block does not change (observed in Excel), and which the block holds until
        // its End With - "Nothing is assigned to the anonymous With block variable".
        if (IsRecord(targetResult.Result!) && withTargets.DenotesAVariable(session, context, withStatement.WithExpression))
        {
            activation.SetBlockState(instruction.Offset, targetResult.Result!);
        }
        else
        {
            var target = DataValues.Copy(targetResult.Result!);
            activation.SetBlockState(instruction.Offset, target);
            HoldForBlock(session, activation, instruction, target);
        }

        activation.Pc = instruction.Offset + 1;
        return null;
    }

    private static bool IsRecord(VBTypedValue value) => value is VBUserDefinedTypeValue or VBVariantValue { TypedValue: VBUserDefinedTypeValue };

    private static RuntimeExecutionOutcome? EndWithInSweep(IRuntimeSession session, Instruction instruction, CallStackFrame activation)
    {
        ExecuteEndWith(session, instruction, activation);
        return null;
    }

    // MS-VBAL §5.4.2.21: End With assigns Nothing to the block variable. The block has no target from here on - a jump back into it finds none, and a
    // member read with no object before the dot is error 91 (MS-VBA, observed in Excel) - and lets go of what it held.
    private static void ExecuteEndWith(IRuntimeSession session, Instruction instruction, CallStackFrame activation)
    {
        if (instruction.Matching is not { } opener)
        {
            return;
        }

        activation.ClearBlockState(opener);
        if (activation.TakeBlockHolder(opener) is { } held)
        {
            ObjectReferences.Release(session, held);
        }
    }

    // MS-VBAL §5.4.2.21: the object of a With block is held by the block, "the With block variable", until its End With - whatever the statements in
    // it do to the variable it was read from, and wherever the program jumps meanwhile: a jump out of the block and back in finds it (MS-VBA, observed in
    // Excel). A For Each holds what it enumerates the same way while the loop runs: its enumerator, or the array whose elements it reads. The loop goes on
    // over it after a GoSub out of the loop returns, or a handler past it resumes; it is let go of when the loop is over - its Next finds no element left,
    // or an Exit For or a jump leaves it (LeaveLoops) - or is started again, which lets go of the one it held before, and the array is locked only while the
    // loop runs (CallStackFrame.LocksArray). What is still held is let go of when the activation is (RuntimeProcedureInvoker).
    private static void HoldForBlock(IRuntimeSession session, CallStackFrame activation, Instruction opener, VBTypedValue value)
    {
        if (opener.End is not { } end || value is not (VBObjectValue or VBVariantValue or VBArrayValue))
        {
            return;
        }

        Hold(session, activation.BlockHolder(opener.Offset, opener.Kind is InstructionKind.With ? null : end), value);
    }

    // what the collection expression of a For Each made - a function's result, a property's value - is held as a hidden local of the activation, until it
    // returns or the loop is started again, after the loop is over (MS-VBA, observed in Excel): the objects of an array a function returned outlive the loop.
    // A variable is not held: what it holds goes when the variable lets go of it, once the loop no longer holds it.
    private static void HoldForEachSource(IRuntimeSession session, CallStackFrame activation, Instruction opener, ExpressionNode expression, VBTypedValue value,
        RuntimeEvaluationContext context)
    {
        if (value is not (VBObjectValue or VBVariantValue or VBArrayValue) || expression is SimpleNameExpressionNode name
            && session.Symbols.Resolver.ResolveValue(name, ScopeKind.Local, context.Scope).Symbol is VBLocalVariableSymbol or VBParameterSymbol
                or VBModuleFieldVariableMemberSymbol or VBInstanceFieldVariableMemberSymbol)
        {
            return;
        }

        Hold(session, activation.SourceHolder(opener.Offset), value);
    }

    private static void Hold(IRuntimeSession session, ValueBindingHandle cell, VBTypedValue value)
    {
        var before = ObjectReferences.HeldIn(session, cell);
        cell.Store(value is VBArrayValue ? SymbolAddressTable.BoxedValue(value) : value.RuntimeValue, !value.IsIndeterminate);
        ObjectReferences.Rebind(session, cell, before);
    }

    // a jump from inside a For Each to past it - an Exit For, a GoTo - ends the loop: what it held is let go of.
    internal static void LeaveLoops(IRuntimeSession session, CallStackFrame activation, int from, int to)
    {
        foreach (var held in activation.TakeLoopHoldersLeft(from, to))
        {
            ObjectReferences.Release(session, held);
        }
    }

    // a For Each whose Next finds no element left is over: what it held is let go of.
    private static void EndLoop(IRuntimeSession session, CallStackFrame activation, int opener)
    {
        if (activation.TakeBlockHolder(opener) is { } held)
        {
            ObjectReferences.Release(session, held);
        }
    }

    // Returns null when start/end/step were evaluated and stashed successfully and the loop should keep
    // running (activation.Pc set to the body's first instruction, or skipped straight past the whole
    // construct when already out of range on entry); returns a non-null outcome only when execution
    // must stop.
    private RuntimeExecutionOutcome? ExecuteForOpener(IRuntimeSession session, RuntimeEvaluationContext context, Instruction instruction, CallStackFrame activation)
    {
        if (instruction.Node is not ForStatementNode forStatement || forStatement.ControlExpression is not SimpleNameExpressionNode simpleName)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        var counterResult = session.Symbols.Resolver.ResolveValue(simpleName, ScopeKind.Local, context.Scope);
        if (counterResult.Symbol is not { } counterSymbol)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        // the loop counts in one numeric type, which the start, the end and the step are Let-coerced to.
        var counterType = counterSymbol is ITypedSymbol typed ? typed.ResolvedType : VBVariantType.TypeInfo;
        var startResult = forLoop.EvaluateOperand(session, forStatement.StartExpression, counterType, context);
        if (!startResult.IsSuccess)
        {
            return Unevaluated(session, instruction, activation, forStatement, counterSymbol, counterType, ToFailureOutcome(startResult));
        }

        var endResult = forLoop.EvaluateOperand(session, forStatement.EndExpression, counterType, context);
        if (!endResult.IsSuccess)
        {
            return Unevaluated(session, instruction, activation, forStatement, counterSymbol, counterType, ToFailureOutcome(endResult));
        }

        // MS-VBAL §5.4.2.3: "if no step-clause is present, the step-increment value is the integer data
        // value 1" - a static default, never itself evaluated as a source expression (ForLoopEvaluator.Operands).
        VBTypedValue? step = null;
        if (forStatement.StepExpression is { } stepExpression)
        {
            var stepResult = forLoop.EvaluateOperand(session, stepExpression, counterType, context);
            if (!stepResult.IsSuccess)
            {
                return Unevaluated(session, instruction, activation, forStatement, counterSymbol, counterType, ToFailureOutcome(stepResult));
            }

            step = stepResult.Result!;
        }

        if (forLoop.Operands(session, forStatement, counterType, startResult.Result!, endResult.Result!, step, out var counted) is { } operandError)
        {
            return Unevaluated(session, instruction, activation, forStatement, counterSymbol, counterType,
                ToFailureOutcome(RuntimeSemanticsEvaluationResult.Error(operandError)));
        }

        var state = new ForLoopState(counterSymbol, forStatement.ControlExpression, counted.End, counted.Step)
        {
            CountsDown = ForLoopEvaluator.CountsDown(counted.Step),
            // what stepping on payloads keeps from one Next to the next: filled by the first.
            RuntimeState = new ForLoopPayloads(),
        };

        var assignResult = forLoop.AssignCounter(session, state, forStatement.StartExpression, counted.Start, context);
        if (!assignResult.IsSuccess)
        {
            return Unevaluated(session, instruction, activation, forStatement, counterSymbol, counterType, ToFailureOutcome(assignResult));
        }

        // stashed before the entry test below, exactly as MS-VBAL frames it: start/end/step are
        // considered evaluated the moment the opener runs, regardless of whether the body ever executes.
        activation.SetForLoopState(instruction.Offset, state);

        var rangeResult = forLoop.IsOutOfRange(session, state, assignResult.Result!);
        if (!rangeResult.IsSuccess)
        {
            return ToFailureOutcome(rangeResult);
        }

        if (rangeResult.Result is not VBBooleanValue isOutOfRange)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        if (isOutOfRange.Value.StoredValue != 0)
        {
            // steps 1/2: already out of range - the body never runs at all.
            if (instruction.End is not { } end)
            {
                return RuntimeExecutionOutcome.InternalError;
            }
            activation.Pc = end;
        }
        else
        {
            activation.Pc = instruction.Offset + 1;
        }
        return null;
    }

    // MS-VBAL §5.4.4.1: a For statement whose start, end or step raised an error, or whose counter could not be given the start, keeps nothing of what it
    // evaluated - the counter is not assigned, and its loop has the end and the step it had before. Under Resume Next its body runs, untested, and its Next
    // steps the counter with them: what an earlier pass through the statement left, or a 0 end and a 0 step (a step of 1 for a loop that writes none) when
    // there was none (MS-VBA, observed in Excel).
    private RuntimeExecutionOutcome Unevaluated(IRuntimeSession session, Instruction instruction, CallStackFrame activation, ForStatementNode forStatement,
        Symbol counterSymbol, VBType counterType, RuntimeExecutionOutcome failure)
    {
        if (!activation.TryGetForLoopState(instruction.Offset, out _)
            && forLoop.Operands(session, forStatement, counterType, new VBIntegerValue(0), new VBIntegerValue(0),
                forStatement.StepExpression is null ? null : new VBIntegerValue(0), out var unset) is null)
        {
            activation.SetForLoopState(instruction.Offset, new ForLoopState(counterSymbol, forStatement.ControlExpression, unset.End, unset.Step)
            {
                CountsDown = ForLoopEvaluator.CountsDown(unset.Step),
                RuntimeState = new ForLoopPayloads(),
            });
        }

        return failure;
    }

    // Returns null when the counter was incremented and re-tested successfully and the loop should keep
    // running (activation.Pc branched back to the body, or fallen through past the loop); returns a
    // non-null outcome only when execution must stop.
    private RuntimeExecutionOutcome? ExecuteForNext(IRuntimeSession session, RuntimeEvaluationContext context, Instruction instruction, CallStackFrame activation)
    {
        if (instruction.Matching is not { } openerOffset || !activation.TryGetForLoopState(openerOffset, out var state))
        {
            // MS-VBAL §5.4.2.3: a GoTo landed directly on this Next without its own ForOpener ever
            // running this activation - error 92, "For loop not initialized".
            return RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.ForLoopNotInitialized,
                instruction.Location ?? default, Exceptions.VBForLoopNotInitialized_Verbose));
        }

        // a counter that holds a payload is stepped on payloads; a step that would raise an error is the boxed path's to raise.
        if (forLoop.TryStepUnboxed(session, state, context, out var isPastTheEnd))
        {
            if (isPastTheEnd)
            {
                activation.Pc = instruction.Offset + 1;
                return null;
            }

            if (instruction.Target is not { } loopTarget)
            {
                return RuntimeExecutionOutcome.InternalError;
            }

            activation.Pc = loopTarget;
            return null;
        }

        var counterResult = forLoop.EvaluateOperand(session, state.ControlExpression, context);
        if (!counterResult.IsSuccess)
        {
            return AtTheNext(instruction, state, ToFailureOutcome(counterResult));
        }

        var sumResult = forLoop.Increment(session, state, counterResult.Result!);
        if (!sumResult.IsSuccess)
        {
            return AtTheNext(instruction, state, ToFailureOutcome(sumResult));
        }

        var assignResult = forLoop.AssignCounter(session, state, state.ControlExpression, sumResult.Result!, context);
        if (!assignResult.IsSuccess)
        {
            return AtTheNext(instruction, state, ToFailureOutcome(assignResult));
        }

        var rangeResult = forLoop.IsOutOfRange(session, state, assignResult.Result!);
        if (!rangeResult.IsSuccess)
        {
            return AtTheNext(instruction, state, ToFailureOutcome(rangeResult));
        }

        if (rangeResult.Result is not VBBooleanValue isOutOfRange)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        if (isOutOfRange.Value.StoredValue != 0)
        {
            activation.Pc = instruction.Offset + 1; // out of range - the loop ends here.
        }
        else
        {
            if (instruction.Target is not { } target)
            {
                // lowering always sets Target on a ForNext instruction - reaching here is a lowering bug.
                return RuntimeExecutionOutcome.InternalError;
            }
            activation.Pc = target;
        }
        return null;
    }

    // MS-VBAL §5.4.2.3: what a Next does - read the counter, add the step to it, assign it the sum, test it with the end - it does at the counter's expression,
    // the For's, as it has none of its own; an error one of them raises there is the Next's, and is located at it (Instruction.Location), where MS-VBA locates
    // it. An error raised elsewhere - in a member the counter's value calls - keeps its own location.
    private static RuntimeExecutionOutcome AtTheNext(Instruction instruction, ForLoopState state, RuntimeExecutionOutcome failure)
        => failure is { Kind: RuntimeExecutionOutcomeKind.Error, ErrorInfo: { } error } && error.Location == state.ControlExpression.Location
            && instruction.Location is { } next
            ? RuntimeExecutionOutcome.Error((IVBRaisableError)(error.AsErrorInfo with { Location = next }))
            : failure;

    // a For Each statement or its Next, run for compiled code as this runs it (CompiledActivation.StepForEach): null when the program goes on, at the instruction
    // activation.Pc says, and the outcome only when it must stop.
    internal RuntimeExecutionOutcome? ExecuteForEach(IRuntimeSession session, RuntimeEvaluationContext context, Instruction instruction, CallStackFrame activation)
        => instruction.Kind is InstructionKind.ForEachOpener
            ? ExecuteForEachOpener(session, context, instruction, activation)
            : ExecuteForEachNext(session, context, instruction, activation);

    // Returns null when the collection was evaluated and stashed successfully and the loop should keep
    // running (activation.Pc set to the body's first instruction, or skipped straight past the whole
    // construct when the array is empty); returns a non-null outcome only when execution must stop.
    private RuntimeExecutionOutcome? ExecuteForEachOpener(IRuntimeSession session, RuntimeEvaluationContext context, Instruction instruction, CallStackFrame activation)
    {
        if (instruction.Node is not ForEachStatementNode forEachStatement || forEachStatement.ControlExpression is not SimpleNameExpressionNode simpleName)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        var controlResult = session.Symbols.Resolver.ResolveValue(simpleName, ScopeKind.Local, context.Scope);
        if (controlResult.Symbol is not ITypedSymbol control)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        var collectionResult = forEach.EvaluateCollection(session, forEachStatement.CollectionExpression, context);
        if (!collectionResult.IsSuccess)
        {
            return ToFailureOutcome(collectionResult);
        }

        HoldForEachSource(session, activation, instruction, forEachStatement.CollectionExpression, collectionResult.Result!, context);

        // a Variant holding an array (or an object) reports its own TypeInfo as the wrapped value's,
        // but stays a VBVariantValue instance - unwrap it here (recursively) so "For Each x In v" on a
        // Variant works the same as on a declared array/object collection.
        var assumedBefore = forEach.AssumedSoFar;
        var outcome = StartEnumeration(session, context, instruction, activation, forEachStatement, control, collectionResult.Result!);
        forEach.ObserveEnumeration(session, context, forEachStatement.CollectionExpression, collectionResult.Result!, assumedBefore, outcome);
        return outcome;
    }

    private RuntimeExecutionOutcome? StartEnumeration(
        IRuntimeSession session, RuntimeEvaluationContext context, Instruction instruction, CallStackFrame activation, ForEachStatementNode forEachStatement,
        ITypedSymbol control, VBTypedValue? collection)
    {
        while (collection is VBVariantValue { TypedValue: var wrapped })
        {
            collection = wrapped;
        }

        switch (collection)
        {
            case VBArrayValue { IsInitialized: false }:
                // A Dim'd-but-never-ReDim'd array has no SAFEARRAY behind it at all - not "an array with
                // no elements" (MS-VBAL §5.4.2.4's silent-skip case below). Real VBA raises error 92 here,
                // the same "For loop not initialized" ForNext/ForEachNext already report on a GoTo landing
                // directly on the closer - the collection itself was never properly set up either way.
                return RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.ForLoopNotInitialized,
                    forEachStatement.CollectionExpression.Location, Exceptions.VBForEach_ArrayNotInitialized_Verbose));

            case VBArrayValue array:
                if (array.Length == 0)
                {
                    // MS-VBAL §5.4.2.4: "if the array has no elements, execution completes immediately" -
                    // a real, initialized array whose declared bounds just happen to hold zero elements. It completes with its control variable emptied,
                    // as one that ran out has it (RD-VBAL §5.4.2.4).
                    if (instruction.End is not { } emptyEnd)
                    {
                        return RuntimeExecutionOutcome.InternalError;
                    }

                    if (EmptyControl(session, control, forEachStatement.ControlExpression) is { } notEmptied)
                    {
                        return notEmptied;
                    }

                    activation.Pc = emptyEnd;
                    return null;
                }

                var firstElement = array.ElementAt(0)!;
                var assignResult = forEach.AssignControl(session, control, forEachStatement.ControlExpression,
                    ForEachEvaluator.IsSetAssigned(array.ItemType, firstElement), firstElement);
                if (!assignResult.IsSuccess)
                {
                    return ToFailureOutcome(assignResult);
                }

                activation.SetForEachState(instruction.Offset, new ForEachState(control, forEachStatement.ControlExpression, array, 0));
                HoldForBlock(session, activation, instruction, array);
                activation.Pc = instruction.Offset + 1;
                return null;

            case VBObjectValue objectValue when objectValue.IsNothing():
                // enumerating an object collection means invoking its _NewEnum member; on Nothing that
                // invocation itself would raise error 91.
                return RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet,
                    forEachStatement.CollectionExpression.Location, Exceptions.VBForEach_ObjectVariableNotSet_Verbose));

            case VBObjectValue objectValue when RuntimeExpressionEvaluator.HasEnumerationMember(session, objectValue):
                // an object that has an enumeration member (VB_UserMemId = -4, commonly "_NewEnum") is enumerated by what it returns.
                return StartObjectEnumeration(session, context, instruction, activation, forEachStatement, control, objectValue);

            case VBObjectValue:
                // a live object with no enumeration member - MS-VBAL §5.4.2.4 requires one.
                return RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod,
                    forEachStatement.CollectionExpression.Location, Exceptions.VBForEach_RequiresEnumerableCollection_Verbose));

            default:
                // a scalar value - MS-VBAL §5.4.2.4 requires an array or an enumeration-capable object.
                return RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.TypeMismatch,
                    forEachStatement.CollectionExpression.Location, Exceptions.VBForEach_RequiresEnumerableCollection_Verbose));
        }
    }

    // MS-VBAL §5.4.2.4 leaves the enumeration of an object implementation-defined: its enumeration member returns an enumerator, which is
    // asked once for each member - moved to, then read - until it has none. The enumerator is asked for before any member is, and is the
    // loop's for as long as the loop runs: what the collection holds by then is the enumerator's to say.
    // Returns null when the first member was assigned (or there is none, and the loop was skipped), and a non-null outcome only when execution must stop.
    private RuntimeExecutionOutcome? StartObjectEnumeration(
        IRuntimeSession session, RuntimeEvaluationContext context, Instruction instruction, CallStackFrame activation,
        ForEachStatementNode forEachStatement, ITypedSymbol control, VBObjectValue collection)
    {
        var enumeratorResult = forEach.Enumerate(session, context, collection);
        if (!enumeratorResult.IsSuccess)
        {
            return ToFailureOutcome(enumeratorResult);
        }

        var returned = enumeratorResult.Result;
        while (returned is VBVariantValue { TypedValue: { } wrapped })
        {
            returned = wrapped;
        }

        if (returned is not VBObjectValue { } enumerator || enumerator.IsNothing())
        {
            // an enumeration member that returns no enumerator has nothing to enumerate with.
            return RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet,
                forEachStatement.CollectionExpression.Location, Exceptions.VBForEach_ObjectVariableNotSet_Verbose));
        }

        HoldForBlock(session, activation, instruction, enumerator);
        var advanced = AdvanceEnumerator(session, context, enumerator, forEachStatement.ControlExpression, control, out var outcome);
        if (outcome is not null)
        {
            return outcome;
        }

        if (!advanced)
        {
            // a collection with no members: the loop is over before it began, as for an array with no elements, and its control variable is emptied.
            if (instruction.End is not { } emptyEnd)
            {
                return RuntimeExecutionOutcome.InternalError;
            }

            if (EmptyControl(session, control, forEachStatement.ControlExpression) is { } notEmptied)
            {
                return notEmptied;
            }

            EndLoop(session, activation, instruction.Offset);
            activation.Pc = emptyEnd;
            return null;
        }

        activation.SetForEachState(instruction.Offset, new ForEachState(control, forEachStatement.ControlExpression, null, 0, enumerator));
        activation.Pc = instruction.Offset + 1;
        return null;
    }

    // Moves the enumerator to its next member and assigns it to the control variable. False when there is none to assign; the outcome is
    // non-null only when execution must stop.
    private bool AdvanceEnumerator(
        IRuntimeSession session, RuntimeEvaluationContext context, VBObjectValue enumerator, ExpressionNode controlExpression, ITypedSymbol control,
        out RuntimeExecutionOutcome? outcome)
    {
        outcome = null;

        var moved = forEach.MoveNext(session, context, enumerator);
        if (!moved.IsSuccess)
        {
            outcome = ToFailureOutcome(moved);
            return false;
        }

        var held = moved.Result;
        while (held is VBVariantValue { TypedValue: { } wrapped })
        {
            held = wrapped;
        }

        // a Boolean's own storage is an integer: any that is not zero is True.
        if (held is null || Convert.ToInt32(held.Handle.Value.BoxedValue) == 0)
        {
            return false;
        }

        var current = forEach.Current(session, context, enumerator);
        if (!current.IsSuccess)
        {
            outcome = ToFailureOutcome(current);
            return false;
        }

        var assigned = forEach.AssignEnumerated(session, control, controlExpression, current.Result!);
        if (!assigned.IsSuccess)
        {
            outcome = ToFailureOutcome(assigned);
            return false;
        }

        return true;
    }

    // Returns null when the next element was assigned successfully and the loop should keep running
    // (activation.Pc branched back to the body, or fallen through past the loop); returns a non-null
    // outcome only when execution must stop.
    private RuntimeExecutionOutcome? ExecuteForEachNext(IRuntimeSession session, RuntimeEvaluationContext context, Instruction instruction, CallStackFrame activation)
    {
        if (instruction.Matching is not { } openerOffset || !activation.TryGetForEachState(openerOffset, out var state))
        {
            // MS-VBAL §5.4.2.4: a GoTo landed directly on this Next without its own ForEachOpener ever running this activation, or the opener raised an
            // error that was resumed past - error 92, "For loop not initialized". MS-VBA empties a Variant control variable first, unless the collection
            // is declared as an array (RD-VBAL §5.4.2.4).
            if (instruction.Matching is { } matching && activation.Body?.Items[matching].Node is ForEachStatementNode { ControlExpression: SimpleNameExpressionNode simpleName } opener
                && session.Symbols.Resolver.ResolveValue(simpleName, ScopeKind.Local, context.Scope).Symbol is ITypedSymbol unopened
                && !forEach.KeepsControlWithNoLoopRunning(session, unopened, opener.CollectionExpression, context)
                && EmptyControl(session, unopened, simpleName) is { } notEmptied)
            {
                return notEmptied;
            }

            return RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.ForLoopNotInitialized,
                instruction.Node?.SourceLocation ?? default, Exceptions.VBForLoopNotInitialized_Verbose));
        }

        // an object is enumerated by the enumerator it gave: the next member is the one it moves to.
        if (state.Enumerator is { } enumerator)
        {
            if (!AdvanceEnumerator(session, context, enumerator, state.ControlExpression, state.Control, out var enumeratedOutcome))
            {
                if (enumeratedOutcome is not null)
                {
                    return enumeratedOutcome;
                }

                // no more members - the loop ends here, with its control variable emptied (RD-VBAL §5.4.2.4).
                if (EmptyControl(session, state.Control, state.ControlExpression) is { } notEmptied)
                {
                    return notEmptied;
                }

                EndLoop(session, activation, openerOffset);
                activation.Pc = instruction.Offset + 1;
                return null;
            }

            if (instruction.Target is not { } enumeratedTarget)
            {
                return RuntimeExecutionOutcome.InternalError;
            }

            activation.Pc = enumeratedTarget;
            return null;
        }

        var nextIndex = state.Index + 1;
        if (nextIndex >= state.Array!.Length)
        {
            // no more elements - the loop ends here, with its control variable emptied (RD-VBAL §5.4.2.4).
            if (EmptyControl(session, state.Control, state.ControlExpression) is { } notEmptied)
            {
                return notEmptied;
            }

            EndLoop(session, activation, openerOffset);
            activation.Pc = instruction.Offset + 1;
            return null;
        }

        // an element of an array of a scalar type is let-assigned to a Variant control variable as the payload it is: the Variant holds a value of
        // the element's type, as the assignment makes it hold. A control variable that holds an object is assigned boxed, which lets go of it.
        var payloads = state.RuntimeState as ForEachPayloads ?? NewForEachPayloads(activation, state);
        if (observation is null && UnboxedEvaluation.IsEnabled && !state.Array.IsIndeterminate && payloads.Control is { } slot
            && state.Array.CellAt(nextIndex) is ScalarCell elementCell && elementCell.TryRead(out var payload)
            && activation.TryReadSlot(slot, (Symbol)state.Control, out var controlCell, out var referenced) && referenced is null
            && controlCell is VariantCell { HoldsNoObject: true } control)
        {
            control.Write(payload);
        }
        else
        {
            var element = state.Array.ElementAt(nextIndex)!;
            var assignResult = forEach.AssignControl(session, state.Control, state.ControlExpression,
                ForEachEvaluator.IsSetAssigned(state.Array.ItemType, element), element);
            if (!assignResult.IsSuccess)
            {
                return ToFailureOutcome(assignResult);
            }
        }

        activation.SetForEachState(openerOffset, state with { Index = nextIndex, RuntimeState = payloads });

        if (instruction.Target is not { } target)
        {
            // lowering always sets Target on a ForEachNext instruction - reaching here is a lowering bug.
            return RuntimeExecutionOutcome.InternalError;
        }
        activation.Pc = target;
        return null;
    }

    // MS-VBAL §5.4.2.4 says a loop that has finished leaves its last element in the control variable; MS-VBA empties it (RD-VBAL §5.4.2.4). Null when it was
    // emptied, and the outcome of the assignment when that failed.
    private RuntimeExecutionOutcome? EmptyControl(IRuntimeSession session, ITypedSymbol control, ExpressionNode controlExpression)
    {
        var emptied = forEach.EmptyControl(session, control, controlExpression);
        return emptied.IsSuccess ? null : ToFailureOutcome(emptied);
    }

    // where the control variable of a For Each is kept, found once for the loop: its slot, when it is a local of the activation.
    private static ForEachPayloads NewForEachPayloads(CallStackFrame activation, ForEachState state)
        => new()
        {
            Control = activation.Procedure is { } procedure && FrameLayout.For(procedure).TryGetSlot((Symbol)state.Control, out var slot) ? slot : null,
        };

    // Shared by On...GoTo (JumpTable, pushReturn: false) and On...GoSub (GoSubTable, pushReturn: true) -
    // MS-VBAL §5.4.2.13/.16 describe the exact same selector algorithm for both, differing only in
    // whether a successful branch also pushes a GoSub Resumption List entry. Returns null when the
    // instruction fell through (out-of-range selector) or branched successfully; a non-null outcome only
    // when execution must stop.
    private RuntimeExecutionOutcome? ExecuteJumpTable(IRuntimeSession session, RuntimeEvaluationContext context, Instruction instruction, CallStackFrame activation, bool pushReturn)
    {
        var selector = instruction.Node switch
        {
            OnGoToStatementNode onGoTo => onGoTo.Selector,
            OnGoSubStatementNode onGoSub => onGoSub.Selector,
            _ => null
        };
        if (selector is null)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        var selectorResult = jumpTable.EvaluateSelector(session, selector, context);
        if (!selectorResult.IsSuccess)
        {
            return ToFailureOutcome(selectorResult);
        }

        var n = ((VBIntegerValue)selectorResult.Result!).Value;
        if (n == 0 || n > instruction.Targets.Length)
        {
            // MS-VBAL §5.4.2.13/.16: "if n is zero, or greater than the number of statement-label
            // defined..., execution completes immediately" - falls through without branching.
            activation.Pc = instruction.Offset + 1;
            return null;
        }

        if (n < 0 || n > 255)
        {
            return RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.InvalidProcedureCallOrArgument,
                selector.Location, Exceptions.VBOnGoToGoSub_SelectorOutOfRange_Verbose));
        }

        if (instruction.Targets[n - 1] is not { } target)
        {
            // an unresolved label already reported its own VBC09309 at lowering time.
            return RuntimeExecutionOutcome.InternalError;
        }

        if (pushReturn)
        {
            activation.PushGoSubReturn(instruction.Offset + 1);
        }
        else
        {
            LeaveLoops(session, activation, instruction.Offset, target);
        }

        activation.Pc = target;
        return null;
    }

    // Returns null when the branch was taken (activation.Pc already set); returns a non-null outcome
    // only when execution must stop.
    private static RuntimeExecutionOutcome? ExecuteReturn(Instruction instruction, CallStackFrame activation)
    {
        if (!activation.TryPopGoSubReturn(out var returnOffset))
        {
            // MS-VBAL §5.4.2.15: an empty GoSub Resumption List is error 3, "Return without GoSub".
            return RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.ReturnWithoutGoSub,
                instruction.Node?.SourceLocation ?? default, Exceptions.VBReturn_WithoutGoSub_Verbose));
        }

        activation.Pc = returnOffset;
        return null;
    }

    // 🎯 What Erl reports. MS-VBA answers with the last line-number label it passed, which is only ever the
    // truth for code that numbers every line; the document line is the truth for any code at all, and is
    // what an environment reports unless its workspace depends on MS-VBA's own behaviour. Either way it is the line of the activation's own statement
    // that faulted: an error a callee raised is at the call, as MS-VBA's handler of the caller reports it.
    private static long ErlLineOf(IRuntimeSession session, InstructionList list, int faultOffset, IVBRaisableError error, SourceLocation? statement)
    {
        if (session.Environment.ErlLineNumbering is VBErlLineNumbering.LineLabel)
        {
            list.TryGetLineNumber(faultOffset, out var label);
            return label;
        }

        // the error is where it was raised, which is a line of the statement when the statement raised it, one of a statement continued over several
        // lines included; another procedure's when a callee raised it.
        var raised = error.Location.Range.Start.Line;
        var line = statement is { } faulting && (faulting.Uri != error.Location.Uri || raised < faulting.Range.Start.Line || raised > faulting.Range.End.Line)
            ? faulting.Range.Start.Line
            : raised;

        // every position in the platform is zero-based; a line number is what an editor shows, counted from 1.
        return line + 1;
    }

    /// <summary>
    /// Runs <paramref name="frame"/> on from the instruction at <paramref name="pc"/>, which a caller other than this executor ran and which ended with
    /// <paramref name="outcome"/> - an error, or the program stopped - as this executor goes on after a statement that did: the error given the activation's
    /// handler, which catches it or lets it propagate; the program stopped, which stops this activation too. What compiled code hands over when a call it made
    /// does not return.
    /// </summary>
    internal RuntimeExecutionOutcome RunAfter(IRuntimeSession session, CallStackFrame frame, InstructionList list, RuntimeEvaluationContext context, int pc,
        RuntimeExecutionOutcome outcome, int? held = null)
    {
        frame.Pc = pc;
        if (InterceptError(session, frame, list, pc, outcome) is { } unhandled)
        {
            var propagated = unhandled.Kind is RuntimeExecutionOutcomeKind.InternalError && session.Halt.Pending is { } halt
                ? halt is RuntimeHaltKind.End ? RuntimeExecutionOutcome.Halt : RuntimeExecutionOutcome.Break
                : unhandled;
            LetGoOfStatement(session, held, propagated);
            return propagated;
        }

        return Run(session, frame, list, context, once: false, held);
    }

    // Central error-handling interception point (MS-VBAL §5.4.4): every instruction dispatch above that
    // could produce an Error outcome routes through here before the executor loop decides whether to
    // actually stop. Returns null when an active handler caught it (activation.Pc/ErrorHandler already
    // updated - the loop keeps running); returns `outcome` otherwise (no active handler, or a non-Error
    // outcome - propagate/stop exactly as if this interception point didn't exist), its error given the
    // faulting statement's location when it had none.
    private static RuntimeExecutionOutcome? InterceptError(
        IRuntimeSession session, CallStackFrame activation, InstructionList list, int faultOffset, RuntimeExecutionOutcome outcome)
    {
        if (outcome.Kind != RuntimeExecutionOutcomeKind.Error)
        {
            return outcome;
        }

        // an error carries the location of what raised it, and a library member cannot know where it was called from: until its call
        // site is given to it, its error says nowhere. The statement that faulted is where it was raised, as far as anything here knows,
        // and the error is given that location once, here, before anything reads it - a Next is no statement of its own, and is where
        // its closing line is (Instruction.Location). The error that propagates is that one: a caller it unwinds into finds it located
        // and leaves it be, and a debugger that waited at it does not wait at it again.
        var faulting = list.Items[faultOffset];
        var location = faulting.Location ?? (faulting.Matching is { } opener ? list.Items.ElementAtOrDefault(opener)?.Node?.SourceLocation : null);
        if (outcome.ErrorInfo!.Location is { Uri: null } && location is { Uri: not null } statement)
        {
            outcome = RuntimeExecutionOutcome.Error((IVBRaisableError)(outcome.ErrorInfo.AsErrorInfo with { Location = statement }));
        }

        // every run-time error reaches the session's error state, handled or not: MS-VBAL §6.1.3.2's
        // Err is set by the error being raised, not by anything choosing to deal with it. This is the
        // one place every error the interpreter can raise passes through, which is why it is here.
        // the line the fault is at travels with it, which is what Erl reports - counted as the environment
        // says to count it.
        session.Errors.Raise(outcome.ErrorInfo!, ErlLineOf(session, list, faultOffset, outcome.ErrorInfo!, location));

        // a program under a debugger that asked to see errors waits here, where the error was raised and nothing has been unwound: the activation is the one that faulted,
        // at the statement that did. It goes on from here when it is resumed. An error a handler catches is caught; one nothing catches is not given to the
        // program, which runs the statement that raised it again - to raise it again, and wait again, unless it was edited (MS-VBA, observed in Excel).
        // A program moved while it waits goes on from where it was moved to, the error not given to it either. Err keeps the error
        // in both. The code may be edited while it waits: the activation is then moved into its edited body (CallStackFrame.TryRebase) - to the
        // statement that raised the error, as it now is.
        var body = activation.Body;
        var handled = session.CallStack.Frames.OfType<CallStackFrame>().Any(frame => frame.ErrorHandler.Mode is not ErrorHandlingMode.Disabled);
        (activation.WaitsAtAnError, activation.WaitsAtAHandledError) = (true, handled);
        bool waited;
        try
        {
            waited = session.Halt.TrySuspendOnError(outcome.ErrorInfo!, handled);
        }
        finally
        {
            (activation.WaitsAtAnError, activation.WaitsAtAHandledError) = (false, false);
        }

        if (waited && (activation.TakeMove() || (!handled && session.Errors.Untrappable is null)))
        {
            return null;
        }

        if (!ReferenceEquals(activation.Body, body) && activation.Body is { } rebased)
        {
            (list, faultOffset) = (rebased, activation.Pc);
        }

        // an error a Class_Terminate left unhandled is handled by nothing: it ends the program, from whichever activation it reaches (MS-VBA, observed in Excel).
        var handler = session.Errors.Untrappable is null ? activation.ErrorHandler : ErrorHandlerState.Disabled;
        switch (handler.Mode)
        {
            case ErrorHandlingMode.ResumeNext:
                // "Resume Next" does not reset the activation's own policy - it keeps silently catching
                // every later error in the same activation, not just the first one.
                activation.ErrorHandler = handler with { ActiveError = outcome.ErrorInfo, FaultStatementOffset = faultOffset };
                activation.Pc = NextStatementAfter(list, faultOffset);
                return null;

            case ErrorHandlingMode.GoTo:
                // Catching via GoTo resets the activation's own policy to Default - an unhandled second
                // error inside the handler body itself propagates rather than re-entering the handler. What it is to be once the handler is over is the
                // same label, until the handler says otherwise (ErrorHandlerState.AfterHandling).
                activation.ErrorHandler = new ErrorHandlerState(ErrorHandlingMode.Disabled, handler.HandlerTarget, outcome.ErrorInfo, faultOffset,
                    HandlerEntry: handler.HandlerTarget, AfterHandling: ErrorHandlingMode.GoTo);
                activation.Pc = handler.HandlerTarget!.Value;
                return null;

            default:
                return outcome;
        }
    }

    // Returns null when the Resume succeeded (activation.Pc/ErrorHandler already updated - the loop
    // keeps running); returns a non-null outcome only when there was no active error to resume from
    // (error 20), itself routed back through InterceptError by the caller like any other Error outcome.
    private static RuntimeExecutionOutcome? ExecuteResume(InstructionList list, Instruction instruction, CallStackFrame activation)
    {
        if (activation.ErrorHandler.ActiveError is null || activation.ErrorHandler.FaultStatementOffset is not { } faultOffset)
        {
            return RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.ResumeWithoutError,
                instruction.Node?.SourceLocation ?? default, Exceptions.VBResume_WithoutError_Verbose));
        }

        // MS-VBAL §5.4.4.1: the policy an On Error GoTo set is still to go to its label - catching an error only made it the handler's to run, which an
        // error inside it does not re-enter. The Resume ends the handler: the next error goes to the label again (MS-VBA, observed in Excel), or where an
        // On Error statement in the handler said it goes after it.
        var resumed = activation.ErrorHandler;
        activation.ErrorHandler = resumed.IsHandling
            ? resumed.HandlingOver()
            : resumed with { ActiveError = null, FaultStatementOffset = null, HandlerEntry = null };
        activation.Pc = instruction.Kind switch
        {
            InstructionKind.ResumeCurrentStatement => faultOffset,
            InstructionKind.ResumeNext => NextStatementAfter(list, faultOffset),
            InstructionKind.ResumeLabel => instruction.Target!.Value,
            _ => activation.Pc,
        };
        return null;
    }

    // MS-VBAL §5.4.4.1, §5.4.4.2: execution goes on at the statement "immediately following the <statement> which caused the error". A Select Case is
    // one statement up to its End Select: an error in its selector goes on past the whole construct, no Case matched (MS-VBA, observed in Excel).
    private static int NextStatementAfter(InstructionList list, int faultOffset)
        => list.Items[faultOffset] is { Kind: InstructionKind.Select, End: { } end } ? end : faultOffset + 1;

    // MS-VBAL §5.4.4.3: "the effect is as if the Err.Raise method were invoked" - always produces an
    // Error outcome (or InternalError on a lowering bug/a number expression that fails to evaluate), for
    // the caller to route through InterceptError exactly like any other runtime error.
    private RuntimeExecutionOutcome ExecuteRaiseError(IRuntimeSession session, RuntimeEvaluationContext context, Instruction instruction)
    {
        if (instruction.Node is not ErrorStatementNode errorStatement)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        var numberResult = errorHandling.EvaluateErrorNumber(session, errorStatement.NumberExpression, context);
        if (!numberResult.IsSuccess)
        {
            return ToFailureOutcome(numberResult);
        }

        // an *application* error (RD-VBAL §2.6.3, the VBA family) rather than one the runtime semantics
        // reported: the family follows who raised it, and workspace source raised this one. `Error 11` is
        // VBA00011, not VBR00011, even though 11 is a code MS-VBA defines.
        var number = ((VBIntegerValue)numberResult.Result!).Value;

        // its help context is the error's, as for an error the runtime raises, whatever one Err has: Error 9 is 1000009, and Error 5 after
        // Err.Raise 13, , , , 77 is 1000005 (observed in Excel).
        return RuntimeExecutionOutcome.Error(VBApplicationErrorInfo.Raised(
            number, errorStatement.NumberExpression.Location, Exceptions.VBErrorStatement_Raised_Verbose));
    }

    // Structural recognition only - the member's own body is never called (no procedure invocation
    // machinery exists yet). The member is the one whose VB_UserMemId is DISPID_NEWENUM (-4).
    private static bool HasNewEnumMember(VBClassModuleSymbol classModule)
        => classModule.Members.OfType<VBReturningMemberSymbol>().Any(member => member.GetProperty(SymbolProperties.UserMemId) == WellKnownDispIds.NewEnum);

    private static RuntimeExecutionOutcome ToFailureOutcome(RuntimeSemanticsEvaluationResult result)
        => result.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(result.ErrorInfo!);

    // The If/ElseIf/inline-If/pre-test-and-post-test-loop header node kinds only - a Select Case's own
    // Case header (CaseExpressionStatementNode) needs different handling entirely (range-clause matching
    // against a stashed selector, not a single Boolean condition), not this method's job. The bool is
    // true for a "…Until" condition, whose polarity is the opposite of every other kind here (MS-VBAL
    // §5.4.2.2/.6: an "Until"/"While" pair share one node shape per test position, distinguished only by
    // which value of the same ConditionExpression means "keep going").
    private static (ExpressionNode? Condition, bool Negate) GetCondition(StatementNode? node) => node switch
    {
        IfBlockStatementNode ifBlock => (ifBlock.ConditionExpression, false),
        ElseIfBlockStatementNode elseIf => (elseIf.ConditionExpression, false),
        InlineIfStatementNode inlineIf => (inlineIf.ConditionExpression, false),
        WhileWendStatementNode whileWend => (whileWend.ConditionExpression, false),
        DoWhileLoopStatementNode doWhile => (doWhile.ConditionExpression, false),
        DoUntilLoopStatementNode doUntil => (doUntil.ConditionExpression, true),
        DoLoopWhileStatementNode doLoopWhile => (doLoopWhile.ConditionExpression, false),
        DoLoopUntilStatementNode doLoopUntil => (doLoopUntil.ConditionExpression, true),
        _ => (null, false),
    };
}
