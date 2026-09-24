using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.AST.Abstract;
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
    protected override T EvaluateBitwiseOp<T>(T lhs, T rhs) => lhs | rhs;

    protected override RuntimeSemanticsEvaluationResult EvaluateSemanticallly(
        ISymbolResolver resolver,
        ExpressionNode expression,
        OperatorEvaluationFrame frame)
    {
        var lhs = frame[InputIndex.BinaryLeftOperand];
        var rhs = frame[InputIndex.BinaryRightOperand];

        if (AsNullOperandTableValue(lhs) is double lhsValue && rhs is VBNullValue)
        {
            return RuntimeSemanticsEvaluationResult.Success(CreateNullOperandTableResult(frame.EffectiveType, lhsValue));
        }

        if (AsNullOperandTableValue(rhs) is double rhsValue && lhs is VBNullValue)
        {
            return RuntimeSemanticsEvaluationResult.Success(CreateNullOperandTableResult(frame.EffectiveType, rhsValue));
        }

        return RuntimeSemanticsEvaluationResult.InternalError();
    }
}
