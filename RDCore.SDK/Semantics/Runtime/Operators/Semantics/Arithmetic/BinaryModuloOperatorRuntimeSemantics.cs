using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.LetCoercion;
using RDCore.SDK.Semantics.Runtime.Operators.Context;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.SDK.Semantics.Runtime.Operators.Semantics.Arithmetic
{
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
                && frame[OperandIndex.BinaryLeftOperand] is VBNumericTypedValue lhsNumeric 
                && frame[OperandIndex.BinaryRightOperand] is VBNumericTypedValue rhsNumeric)
            {
                if (rhsNumeric.ManagedValue == 0)
                {
                    OnDivisionByZero(expression, Exceptions.VBDivisionOp_DivisionByZero);
                }

                return RuntimeSemanticsEvaluationResult.Success(
                    VBTypedValueFactory.CreateValue((VBNumericType)frame.EffectiveType, expression.ResultSymbol,
                    EvaluateManagedNumericOp(lhsNumeric.ManagedValue, rhsNumeric.ManagedValue)));
            }
            else if (frame.EffectiveType is VBNullType)
            {
                return EvaluateNullBinaryExpressionResult(expression.ResultSymbol);
            }

            return RuntimeSemanticsEvaluationResult.InternalError();
        }
    }
}
