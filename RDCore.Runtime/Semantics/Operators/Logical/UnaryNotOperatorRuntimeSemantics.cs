using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Runtime.Semantics.Operators.Logical;

/// <summary>
/// <strong>MS-VBAL 5.6.9.8.1</strong> <c>Not</c> operator.
/// </summary>
public record class UnaryNotOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider, 
    IErrorDetailsRecorder ErrorDetails) 
    : UnaryLogicalOperatorRuntimeSemantics(LetCoercionProvider, ErrorDetails)
{
    protected override string Operator => Tokens.LogicalNotOp;

    protected override T EvaluateBitwiseOp<T>(T operand) => ~operand;

    protected override RuntimeSemanticsEvaluationResult EvaluateExpressionResult(ISymbolResolver resolver,
        UnaryLogicalOperatorSemanticContext context,
        ExpressionNode expression,
        OperatorEvaluationFrame frame) =>
        frame.EffectiveType switch
        {
            VBNumericType numericEffectiveType when frame.EffectiveType is IIntegralNumericType && frame[InputIndex.UnaryOperand] is VBNumericTypedValue numericOperand 
                => RuntimeSemanticsEvaluationResult.Success(EvaluateRuntimeSemantics(numericEffectiveType, numericOperand)),

            // the operand was let-coerced to the Boolean it negates.
            VBBooleanType when frame[InputIndex.UnaryOperand] is VBBooleanValue boolean
                => RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(boolean.Value.StoredValue == 0)),

            // a Null effective type is Null whatever holds it: a Variant holding Null is no different from Null (MS-VBAL 5.6.9.8.1).
            VBNullType nullEffectiveType => RuntimeSemanticsEvaluationResult.Success(nullEffectiveType.DefaultValue),

            _ => RuntimeSemanticsEvaluationResult.InternalError(),
        };
}