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
/// Evaluates an <c>On…GoTo</c>/<c>On…GoSub</c> selector expression and forces the result to <c>Integer</c>
/// (<strong>MS-VBAL §5.4.2.13</strong>/<strong>§5.4.2.16</strong>: "Let n be the value of the evaluation
/// of &lt;expression&gt; after having been Let-coerced to declared type Integer").
/// </summary>
/// <remarks>
/// Same shape as <see cref="ConditionEvaluator"/>: the value is let-coerced by the coercion provider, the way any other coercion is.
/// </remarks>
public sealed class JumpTableEvaluator(RuntimeExpressionEvaluator expressionEvaluator, ILetCoercionRuntimeSemanticsProvider letCoercion)
{
    /// <summary>
    /// Evaluates <paramref name="selector"/> and let-coerces the result to <c>Integer</c>.
    /// </summary>
    public RuntimeSemanticsEvaluationResult EvaluateSelector(IRuntimeSession session, ExpressionNode selector, RuntimeEvaluationContext context)
    {
        var valueResult = expressionEvaluator.Evaluate(session, selector, context);
        if (!valueResult.IsSuccess)
        {
            return valueResult;
        }

        var frame = new LetCoercionStackFrame(selector.Identity, InputIndex.CoercionSourceValue, valueResult.Result!, new VBTypeDescValue(VBIntegerType.TypeInfo), ConversionSite.JumpSelector);
        var coercionResult = letCoercion.EvaluateLetCoercionSemantics(session.Symbols.Resolver, selector, frame);

        return coercionResult.IsSuccess
            ? RuntimeSemanticsEvaluationResult.Success(coercionResult.Result!)
            : RuntimeSemanticsEvaluationResult.Error(coercionResult.ErrorInfo!);
    }
}
