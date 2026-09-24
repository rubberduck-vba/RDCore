using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Evaluates an <c>Error</c> statement's own number expression and forces the result to <c>Integer</c>
/// (<strong>MS-VBAL §5.4.4.3</strong>: "the data value of &lt;error-number&gt; MUST be a valid error
/// number").
/// </summary>
/// <remarks>
/// Same shape as <see cref="ConditionEvaluator"/>: the destination type is always statically known
/// (<c>Integer</c>), so this calls <see cref="VBNumericLetCoercionTypeRuntimeSemantics"/> directly,
/// bypassing the coercion provider's own strategy-dispatch machinery.
/// </remarks>
public sealed class ErrorHandlingEvaluator(RuntimeExpressionEvaluator expressionEvaluator, VBNumericLetCoercionTypeRuntimeSemantics numericCoercion)
{
    /// <summary>
    /// Evaluates <paramref name="numberExpression"/> and let-coerces the result to <c>Integer</c>.
    /// </summary>
    public RuntimeSemanticsEvaluationResult EvaluateErrorNumber(IRuntimeSession session, ExpressionNode numberExpression, RuntimeEvaluationContext context)
    {
        var valueResult = expressionEvaluator.Evaluate(session, numberExpression, context);
        if (!valueResult.IsSuccess)
        {
            return valueResult;
        }

        var frame = new LetCoercionStackFrame(numberExpression.Identity, InputIndex.CoercionSourceValue, valueResult.Result!, new VBTypeDescValue(VBIntegerType.TypeInfo));
        var coercionResult = numericCoercion.EvaluateLetCoercion(session.Symbols.Resolver, numberExpression, frame);

        if (!coercionResult.IsApplicable)
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        return coercionResult.IsSuccess
            ? RuntimeSemanticsEvaluationResult.Success(coercionResult.Result!)
            : RuntimeSemanticsEvaluationResult.Error(coercionResult.ErrorInfo!);
    }
}
