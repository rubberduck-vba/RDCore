using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
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
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.Operators.Logical;

/// <summary>
/// MS-VBAL 5.6.9.8.6 Binary 'Imp' Operator
/// </summary>
public record class BinaryImpLogicalOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionSemanticsProvider,
    IVerboseMessageBuilder FormatterService)
    : BinaryLogicalOperatorRuntimeSemantics(LetCoercionSemanticsProvider, FormatterService)
{
    protected override T EvaluateBitwiseOp<T>(T lhs, T rhs) => ~lhs | rhs;

    /// <summary>
    /// Evaluates the not-bitwise evaluation branches of the MS-VBAL specifications for a logical operator.
    /// </summary>
    /// <remarks>
    /// Operands are <strong>explicitly specified</strong> as being evaluated bitwise only given specific operand data types.
    /// Base implementation has already handled the case where both operands are <see cref="IIntegralNumericType"/>, and the case where they're both <see cref="VBNullValue"/>.
    /// </remarks>
    protected override RuntimeSemanticsEvaluationResult EvaluateSemanticallly(
        ISymbolResolver resolver, 
        VBBinaryOperatorExpressionNode expression, 
        OperatorEvaluationFrame frame)
    {
        var lhs = frame[InputIndex.BinaryLeftOperand];
        var rhs = frame[InputIndex.BinaryRightOperand];

        // the both-integral case is handled upstream by the bitwise dispatcher; here only Null-operand edges remain.
        if (AsNullOperandTableValue(lhs) is double lhsValue && rhs is VBNullValue)
        {
            return lhsValue != -1
                ? RuntimeSemanticsEvaluationResult.Success(
                    CreateNullOperandTableResult(frame.EffectiveType, EvaluateBitwiseOp((int)lhsValue, 0)))
                : EvaluateNullBinaryExpressionResult();
        }
        else if (lhs is VBNullValue && AsNullOperandTableValue(rhs) is double rhsValue)
        {
            return rhsValue != 0
                ? RuntimeSemanticsEvaluationResult.Success(CreateNullOperandTableResult(frame.EffectiveType, rhsValue))
                : EvaluateNullBinaryExpressionResult();
        }

        return RuntimeSemanticsEvaluationResult.InternalError();
    }
}
