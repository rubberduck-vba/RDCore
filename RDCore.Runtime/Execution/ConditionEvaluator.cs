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
/// Evaluates an expression and forces the result to <c>Boolean</c> — every VBA condition (<c>If</c>,
/// <c>Case Is</c>, a loop's <c>While</c>/<c>Until</c>) is evaluated this way.
/// </summary>
/// <remarks>
/// A condition's truth test has no operator node of its own in source — <c>If x Then</c> let-coerces
/// <c>x</c> without any <c>(...)</c> forcing an explicit let-coercion frame, which is what the
/// <c>"__c()_op"</c> operator (<strong>RD-VBAL §5.6.9.9</strong>) exists to represent. The value is
/// let-coerced to <c>Boolean</c> (<strong>MS-VBAL §5.5.1.2</strong>) by the coercion provider, the way any
/// other coercion is: a <c>Variant</c> by what it holds, an object by its default member.
/// </remarks>
public sealed class ConditionEvaluator(RuntimeExpressionEvaluator expressionEvaluator, ILetCoercionRuntimeSemanticsProvider letCoercion)
{
    /// <summary>
    /// Evaluates <paramref name="condition"/> and let-coerces the result to <c>Boolean</c>.
    /// </summary>
    public RuntimeSemanticsEvaluationResult EvaluateBoolean(IRuntimeSession session, ExpressionNode condition, RuntimeEvaluationContext context)
    {
        var valueResult = expressionEvaluator.Evaluate(session, condition, context);
        if (!valueResult.IsSuccess)
        {
            return valueResult;
        }

        var frame = new LetCoercionStackFrame(condition.Identity, InputIndex.CoercionSourceValue, valueResult.Result!, new VBTypeDescValue(VBBooleanType.TypeInfo));
        var coercionResult = letCoercion.EvaluateLetCoercionSemantics(session.Symbols.Resolver, condition, frame);

        return coercionResult.IsSuccess
            ? RuntimeSemanticsEvaluationResult.Success(coercionResult.Result!)
            : RuntimeSemanticsEvaluationResult.Error(coercionResult.ErrorInfo!);
    }
}
