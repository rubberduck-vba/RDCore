using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK;
using RDCore.SDK.Model.AST.Abstract;
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

using RDCore.SDK.Model;

namespace RDCore.Runtime.Semantics.Operators.Arithmetic;

/// <summary>
/// MS-VBAL 5.6.9.3.7 Binary '^' Operator (runtime semantics)
/// </summary>
public record class BinaryExponentOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider,
    IErrorDetailsRecorder ErrorDetails)
    : BinaryArithmeticOperatorRuntimeSemantics(LetCoercionProvider, ErrorDetails)
{
    // '^' always has a Double effective type; IEEE-754 exponentiation does not fit INumber<T> cleanly.
    protected override T EvaluateManagedNumericOp<T>(T lhs, T rhs)
        => T.CreateChecked(Math.Pow(double.CreateChecked(lhs), double.CreateChecked(rhs)));

    protected override string Operator => Tokens.PowerOp;

    protected override RuntimeSemanticsEvaluationResult EvaluateBinaryOperatorExpressionResult(
        IRuntimeSymbolResolver resolver,
        BinaryArithmeticOperatorSemanticContext context, 
        ExpressionNode expression, 
        OperatorEvaluationFrame frame)
    {
        if (frame.EffectiveType is VBDoubleType 
            && frame[InputIndex.BinaryLeftOperand] is VBNumericTypedValue lhsValue 
            && frame[InputIndex.BinaryRightOperand] is VBNumericTypedValue rhsValue)
        {
            if (lhsValue.AsDouble == 0 && rhsValue.AsDouble == 0)
            {
                return RuntimeSemanticsEvaluationResult.Success(
                    ((VBNumericType)frame.EffectiveType).CreateValue(VBDoubleType.One.Value));
            }

            if (lhsValue.AsDouble == 0 && rhsValue.AsDouble < 0)
            {
                // if LHS is zero and RHS is negative, we must raise error 5.
                return OnInvalidProcedureCallOrArgument(expression, Exceptions.VBExponentOp_InvalidProcedureCallOrArgument_Verbose);
            }

            // of finite operands, a power that is no number - a negative number to a fraction - is error 5, and one past the range of a Double is an
            // overflow, Variant or not: the Double effective type has nothing wider (MS-VBAL §5.6.9.3; observed in Excel).
            var power = EvaluateManagedNumericOp(lhsValue.AsDouble, rhsValue.AsDouble);
            if (!double.IsFinite(power) && double.IsFinite(lhsValue.AsDouble) && double.IsFinite(rhsValue.AsDouble))
            {
                return double.IsNaN(power)
                    ? OnInvalidProcedureCallOrArgument(expression, Exceptions.VBExponentOp_NoRealPower_Verbose)
                    : OnOverflow(expression, Exceptions.VBRuntimeError_ArithmeticOverflow);
            }

            return RuntimeSemanticsEvaluationResult.Success(((VBNumericType)frame.EffectiveType).CreateValue(power));
        }
        else if (frame.EffectiveType is VBNullType)
        {
            return EvaluateNullBinaryExpressionResult();
        }

        return RuntimeSemanticsEvaluationResult.InternalError();
    }
}
