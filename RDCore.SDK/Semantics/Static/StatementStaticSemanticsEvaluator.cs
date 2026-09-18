using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Semantics.Static.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// Recursively walks a real, arbitrarily-nested statement tree, evaluating every expression it
/// contains via <see cref="ExpressionStaticSemanticsEvaluator"/>, threading the innermost enclosing
/// <c>With</c> block's target type (<strong>MS-VBAL §5.6.15</strong>) through its body, and checking
/// <c>Let</c>/<c>Set</c> assignment coercion validity between an assignment's <c>Target</c> and
/// <c>Value</c>.
/// </summary>
/// <remarks>
/// This is the statement-tree analogue of <see cref="ExpressionStaticSemanticsEvaluator"/>: nothing
/// previously walked a <see cref="StatementBlock"/>'s nested blocks (<c>If</c>/<c>Do</c>/<c>For</c>/
/// <c>Select Case</c>/<c>With</c>, ...) at all, so a <c>With</c> block's target type never had
/// anywhere to flow from — <see cref="ExpressionStaticSemanticsEvaluator"/> could only ever defer a
/// with-relative access, and an assignment's own coercion validity was never checked at all. Unlike an
/// expression tree, a statement tree's individual statements are largely independent of one another,
/// so this collects every error found across the whole tree rather than short-circuiting on the first
/// one the way the expression evaluator does.
/// </remarks>
public static class StatementStaticSemanticsEvaluator
{
    /// <summary>
    /// Walks every statement in <paramref name="block"/>, recursing into nested blocks and collecting
    /// every compile error found anywhere in the tree.
    /// </summary>
    /// <param name="context">
    /// The compile-time context to start walking from. <see cref="StaticEvaluationContext.EnclosingWithTargetType"/>
    /// should be <c>null</c> unless <paramref name="block"/> is itself already inside a <c>With</c>
    /// block relative to some outer context the caller is threading through.
    /// </param>
    /// <param name="block">The statement block to walk — a procedure body, or any nested block.</param>
    /// <returns>Every compile error found, in traversal order. Empty when the whole tree is valid.</returns>
    public static ImmutableArray<VBCompileErrorInfo> Evaluate(StaticEvaluationContext context, StatementBlock block)
    {
        var errors = ImmutableArray.CreateBuilder<VBCompileErrorInfo>();
        EvaluateBlock(context, block, errors);
        return errors.ToImmutable();
    }

    private static void EvaluateBlock(StaticEvaluationContext context, StatementBlock block, ImmutableArray<VBCompileErrorInfo>.Builder errors)
    {
        foreach (var child in block.Children)
        {
            if (child is StatementNode statement)
            {
                EvaluateStatement(context, statement, errors);
            }
        }
    }

