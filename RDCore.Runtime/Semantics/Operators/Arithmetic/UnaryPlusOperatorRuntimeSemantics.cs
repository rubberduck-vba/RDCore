using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.Operators.Arithmetic;

/// <summary>
/// <strong>RD-VBAL 5.6.9.3.1.1 Unary '+' Operator</strong> (runtime semantics)
/// </summary>
/// <remarks>
/// 👉 This operator is omitted from the MS-VBAL specifications, but is clearly implemented in MS-VBA and therefore deemed to be implicitly specified.
/// The effect of the unary '+' operator is the application of all the let-coercion and null-exception rules to the resulting value.
/// </remarks>
public sealed record class UnaryPlusOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider,
    IVerboseMessageBuilder FormatterService) 
    : UnaryArithmeticOperatorRuntimeSemantics(LetCoercionProvider, FormatterService)
{
    /// <remarks>
    /// The <em>effective type</em> runtime semantics of the unary '+' operator are inferred to match those of MS-VBAL 5.6.9.3.1: the planner has the same
    /// table for both.
    /// </remarks>
    protected override string Operator => Tokens.AdditionOp;

    protected override T EvaluateNumericOp<T>(T operand) => +operand;

    protected override RuntimeSemanticsEvaluationResult EvaluateExpressionResult(
        ISymbolResolver resolver,
        UnaryArithmeticOperatorSemanticContext context,
        ExpressionNode expression,
        OperatorEvaluationFrame frame) => frame.EffectiveType switch
        {
            VBNumericType numericEffectiveType when frame[InputIndex.UnaryOperand] is VBNumericTypedValue numericOperand
                => EvaluateRuntimeSemantics(frame, numericEffectiveType, numericOperand, expression),

            // per specifications a VBDateValue operand was let-coerced into a VBDoubleValue during validation stage:
            VBDateType dateEffectiveType when frame[InputIndex.UnaryOperand] is VBNumericTypedValue numericOperand
                => EvaluateRuntimeSemantics(dateEffectiveType, numericOperand, expression),

            // a Null effective type is Null whatever holds it: a Variant holding Null is no different from Null.
            VBNullType nullEffectiveType => RuntimeSemanticsEvaluationResult.Success(nullEffectiveType.DefaultValue),

            _ => RuntimeSemanticsEvaluationResult.InternalError(),
        };
}
