using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK;
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
using RDCore.SDK.Semantics.Runtime.Operators;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.Operators.Arithmetic;

/// <summary>
/// MS-VBAL 5.6.9.3.5 Binary '/' Operator (runtime semantics)
/// </summary>
public record class BinaryDivisionOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider,
    IVerboseMessageBuilder FormatterService)
    : BinaryArithmeticOperatorRuntimeSemantics(LetCoercionProvider, FormatterService)
{
    protected override double EvaluateManagedNumericOp(double lhs, double rhs) => lhs / rhs;

    protected override DetermineOperatorEffectiveTypeResult DetermineArithmeticOperatorEffectiveType(
        ISymbolResolver resolver, 
        BinaryArithmeticOperatorSemanticContext context, 
        VBBinaryOperatorExpression<BinaryArithmeticOperatorSemanticContext, ArithmeticOperatorSemanticFlags> expression, 
        OperatorEvaluationFrame frame) => frame[InputIndex.BinaryLeftOperand].TypeInfo switch
        {
            VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBLongLongType or VBEmptyType
                when frame[InputIndex.BinaryRightOperand].GetTargetType() is VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBLongLongType or VBEmptyType 
                => DetermineOperatorEffectiveTypeResult.Success(VBDoubleType.TypeInfo),

            VBDoubleType or VBStringType or VBCurrencyType or VBDateType
                when frame[InputIndex.BinaryRightOperand].GetTargetType() is INumericType or VBStringType or VBDateType or VBEmptyType 
                => DetermineOperatorEffectiveTypeResult.Success(VBDoubleType.TypeInfo),

            INumericType or VBStringType or VBDateType or VBEmptyType
                when frame[InputIndex.BinaryRightOperand].GetTargetType() is VBDoubleType or VBStringType or VBCurrencyType or VBDateType 
                => DetermineOperatorEffectiveTypeResult.Success(VBDoubleType.TypeInfo),

            _ => DetermineOperatorEffectiveTypeResult.NotApplicable()
        };

    protected override RuntimeSemanticsEvaluationResult EvaluateExpressionResult(
        IVBExecutionContext runtime,
        SemanticContext<ArithmeticOperatorSemanticFlags> context,
        VBBinaryOperatorExpression<BinaryArithmeticOperatorSemanticContext, ArithmeticOperatorSemanticFlags> expression,
        OperatorEvaluationFrame frame)
    {
        var lhs = frame[InputIndex.BinaryLeftOperand];
        var rhs = frame[InputIndex.BinaryRightOperand];

        if (frame.EffectiveType is VBDecimalType)
        {
            var rhsNumeric = (VBNumericTypedValue)rhs;
            if (rhsNumeric.ManagedValue.InteropValue!.Value.Decimal!.Value.StoredValue == 0)
            {
                return OnDivisionByZero(expression, Exceptions.VBDivisionOp_DivisionByZero);
            }
        }
        else if (frame.EffectiveType is VBSingleType or VBDoubleType)
        {
            var lhsNumeric = (VBNumericTypedValue)lhs;
            var rhsNumeric = (VBNumericTypedValue)rhs;
            if (rhsNumeric.ManagedValue.InteropValue!.Value.Double == 0)
            {
                //if (lhsNumeric is VBDoubleValue && rhsNumeric is VBDoubleValue)
                //{
                //    // MS-VBAL trying to be cute here:
                //    // * if this expression was within the RHS of a Let statement
                //    // * and both operators have a declared type of Double
                //    // * the resulting IEEE 754 Double special value (+/- infinity, NaN) is assigned...

                //    // since we're not throwing any errors, we get the overflow into the result,
                //    // and then let-assignment semantics would know what to do.
                //}

                return lhsNumeric.ManagedValue.InteropValue!.Value.Double == 0 && !(lhs is VBSingleValue or VBDoubleValue or VBStringValue or VBDateValue && rhs is VBEmptyValue)
                    ? OnOverflow(expression, Exceptions.VBRuntimeError_ArithmeticOverflow)
                    : OnDivisionByZero(expression, Exceptions.VBDivisionOp_DivisionByZero);
            }

            return EvaluateBinaryExpressionResult((VBNumericType)frame.EffectiveType, expression.ResultSymbol, lhsNumeric, rhsNumeric);
        }
        else if (frame.EffectiveType is VBNullType)
        {
            return EvaluateNullBinaryExpressionResult(expression.ResultSymbol);
        }

        return RuntimeSemanticsEvaluationResult.InternalError();
    }
}
