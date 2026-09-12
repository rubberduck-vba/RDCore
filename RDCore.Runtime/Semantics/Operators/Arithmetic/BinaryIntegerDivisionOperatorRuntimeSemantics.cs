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
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.Operators.Arithmetic;

/// <summary>
/// MS-VBAL 5.6.9.3.6 Binary '\' Operator and 'Mod' Operator (runtime semantics)
/// </summary>
public record class BinaryIntegerDivisionOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider,
    IVerboseMessageBuilder FormatterService)
    : BinaryArithmeticOperatorRuntimeSemantics(LetCoercionProvider, FormatterService)
{
    protected override T EvaluateManagedNumericOp<T>(T lhs, T rhs) => lhs / rhs;

    protected override DetermineOperatorEffectiveTypeResult DetermineArithmeticOperatorEffectiveType(
        ISymbolResolver resolver, 
        BinaryArithmeticOperatorSemanticContext context, 
        VBBinaryOperatorExpressionNode expression, 
        OperatorEvaluationFrame frame) 
    {
        var rhs = frame[InputIndex.BinaryRightOperand].TypeInfo;
        return frame[InputIndex.BinaryLeftOperand].TypeInfo switch
        {
            VBByteType when rhs is VBEmptyType 
                => DetermineOperatorEffectiveTypeResult.Success(VBIntegerType.TypeInfo),
            VBEmptyType when rhs is VBByteType 
                => DetermineOperatorEffectiveTypeResult.Success(VBIntegerType.TypeInfo),
        
            VBBooleanType or VBIntegerType 
                when rhs is VBSingleType or VBDoubleType or VBStringType or VBCurrencyType or VBDateType or VBDecimalType 
                => DetermineOperatorEffectiveTypeResult.Success(VBIntegerType.TypeInfo),

            IFloatingPointNumericType or IFixedPointNumericType or VBStringType or VBDateType
                when rhs is VBNumericType and not VBLongLongType or VBStringType or VBDateType or VBEmptyType 
                => DetermineOperatorEffectiveTypeResult.Success(VBLongType.TypeInfo),

            VBNumericType and not VBLongLongType or VBStringType or VBDateType or VBEmptyType
                when rhs is IFloatingPointNumericType or IFixedPointNumericType or VBStringType or VBDateType 
                => DetermineOperatorEffectiveTypeResult.Success(VBLongType.TypeInfo),

            VBLongLongType when rhs is VBNumericType or VBStringType or VBDateType or VBEmptyType 
                => DetermineOperatorEffectiveTypeResult.Success(VBLongLongType.TypeInfo),
            VBNumericType or VBStringType or VBDateType or VBEmptyType when rhs is VBLongLongType 
                => DetermineOperatorEffectiveTypeResult.Success(VBLongLongType.TypeInfo),

            _ => DetermineOperatorEffectiveTypeResult.NotApplicable()
        };
    }

    protected override RuntimeSemanticsEvaluationResult EvaluateExpressionResult(
        ISymbolResolver resolver,
        BinaryArithmeticOperatorSemanticContext context,
        VBBinaryOperatorExpressionNode expression,
        OperatorEvaluationFrame frame)
    {
        var lhs = frame[InputIndex.BinaryLeftOperand];
        var rhs = frame[InputIndex.BinaryRightOperand];

        // '\' and 'Mod' always have an integral effective type, so the operands have been let-coerced
        // (banker's-rounded) to it already: the managed operation is a plain integer division / remainder,
        // and a zero divisor surfaces as DivideByZeroException from the dispatcher.
        if (frame.EffectiveType is VBByteType or VBIntegerType or VBLongType or VBLongLongType
            && lhs is VBNumericTypedValue lhsValue && rhs is VBNumericTypedValue rhsValue)
        {
            return EvaluateManagedArithmetic((VBNumericType)frame.EffectiveType, lhsValue, rhsValue, expression);
        }
        else if (frame.EffectiveType is VBNullType)
        {
            return EvaluateNullBinaryExpressionResult();
        }

        return RuntimeSemanticsEvaluationResult.InternalError();
    }
}
