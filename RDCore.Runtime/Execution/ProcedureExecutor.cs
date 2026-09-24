using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.Statements;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
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
/// <c>Halt</c>, <c>Break</c> are wired too. Every other kind (<c>JumpTable</c>, the loop kinds) is not yet
/// wired and defers with <see cref="RuntimeExecutionOutcome.InternalError"/> rather than being silently
/// mishandled.
/// </para>
/// </remarks>
public sealed class ProcedureExecutor(IStatementRuntimeSemanticsProvider statements, ConditionEvaluator conditions, WithTargetEvaluator withTargets, CaseMatchEvaluator cases)
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
            var instruction = list.Items[activation.Pc];
            var instructionContext = ResolveContext(context, activation, instruction);

            switch (instruction.Kind)
            {
                case InstructionKind.Simple:
                    var outcome = statements.Execute(session, instructionContext, instruction.Node!);
                    if (outcome.Kind != RuntimeExecutionOutcomeKind.Next)
                    {
                        return outcome;
                    }
                    activation.Pc = instruction.Offset + 1;
                    break;

                case InstructionKind.Jump:
                    if (instruction.Target is not { } target)
                    {
                        // an unresolved label already reported its own VBC09309 at lowering time.
                        return RuntimeExecutionOutcome.InternalError;
                    }
                    activation.Pc = target;
                    break;

                case InstructionKind.ConditionalBranch:
                    var branchOutcome = ExecuteConditionalBranch(session, instructionContext, instruction, activation);
                    if (branchOutcome is { } stop)
                    {
                        return stop;
                    }
                    break;

                case InstructionKind.With:
                    var withOutcome = ExecuteWith(session, instructionContext, instruction, activation);
                    if (withOutcome is { } stopWith)
                    {
                        return stopWith;
                    }
                    break;

                case InstructionKind.Select:
                    var selectOutcome = ExecuteSelect(session, instructionContext, instruction, activation);
                    if (selectOutcome is { } stopSelect)
                    {
                        return stopSelect;
                    }
                    break;

                case InstructionKind.ExitProcedure:
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

        if (GetCondition(instruction.Node) is not { } condition)
        {
            // a lowering bug - every ConditionalBranch is either a Case header (Matching set) or one of
            // the If-family node kinds GetCondition recognizes.
            return RuntimeExecutionOutcome.InternalError;
        }

        var conditionResult = conditions.EvaluateBoolean(session, condition, context);
        return Branch(activation, instruction, conditionResult);
    }

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

    // The If/ElseIf/inline-If header node kinds only - a Select Case's own Case header
    // (CaseExpressionStatementNode) needs different handling entirely (range-clause matching against a
    // stashed selector, not a single Boolean condition), not this method's job.
    private static ExpressionNode? GetCondition(StatementNode? node) => node switch
    {
        IfBlockStatementNode ifBlock => ifBlock.ConditionExpression,
        ElseIfBlockStatementNode elseIf => elseIf.ConditionExpression,
        InlineIfStatementNode inlineIf => inlineIf.ConditionExpression,
        _ => null,
    };
}