    private static void EvaluateStatement(StaticEvaluationContext context, StatementNode statement, ImmutableArray<VBCompileErrorInfo>.Builder errors)
    {
        // WithStatementNode is the one case whose own Inputs result changes the context its Body (and
        // everything the body recursively contains) evaluates against - handled before the generic
        // Inputs pass below so the resolved target type can be threaded straight into bodyContext.
        if (statement is WithStatementNode withStatement)
        {
            var targetResult = ExpressionStaticSemanticsEvaluator.Evaluate(context, withStatement.WithExpression);
            CollectError(targetResult, errors);

            var bodyContext = targetResult.IsSuccess ? context with { EnclosingWithTargetType = targetResult.Result } : context;
            EvaluateBlock(bodyContext, withStatement.Body, errors);
            return;
        }

        // AssignmentStatementNode needs both Target's and Value's declared types kept around (not just
        // their error status) to run the coercion rule matching its Kind - the generic Inputs pass below
        // only ever checks IsError, so this is handled separately rather than folded into it.
        if (statement is AssignmentStatementNode assignment)
        {
            var targetResult = ExpressionStaticSemanticsEvaluator.Evaluate(context, assignment.Target);
            CollectError(targetResult, errors);
            var valueResult = ExpressionStaticSemanticsEvaluator.Evaluate(context, assignment.Value);
            CollectError(valueResult, errors);

            if (targetResult.IsSuccess && valueResult.IsSuccess && ResolveCoercionRule(assignment.Kind) is { } coercionRule)
            {
                CollectError(coercionRule.DetermineDeclaredType(context, assignment.Value, valueResult.Result!, targetResult.Result!), errors);
            }
            return;
        }

        foreach (var input in statement.Inputs)
        {
            if (input is ExpressionNode expression)
            {
                CollectError(ExpressionStaticSemanticsEvaluator.Evaluate(context, expression), errors);
            }
        }

        switch (statement)
        {
            case IfBlockStatementNode ifBlock:
                EvaluateBlock(context, ifBlock.Body, errors);
                foreach (var elseIfBlock in ifBlock.ElseIfBlocks)
                {
                    EvaluateStatement(context, elseIfBlock, errors);
                }
                if (ifBlock.ElseBlock is { } elseBlock)
                {
                    EvaluateStatement(context, elseBlock, errors);
                }
                break;
            case ElseIfBlockStatementNode elseIfBlockStatement:
                EvaluateBlock(context, elseIfBlockStatement.Body, errors);
                break;
            case ElseBlockStatementNode elseBlockStatement:
                EvaluateBlock(context, elseBlockStatement.Body, errors);
                break;
            case InlineIfStatementNode inlineIf:
                EvaluateBlock(context, inlineIf.ThenBody, errors);
                if (inlineIf.ElseBody is { } elseBody)
                {
                    EvaluateBlock(context, elseBody, errors);
                }
                break;
            case DoLoopStatementNode doLoop:
                EvaluateBlock(context, doLoop.Body, errors);
                break;
            case DoLoopUntilStatementNode doLoopUntil:
                EvaluateBlock(context, doLoopUntil.Body, errors);
                break;
            case DoLoopWhileStatementNode doLoopWhile:
                EvaluateBlock(context, doLoopWhile.Body, errors);
                break;
            case DoUntilLoopStatementNode doUntilLoop:
                EvaluateBlock(context, doUntilLoop.Body, errors);
                break;
            case DoWhileLoopStatementNode doWhileLoop:
                EvaluateBlock(context, doWhileLoop.Body, errors);
                break;
            case WhileWendStatementNode whileWend:
                EvaluateBlock(context, whileWend.Body, errors);
                break;
            case ForStatementNode forStatement:
                EvaluateBlock(context, forStatement.Body, errors);
                break;
            case ForEachStatementNode forEachStatement:
                EvaluateBlock(context, forEachStatement.Body, errors);
                break;
            case SelectCaseStatementNode selectCase:
                foreach (var caseExpressionBlock in selectCase.CaseExpressionBlocks)
                {
                    EvaluateStatement(context, caseExpressionBlock, errors);
                }
                if (selectCase.CaseElseBlock is { } caseElseBlock)
                {
                    EvaluateStatement(context, caseElseBlock, errors);
                }
                break;
            case CaseExpressionStatementNode caseExpressionStatement:
                foreach (var rangeClause in caseExpressionStatement.RangeClauses)
                {
                    EvaluateStatement(context, rangeClause, errors);
                }
                EvaluateBlock(context, caseExpressionStatement.Block, errors);
                break;
            case CaseElseClauseStatementNode caseElseClauseStatement:
                EvaluateBlock(context, caseElseClauseStatement.Body, errors);
                break;
        }
    }

    // LSet/RSet (MS-VBAL 5.4.3.6/5.4.3.7) have their own distinct static semantics - neither Let- nor
    // Set-coercion - which aren't modeled yet (a real, accepted gap; see FixedString let-coercion in
    // the runtime layer for the same kind of deliberate deferral). Falls through to null, deferred by
    // the caller like any other unmapped case.
    private static IStaticSemantics? ResolveCoercionRule(AssignmentKind kind) => kind switch
    {
        AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet => LetCoercionStaticSemantics.Instance,
        AssignmentKind.Set => SetCoercionStaticSemantics.Instance,
        _ => null,
    };

    private static void CollectError(StaticSemanticsEvaluationResult result, ImmutableArray<VBCompileErrorInfo>.Builder errors)
    {
        if (result.IsError)
        {
            errors.Add(result.ErrorInfo!);
        }
    }
}
