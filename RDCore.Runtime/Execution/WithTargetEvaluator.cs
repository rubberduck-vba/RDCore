using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.Statements;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Evaluates a <c>With</c> statement's target expression and produces the value that would be stored in
/// its anonymous block variable (<strong>MS-VBAL §5.4.2.21</strong>).
/// </summary>
public sealed class WithTargetEvaluator(RuntimeExpressionEvaluator expressionEvaluator, WithStatementRuntimeSemantics withStatement)
{
    /// <summary>
    /// Evaluates <paramref name="node"/>'s target expression and Set/Let-coerces it, per
    /// <see cref="WithStatementRuntimeSemantics"/>.
    /// </summary>
    public RuntimeSemanticsEvaluationResult Evaluate(IRuntimeSession session, WithStatementNode node, RuntimeEvaluationContext context)
    {
        var targetResult = expressionEvaluator.Evaluate(session, node.WithExpression, context);
        if (!targetResult.IsSuccess)
        {
            return targetResult;
        }

        return withStatement.Evaluate(session, new(), node, targetResult.Result!);
    }
}
