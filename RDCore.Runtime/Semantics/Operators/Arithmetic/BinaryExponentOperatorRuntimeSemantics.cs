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
/// MS-VBAL 5.6.9.3.7 Binary '^' Operator (runtime semantics)
/// </summary>
public record class BinaryExponentOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider,
    IVerboseMessageBuilder FormatterService)
    : BinaryArithmeticOperatorRuntimeSemantics(LetCoercionProvider, FormatterService)
{
    protected override double EvaluateManagedNumericOp(double lhs, double rhs) => Math.Pow(lhs, rhs);

    protected override DetermineOperatorEffectiveTypeResult DetermineArithmeticOperatorEffectiveType(
        ISymbolResolver resolver, 
        BinaryArithmeticOperatorSemanticContext context, 
        VBBinaryOperatorExpression<BinaryArithmeticOperatorSemanticContext, ArithmeticOperatorSemanticFlags> expression, 
        OperatorEvaluationFrame frame) => frame[InputIndex.BinaryLeftOperand].TypeInfo switch
        {
            VBNumericType or VBStringType or VBDateType or VBEmptyType
                when frame[InputIndex.BinaryRightOperand].TypeInfo is VBNumericType or VBStringType or VBDateType or VBEmptyType
                => DetermineOperatorEffectiveTypeResult.Success(VBDoubleType.TypeInfo),

            _ => DetermineOperatorEffectiveTypeResult.NotApplicable()
        };

    protected override RuntimeSemanticsEvaluationResult EvaluateExpressionResult(
        IVBExecutionContext runtime, 
        SemanticContext<ArithmeticOperatorSemanticFlags> context, 
        VBBinaryOperatorExpression<BinaryArithmeticOperatorSemanticContext, ArithmeticOperatorSemanticFlags> expression, 
        OperatorEvaluationFrame frame)
    {
        if (frame.EffectiveType is VBDoubleType 
            && frame[InputIndex.BinaryLeftOperand] is VBNumericTypedValue lhsValue 
            && frame[InputIndex.BinaryRightOperand] is VBNumericTypedValue rhsValue)
        {
            if (lhsValue.ManagedValue == 0 && rhsValue.ManagedValue == 0)
            {
                return RuntimeSemanticsEvaluationResult.Success(
                    VBTypedValueFactory.CreateValue(frame.EffectiveType, expression.ResultSymbol, VBDoubleType.One.Value));
            }

            if (lhsValue.ManagedValue == 0 && rhsValue.ManagedValue < 0)
            {
                // if LHS is zero and RHS is negative, we must raise error 5.
                return OnInvalidProcedureCallOrArgument(expression, Exceptions.VBExponentOp_InvalidProcedureCallOrArgument_Verbose);
            }

            return RuntimeSemanticsEvaluationResult.Success(
                VBTypedValueFactory.CreateValue(frame.EffectiveType, expression.ResultSymbol, 
                EvaluateManagedNumericOp(lhsValue.ManagedValue, rhsValue.ManagedValue)));
        }
        else if (frame.EffectiveType is VBNullType)
        {
            return EvaluateNullBinaryExpressionResult(expression.ResultSymbol);
        }

        return RuntimeSemanticsEvaluationResult.InternalError();
    }
}
