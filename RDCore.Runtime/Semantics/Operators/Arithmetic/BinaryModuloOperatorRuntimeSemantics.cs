using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.Operators.Arithmetic;

/// <summary>
/// MS-VBAL 5.6.9.3.6 Binary '\' Operator and 'Mod' Operator (runtime semantics)
/// </summary>
public sealed record class BinaryModuloOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider,
    IVerboseMessageBuilder FormatterService)
    : BinaryIntegerDivisionOperatorRuntimeSemantics(LetCoercionProvider, FormatterService)
{
    protected override double EvaluateManagedNumericOp(double lhs, double rhs) => Math.DivRem((int)lhs, (int)rhs).Remainder;

    protected override RuntimeSemanticsEvaluationResult EvaluateExpressionResult(
        IVBExecutionContext runtime, 
        SemanticContext<ArithmeticOperatorSemanticFlags> context, 
        VBBinaryOperatorExpression<BinaryArithmeticOperatorSemanticContext, ArithmeticOperatorSemanticFlags> expression, 
        OperatorEvaluationFrame frame)
    {
        if (frame.EffectiveType is VBByteType or VBIntegerType or VBLongType or VBLongLongType
            && frame[InputIndex.BinaryLeftOperand] is VBNumericTypedValue lhsNumeric 
            && frame[InputIndex.BinaryRightOperand] is VBNumericTypedValue rhsNumeric)
        {
            if (rhsNumeric.ManagedValue.InteropValue!.Value.Double == 0)
            {
                OnDivisionByZero(expression, Exceptions.VBDivisionOp_DivisionByZero);
            }

            return RuntimeSemanticsEvaluationResult.Success(
                VBTypedValueFactory.CreateValue((VBNumericType)frame.EffectiveType, expression.ResultSymbol,
                EvaluateManagedNumericOp(
                    lhsNumeric.ManagedValue.InteropValue!.Value.Double, 
                    rhsNumeric.ManagedValue.InteropValue!.Value.Double)));
        }
        else if (frame.EffectiveType is VBNullType)
        {
            return EvaluateNullBinaryExpressionResult(expression.ResultSymbol);
        }

        return RuntimeSemanticsEvaluationResult.InternalError();
    }
}
