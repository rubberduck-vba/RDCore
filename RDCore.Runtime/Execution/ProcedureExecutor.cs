using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.Statements;
using RDCore.SDK;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Instructions;

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
/// <c>Err.Raise</c> were invoked." What this slice does NOT model: the <c>Err</c> object itself as
/// something VBA source can read/call (<c>Err.Number</c>, <c>Err.Raise(...)</c> — needs member-dispatch
/// machinery, S9's job) and a <c>Default</c>-policy error actually propagating to a real CALLER activation
/// (no multi-frame call stack exists yet — today, "propagate" means this <c>Run</c> call's own return
/// value, which is exactly what a future caller's <c>Run</c> would see and apply its own policy to, so the
/// mechanism generalizes for free once S9 exists).
/// </para>
/// </remarks>
public sealed class ProcedureExecutor(IStatementRuntimeSemanticsProvider statements, ConditionEvaluator conditions, WithTargetEvaluator withTargets, CaseMatchEvaluator cases, ForLoopEvaluator forLoop, ForEachEvaluator forEach, JumpTableEvaluator jumpTable, ErrorHandlingEvaluator errorHandling, CancellationToken cancellation = default)
{
    /// <summary>
    /// Runs <paramref name="frame"/> against <paramref name="list"/> from its current <c>Pc</c> until
    /// the outcome is anything other than <c>Next</c>/<c>Branch</c>.
    /// </summary>
    public RuntimeExecutionOutcome Run(IRuntimeSession session, ICallStackFrame frame, InstructionList list, RuntimeEvaluationContext context)
    {
        var activation = (CallStackFrame)frame;

        while (activation.Pc < list.Items.Length)
        {
            // between instructions, never inside one: a half-executed statement would leave the
            // session in a state no VBA program could have produced. This is what stops a program
            // whose own control flow never would - an empty Do…Loop, a GoTo cycle.
            if (cancellation.IsCancellationRequested)
            {
                return RuntimeExecutionOutcome.Break;
            }

            var instruction = list.Items[activation.Pc];
            var instructionContext = ResolveContext(context, activation, instruction);

            switch (instruction.Kind)
            {
                case InstructionKind.Simple:
                    var outcome = statements.Execute(session, instructionContext, instruction.Node!);
                    if (outcome.Kind != RuntimeExecutionOutcomeKind.Next)
                    {
                        if (InterceptError(session, activation, instruction.Offset, outcome) is { } unhandledSimple)
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
                    activation.Pc = target;
                    break;

                case InstructionKind.ConditionalBranch:
                    var branchOutcome = ExecuteConditionalBranch(session, instructionContext, instruction, activation);
                    if (branchOutcome is { } stop && InterceptError(session, activation, instruction.Offset, stop) is { } unhandledBranch)
                    {
                        return unhandledBranch;
                    }
                    break;

                case InstructionKind.LoopBack:
                    var loopBackOutcome = ExecuteLoopBack(session, instructionContext, instruction, activation);
                    if (loopBackOutcome is { } stopLoopBack && InterceptError(session, activation, instruction.Offset, stopLoopBack) is { } unhandledLoopBack)
                    {
                        return unhandledLoopBack;
                    }
                    break;

                case InstructionKind.With:
                    var withOutcome = ExecuteWith(session, instructionContext, instruction, activation);
                    if (withOutcome is { } stopWith && InterceptError(session, activation, instruction.Offset, stopWith) is { } unhandledWith)
                    {
                        return unhandledWith;
                    }
                    break;

                case InstructionKind.Select:
                    var selectOutcome = ExecuteSelect(session, instructionContext, instruction, activation);
                    if (selectOutcome is { } stopSelect && InterceptError(session, activation, instruction.Offset, stopSelect) is { } unhandledSelect)
                    {
                        return unhandledSelect;
                    }
                    break;

                case InstructionKind.ForOpener:
                    var forOpenerOutcome = ExecuteForOpener(session, instructionContext, instruction, activation);
                    if (forOpenerOutcome is { } stopForOpener && InterceptError(session, activation, instruction.Offset, stopForOpener) is { } unhandledForOpener)
                    {
                        return unhandledForOpener;
                    }
                    break;

                case InstructionKind.ForNext:
                    var forNextOutcome = ExecuteForNext(session, instructionContext, instruction, activation);
                    if (forNextOutcome is { } stopForNext && InterceptError(session, activation, instruction.Offset, stopForNext) is { } unhandledForNext)
                    {
                        return unhandledForNext;
                    }
                    break;

                case InstructionKind.ForEachOpener:
                    var forEachOpenerOutcome = ExecuteForEachOpener(session, instructionContext, instruction, activation);
                    if (forEachOpenerOutcome is { } stopForEachOpener && InterceptError(session, activation, instruction.Offset, stopForEachOpener) is { } unhandledForEachOpener)
                    {
                        return unhandledForEachOpener;
                    }
                    break;

                case InstructionKind.ForEachNext:
                    var forEachNextOutcome = ExecuteForEachNext(session, instructionContext, instruction, activation);
                    if (forEachNextOutcome is { } stopForEachNext && InterceptError(session, activation, instruction.Offset, stopForEachNext) is { } unhandledForEachNext)
                    {
                        return unhandledForEachNext;
                    }
                    break;

                case InstructionKind.OnErrorGoTo:
                    if (instruction.Target is not { } handlerTarget)
                    {
                        return RuntimeExecutionOutcome.InternalError;
                    }
                    activation.ErrorHandler = new ErrorHandlerState(ErrorHandlingMode.GoTo, handlerTarget, null, null);
                    // MS-VBAL §6.1.3.2.1: an On Error statement clears the Err object.
                    session.Errors.Clear();
                    activation.Pc = instruction.Offset + 1;
                    break;

                case InstructionKind.OnErrorDisable:
                    activation.ErrorHandler = ErrorHandlerState.Disabled;
                    // On Error GoTo 0 / On Error GoTo -1 is an On Error statement too, and §6.1.3.2.1
                    // makes no exception for it.
                    session.Errors.Clear();
                    activation.Pc = instruction.Offset + 1;
                    break;

                case InstructionKind.OnErrorResumeNext:
                    activation.ErrorHandler = new ErrorHandlerState(ErrorHandlingMode.ResumeNext, null, null, null);
                    session.Errors.Clear();
                    activation.Pc = instruction.Offset + 1;
                    break;

                case InstructionKind.ResumeCurrentStatement:
                case InstructionKind.ResumeNext:
                case InstructionKind.ResumeLabel:
                    var resumeOutcome = ExecuteResume(instruction, activation);
                    if (resumeOutcome is null)
                    {
                        // a Resume that actually resumed; §6.1.3.2.1 clears Err with it.
                        session.Errors.Clear();
                    }
                    if (resumeOutcome is { } stopResume && InterceptError(session, activation, instruction.Offset, stopResume) is { } unhandledResume)
                    {
                        return unhandledResume;
                    }
                    break;

                case InstructionKind.RaiseError:
                    var raiseOutcome = ExecuteRaiseError(session, instructionContext, instruction);
                    if (InterceptError(session, activation, instruction.Offset, raiseOutcome) is { } unhandledRaise)
                    {
                        return unhandledRaise;
                    }
                    break;

                case InstructionKind.JumpTable:
                    var jumpTableOutcome = ExecuteJumpTable(session, instructionContext, instruction, activation, pushReturn: false);
                    if (jumpTableOutcome is { } stopJumpTable && InterceptError(session, activation, instruction.Offset, stopJumpTable) is { } unhandledJumpTable)
                    {
                        return unhandledJumpTable;
                    }
                    break;

                case InstructionKind.GoSubTable:
                    var goSubTableOutcome = ExecuteJumpTable(session, instructionContext, instruction, activation, pushReturn: true);
                    if (goSubTableOutcome is { } stopGoSubTable && InterceptError(session, activation, instruction.Offset, stopGoSubTable) is { } unhandledGoSubTable)
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
                    if (returnOutcome is { } stopReturn && InterceptError(session, activation, instruction.Offset, stopReturn) is { } unhandledReturn)
                    {
                        return unhandledReturn;
                    }
                    break;

                case InstructionKind.ExitProcedure:
                    // Exit Sub / Exit Function / Exit Property clear Err too (§6.1.3.2.1). Falling off
                    // the end of a procedure does not, which is why this is here and not after the loop.
                    session.Errors.Clear();
                    return RuntimeExecutionOutcome.ExitProcedure;

                case InstructionKind.Halt:
                    return RuntimeExecutionOutcome.Halt;

                case InstructionKind.Break:
                    return RuntimeExecutionOutcome.Break;

                default:
                    return RuntimeExecutionOutcome.InternalError;
            }
        }

        // fell off the end of the list - MS-VBAL §5.4.2.17: "completes as if execution had reached
        // the end of the body" is the same outcome as an explicit Exit.
        return RuntimeExecutionOutcome.ExitProcedure;
    }

    // A purely lexical property of the instruction about to run, not something that should persist from
    // whatever the previous instruction's own context happened to carry - a GoTo into or out of a With
    // block leaves no stale stack, so this is recomputed fresh every fetch rather than pushed/popped.
    private static RuntimeEvaluationContext ResolveContext(RuntimeEvaluationContext outer, CallStackFrame activation, Instruction instruction)
        => instruction.EnclosingWith is { } openerOffset && activation.TryGetBlockState(openerOffset, out var target)
            ? outer with { EnclosingWithTarget = target }
            : outer with { EnclosingWithTarget = null };

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
        // Case header's own range-clause matching against it.
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
        if (instruction.Node is not CaseExpressionStatementNode caseBlock || !activation.TryGetBlockState(selectOffset, out var selector))
        {
            // TryGetBlockState failing here means the enclosing Select's own opener never ran - a
            // lowering/executor desync, not a reachable program state.
            return RuntimeExecutionOutcome.InternalError;
        }

        // a Variant selector's own TypeInfo mirrors its wrapped value's, but stays a VBVariantValue
        // instance - unwrap it here (recursively) so a Null (or any other) selector held in a Variant
        // is recognized the same way a directly-typed one would be.
        while (selector is VBVariantValue { TypedValue: var wrapped })
        {
            selector = wrapped;
        }

        if (selector is VBNullValue)
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

        activation.SetBlockState(instruction.Offset, targetResult.Result!);
        activation.Pc = instruction.Offset + 1;
        return null;
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

        var counterResult = session.Symbols.Resolver.ResolveValue(simpleName.IdentifierName, ScopeKind.Local, context.Scope);
        if (counterResult.Symbol is not { } counterSymbol)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        var startResult = forLoop.EvaluateOperand(session, forStatement.StartExpression, context);
        if (!startResult.IsSuccess)
        {
            return ToFailureOutcome(startResult);
        }

        var endResult = forLoop.EvaluateOperand(session, forStatement.EndExpression, context);
        if (!endResult.IsSuccess)
        {
            return ToFailureOutcome(endResult);
        }

        // MS-VBAL §5.4.2.3: "if no step-clause is present, the step-increment value is the integer data
        // value 1" - a static default, never itself evaluated as a source expression.
        RuntimeSemanticsEvaluationResult stepResult = forStatement.StepExpression is { } stepExpression
            ? forLoop.EvaluateOperand(session, stepExpression, context)
            : RuntimeSemanticsEvaluationResult.Success(new VBLongValue(1));
        if (!stepResult.IsSuccess)
        {
            return ToFailureOutcome(stepResult);
        }

        var state = new ForLoopState(counterSymbol, forStatement.ControlExpression, endResult.Result!, stepResult.Result!);

        var assignResult = forLoop.AssignCounter(session, state, forStatement.StartExpression, startResult.Result!);
        if (!assignResult.IsSuccess)
        {
            return ToFailureOutcome(assignResult);
        }

        // stashed before the entry test below, exactly as MS-VBAL frames it: start/end/step are
        // considered evaluated the moment the opener runs, regardless of whether the body ever executes.
        activation.SetForLoopState(instruction.Offset, state);

        var rangeResult = forLoop.IsOutOfRange(session, state, assignResult.Result!);
        if (!rangeResult.IsSuccess)
        {
            return ToFailureOutcome(rangeResult);
        }

        if (((VBBooleanValue)rangeResult.Result!).Value.StoredValue != 0)
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
                instruction.Node?.SourceLocation ?? default, Exceptions.VBForLoopNotInitialized_Verbose));
        }

        var counterResult = forLoop.EvaluateOperand(session, state.ControlExpression, context);
        if (!counterResult.IsSuccess)
        {
            return ToFailureOutcome(counterResult);
        }

        var sumResult = forLoop.Increment(session, state, counterResult.Result!);
        if (!sumResult.IsSuccess)
        {
            return ToFailureOutcome(sumResult);
        }

        var assignResult = forLoop.AssignCounter(session, state, state.ControlExpression, sumResult.Result!);
        if (!assignResult.IsSuccess)
        {
            return ToFailureOutcome(assignResult);
        }

        var rangeResult = forLoop.IsOutOfRange(session, state, assignResult.Result!);
        if (!rangeResult.IsSuccess)
        {
            return ToFailureOutcome(rangeResult);
        }

        if (((VBBooleanValue)rangeResult.Result!).Value.StoredValue != 0)
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

    // Returns null when the collection was evaluated and stashed successfully and the loop should keep
    // running (activation.Pc set to the body's first instruction, or skipped straight past the whole
    // construct when the array is empty); returns a non-null outcome only when execution must stop.
    private RuntimeExecutionOutcome? ExecuteForEachOpener(IRuntimeSession session, RuntimeEvaluationContext context, Instruction instruction, CallStackFrame activation)
    {
        if (instruction.Node is not ForEachStatementNode forEachStatement || forEachStatement.ControlExpression is not SimpleNameExpressionNode simpleName)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        var controlResult = session.Symbols.Resolver.ResolveValue(simpleName.IdentifierName, ScopeKind.Local, context.Scope);
        if (controlResult.Symbol is not ITypedSymbol control)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        var collectionResult = forEach.EvaluateCollection(session, forEachStatement.CollectionExpression, context);
        if (!collectionResult.IsSuccess)
        {
            return ToFailureOutcome(collectionResult);
        }

        // a Variant holding an array (or an object) reports its own TypeInfo as the wrapped value's,
        // but stays a VBVariantValue instance - unwrap it here (recursively) so "For Each x In v" on a
        // Variant works the same as on a declared array/object collection.
        var collection = collectionResult.Result;
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
                    // a real, initialized array whose declared bounds just happen to hold zero elements.
                    if (instruction.End is not { } emptyEnd)
                    {
                        return RuntimeExecutionOutcome.InternalError;
                    }
                    activation.Pc = emptyEnd;
                    return null;
                }

                var firstElement = array.ElementAt(0)!;
                var assignResult = forEach.AssignControl(session, control, forEachStatement.ControlExpression, array.ItemType is VBObjectType, firstElement);
                if (!assignResult.IsSuccess)
                {
                    return ToFailureOutcome(assignResult);
                }

                activation.SetForEachState(instruction.Offset, new ForEachState(control, forEachStatement.ControlExpression, array, 0));
                activation.Pc = instruction.Offset + 1;
                return null;

            case VBObjectValue objectValue when objectValue.IsNothing():
                // enumerating an object collection means invoking its _NewEnum member; on Nothing that
                // invocation itself would raise error 91.
                return RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet,
                    forEachStatement.CollectionExpression.Location, Exceptions.VBForEach_ObjectVariableNotSet_Verbose));

            case VBObjectValue objectValue when session.Symbols.TryGetInstance(objectValue.Value, out var instance) && HasNewEnumMember(instance.ClassModule):
                // recognized (VB_UserMemId = -4, commonly "_NewEnum"), but actually enumerating it means
                // calling it and then the COM IEnumVARIANT-shaped methods on whatever it returns - real
                // procedure invocation, which doesn't exist yet.
                return RuntimeExecutionOutcome.InternalError;

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

    // Returns null when the next element was assigned successfully and the loop should keep running
    // (activation.Pc branched back to the body, or fallen through past the loop); returns a non-null
    // outcome only when execution must stop.
    private RuntimeExecutionOutcome? ExecuteForEachNext(IRuntimeSession session, RuntimeEvaluationContext context, Instruction instruction, CallStackFrame activation)
    {
        if (instruction.Matching is not { } openerOffset || !activation.TryGetForEachState(openerOffset, out var state))
        {
            // MS-VBAL §5.4.2.4: a GoTo landed directly on this Next without its own ForEachOpener ever
            // running this activation - error 92, "For loop not initialized".
            return RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.ForLoopNotInitialized,
                instruction.Node?.SourceLocation ?? default, Exceptions.VBForLoopNotInitialized_Verbose));
        }

        var nextIndex = state.Index + 1;
        if (nextIndex >= state.Array.Length)
        {
            activation.Pc = instruction.Offset + 1; // no more elements - the loop ends here.
            return null;
        }

        var element = state.Array.ElementAt(nextIndex)!;
        var assignResult = forEach.AssignControl(session, state.Control, state.ControlExpression, state.Array.ItemType is VBObjectType, element);
        if (!assignResult.IsSuccess)
        {
            return ToFailureOutcome(assignResult);
        }

        activation.SetForEachState(openerOffset, state with { Index = nextIndex });

        if (instruction.Target is not { } target)
        {
            // lowering always sets Target on a ForEachNext instruction - reaching here is a lowering bug.
            return RuntimeExecutionOutcome.InternalError;
        }
        activation.Pc = target;
        return null;
    }

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

    // Central error-handling interception point (MS-VBAL §5.4.4): every instruction dispatch above that
    // could produce an Error outcome routes through here before the executor loop decides whether to
    // actually stop. Returns null when an active handler caught it (activation.Pc/ErrorHandler already
    // updated - the loop keeps running); returns `outcome` unchanged otherwise (no active handler, or a
    // non-Error outcome - propagate/stop exactly as if this interception point didn't exist).
    private static RuntimeExecutionOutcome? InterceptError(IRuntimeSession session, CallStackFrame activation, int faultOffset, RuntimeExecutionOutcome outcome)
    {
        if (outcome.Kind != RuntimeExecutionOutcomeKind.Error)
        {
            return outcome;
        }

        // every run-time error reaches the session's error state, handled or not: MS-VBAL §6.1.3.2's
        // Err is set by the error being raised, not by anything choosing to deal with it. This is the
        // one place every error the interpreter can raise passes through, which is why it is here.
        session.Errors.Raise(outcome.ErrorInfo!);

        var handler = activation.ErrorHandler;
        switch (handler.Mode)
        {
            case ErrorHandlingMode.ResumeNext:
                // "Resume Next" does not reset the activation's own policy - it keeps silently catching
                // every later error in the same activation, not just the first one.
                activation.ErrorHandler = handler with { ActiveError = outcome.ErrorInfo, FaultStatementOffset = faultOffset };
                activation.Pc = faultOffset + 1;
                return null;

            case ErrorHandlingMode.GoTo:
                // Catching via GoTo resets the activation's own policy to Default - an unhandled second
                // error inside the handler body itself propagates rather than re-entering the handler.
                activation.ErrorHandler = new ErrorHandlerState(ErrorHandlingMode.Disabled, null, outcome.ErrorInfo, faultOffset);
                activation.Pc = handler.HandlerTarget!.Value;
                return null;

            default:
                return outcome;
        }
    }

    // Returns null when the Resume succeeded (activation.Pc/ErrorHandler already updated - the loop
    // keeps running); returns a non-null outcome only when there was no active error to resume from
    // (error 20), itself routed back through InterceptError by the caller like any other Error outcome.
    private static RuntimeExecutionOutcome? ExecuteResume(Instruction instruction, CallStackFrame activation)
    {
        if (activation.ErrorHandler.ActiveError is null || activation.ErrorHandler.FaultStatementOffset is not { } faultOffset)
        {
            return RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.ResumeWithoutError,
                instruction.Node?.SourceLocation ?? default, Exceptions.VBResume_WithoutError_Verbose));
        }

        activation.ErrorHandler = activation.ErrorHandler with { ActiveError = null, FaultStatementOffset = null };
        activation.Pc = instruction.Kind switch
        {
            InstructionKind.ResumeCurrentStatement => faultOffset,
            InstructionKind.ResumeNext => faultOffset + 1,
            InstructionKind.ResumeLabel => instruction.Target!.Value,
            _ => activation.Pc,
        };
        return null;
    }

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
        return RuntimeExecutionOutcome.Error(VBApplicationErrorInfo.Raised(
            number, errorStatement.NumberExpression.Location, Exceptions.VBErrorStatement_Raised_Verbose));
    }

    // Structural recognition only - the member's own body is never called (no procedure invocation
    // machinery exists yet). Matches VBCollectionType's own constructor logic for finding this member.
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
