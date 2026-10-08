using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Facts;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Evaluates an <c>Error</c> statement's own number expression and forces the result to <c>Integer</c>
/// (<strong>MS-VBAL §5.4.4.3</strong>: "the data value of &lt;error-number&gt; MUST be a valid error
/// number").
/// </summary>
/// <remarks>
/// Same shape as <see cref="ConditionEvaluator"/>: the value is let-coerced by the coercion provider, the way any other coercion is.
/// </remarks>
public sealed class ErrorHandlingEvaluator(RuntimeExpressionEvaluator expressionEvaluator, ILetCoercionRuntimeSemanticsProvider letCoercion)
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

        var frame = new LetCoercionStackFrame(numberExpression.Identity, InputIndex.CoercionSourceValue, valueResult.Result!, new VBTypeDescValue(VBIntegerType.TypeInfo), ConversionSite.ErrorNumber);
        var coercionResult = letCoercion.EvaluateLetCoercionSemantics(session.Symbols.Resolver, numberExpression, frame);

        return coercionResult.IsSuccess
            ? RuntimeSemanticsEvaluationResult.Success(coercionResult.Result!)
            : RuntimeSemanticsEvaluationResult.Error(coercionResult.ErrorInfo!);
    }
}
