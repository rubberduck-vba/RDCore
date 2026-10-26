using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;
using RDCore.SDK.Services.VerboseMessages;

using RDCore.SDK.Model;

namespace RDCore.Runtime.Semantics.Operators.Arithmetic;

/// <summary>
/// MS-VBAL 5.6.9.3.5 Binary '/' Operator (runtime semantics)
/// </summary>
public record class BinaryDivisionOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider,
    IVerboseMessageBuilder FormatterService)
    : BinaryArithmeticOperatorRuntimeSemantics(LetCoercionProvider, FormatterService)
{
    protected override T EvaluateManagedNumericOp<T>(T lhs, T rhs) => lhs / rhs;

    protected override string Operator => Tokens.DivisionOp;

    protected override RuntimeSemanticsEvaluationResult EvaluateBinaryOperatorExpressionResult(
        ISymbolResolver resolver,
        BinaryArithmeticOperatorSemanticContext context,
        ExpressionNode expression,
        OperatorEvaluationFrame frame)
    {
        var lhs = frame[InputIndex.BinaryLeftOperand];
        var rhs = frame[InputIndex.BinaryRightOperand];

        if (frame.EffectiveType is VBDecimalType decimalEffectiveType)
        {
            var lhsNumeric = (VBNumericTypedValue)lhs;
            var rhsNumeric = (VBNumericTypedValue)rhs;
            // the runtime value is the decimal's storage struct wrapped in a runtime value; its managed value is the number.
            if (Convert.ToDecimal(rhsNumeric.RuntimeValue.BoxedValue) == 0)
            {
                return OnDivisionByZero(expression, Exceptions.VBDivisionOp_DivisionByZero);
            }

            return EvaluateBinaryExpressionResult(frame, decimalEffectiveType, lhsNumeric, rhsNumeric, expression);
        }
        else if (frame.EffectiveType is VBSingleType or VBDoubleType)
        {
            var lhsNumeric = (VBNumericTypedValue)lhs;
            var rhsNumeric = (VBNumericTypedValue)rhs;
            // MS-VBAL §5.6.9.3.5: a division by zero of typed operands is the extended format's (ExtendedIntermediates) - error 11, or 6 for 0 / 0, carrying
            // the IEEE 754 value of the division, which a Let assignment assigns to a Single or Double variable before raising it. A Variant operand's
            // assigns nothing (recorded in Excel, grid oracle\s16 D08, F05-F06, H03).
            if (rhsNumeric.AsDouble == 0 && frame.HasVariantOperand)
            {
                return lhsNumeric.AsDouble == 0 && !(lhs is VBSingleValue or VBDoubleValue or VBStringValue or VBDateValue && rhs is VBEmptyValue)
                    ? OnOverflow(expression, Exceptions.VBRuntimeError_ArithmeticOverflow)
                    : OnDivisionByZero(expression, Exceptions.VBDivisionOp_DivisionByZero);
            }

            return EvaluateBinaryExpressionResult(frame, (VBNumericType)frame.EffectiveType, lhsNumeric, rhsNumeric, expression);
        }
        else if (frame.EffectiveType is VBNullType)
        {
            return EvaluateNullBinaryExpressionResult();
        }

        return RuntimeSemanticsEvaluationResult.InternalError();
    }
}
