using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.Operators.Logical;

/// <summary>
/// MS-VBAL 5.6.9.8.3 Binary 'Or' Operator
/// </summary>
public record class BinaryOrLogicalOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionSemanticsProvider,
    IVerboseMessageBuilder FormatterService)
    : BinaryLogicalOperatorRuntimeSemantics(LetCoercionSemanticsProvider, FormatterService)
{
    protected override double EvaluateBitwiseOp(int lhs, int rhs) => lhs | rhs;

    protected override RuntimeSemanticsEvaluationResult EvaluateSemanticallly(
        ISymbolResolver resolver,
        VBBinaryOperatorExpressionNode expression,
        OperatorEvaluationFrame frame)
    {
        var lhs = frame[InputIndex.BinaryLeftOperand];
        var rhs = frame[InputIndex.BinaryRightOperand];

        return lhs switch
        {
            VBNumericTypedValue lhsNumeric when lhs.TypeInfo is IIntegralNumericType && rhs is VBNullValue 
                => RuntimeSemanticsEvaluationResult.Success(
                    ((VBNumericType)frame.EffectiveType).CreateValue((double)lhsNumeric.RuntimeValue.BoxedValue)),

            VBNullValue when rhs is VBNumericTypedValue rhsNumeric && rhsNumeric.TypeInfo is IIntegralNumericType 
                => RuntimeSemanticsEvaluationResult.Success(
                    ((VBNumericType)frame.EffectiveType).CreateValue((double)rhsNumeric.RuntimeValue.BoxedValue)),

            _ => RuntimeSemanticsEvaluationResult.InternalError()
        };
    }
}
