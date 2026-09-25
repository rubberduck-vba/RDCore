using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;
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
/// <c>"__c()_op"</c> operator (<strong>RD-VBAL §5.6.9.9</strong>) exists to represent. This calls the real
/// <strong>MS-VBAL §5.5.1.2.2</strong> Boolean let-coercion rule directly instead, bypassing that operator
/// (and the coercion provider's own strategy-dispatch machinery, since the destination type here is always
/// known to be <c>Boolean</c>) entirely.
/// </remarks>
public sealed class ConditionEvaluator(RuntimeExpressionEvaluator expressionEvaluator, VBBooleanLetCoercionRuntimeSemantics booleanCoercion)
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

        // unwrap Variant: bypassing the provider skips its own unwrap too.
        var source = valueResult.Result!;
        while (source is VBVariantValue { TypedValue: var wrapped })
        {
            source = wrapped;
        }

        var frame = new LetCoercionStackFrame(condition.Identity, InputIndex.CoercionSourceValue, source, new VBTypeDescValue(VBBooleanType.TypeInfo));
        var coercionResult = booleanCoercion.EvaluateLetCoercion(session.Symbols.Resolver, condition, frame);

        if (!coercionResult.IsApplicable)
        {
            // every source VBTypedValue kind a condition can legitimately evaluate to is already
            // handled by VBBooleanLetCoercionRuntimeSemantics's own switch (MS-VBAL §5.5.1.2.2/.4);
            // NotApplicable here means the source's own type isn't one of them yet - a coverage gap,
            // not a well-formed program's own runtime error, so it's reported the same way the
            // coercion provider itself reports a strategy that unexpectedly declines its own dispatch.
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        return coercionResult.IsSuccess
            ? RuntimeSemanticsEvaluationResult.Success(coercionResult.Result!)
            : RuntimeSemanticsEvaluationResult.Error(coercionResult.ErrorInfo!);
    }
}
