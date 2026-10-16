using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Model.Values.Meta;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.AST.Abstract;
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
using RDCore.SDK.Services.VerboseMessages;

using RDCore.SDK.Model;

namespace RDCore.Runtime.Semantics.Operators.Arithmetic;

/// <summary>
/// MS-VBAL 5.6.9.3.2 Binary '+' operator (runtime semantics)
/// </summary>
public sealed record class BinaryAdditionOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider, 
    IVerboseMessageBuilder FormatterService)
    : BinaryArithmeticOperatorRuntimeSemantics(LetCoercionProvider, FormatterService)
{
    protected sealed override T EvaluateManagedNumericOp<T>(T lhs, T rhs) => checked(lhs + rhs);

    protected override string Operator => Tokens.AdditionOp;

    protected override RuntimeSemanticsEvaluationResult EvaluateBinaryOperatorExpressionResult(
        ISymbolResolver resolver,
        BinaryArithmeticOperatorSemanticContext context,
        ExpressionNode expression,
        OperatorEvaluationFrame frame)
    {
        switch (frame.EffectiveType)
        {
            case VBNumericType numericEffectiveType:
                return EvaluateBinaryExpressionResult(frame, numericEffectiveType,
                    (VBNumericTypedValue)frame[InputIndex.BinaryLeftOperand],
                    (VBNumericTypedValue)frame[InputIndex.BinaryRightOperand],
                    expression);

            case VBDateType dateEffectiveType:
                return EvaluateBinaryExpressionResult(dateEffectiveType,
                    (VBNumericTypedValue)frame[InputIndex.BinaryLeftOperand],
                    (VBNumericTypedValue)frame[InputIndex.BinaryRightOperand],
                    expression);

            case VBStringType stringEffectiveType:
                // The result is the right operand string concatenated to the left operand string
                // IMPLEMENTATION NOTE: it isn't clear whether operands should be let-coerced left-to-right or right-to-left.
                // However, neither coercion would actually throw us out of the evaluation pipeline in case of error.

                var leftOperand = frame[InputIndex.BinaryLeftOperand];
                var leftCoercion = LetCoercionProvider.EvaluateLetCoercionSemantics(
                    resolver: resolver, 
                    expression: expression, 
                    frame: new(expression.Identity, InputIndex.BinaryLeftOperand, leftOperand, 
                        new VBTypeDescValue(stringEffectiveType)));

                var rightOperand = frame[InputIndex.BinaryRightOperand];
                var rightCoercion = LetCoercionProvider.EvaluateLetCoercionSemantics(
                    resolver: resolver,
                    expression: expression,
                    frame: new(expression.Identity, InputIndex.BinaryRightOperand, rightOperand,
                        new VBTypeDescValue(stringEffectiveType)));

                if (leftCoercion.IsSuccess && rightCoercion.IsSuccess)
                {
                    return EvaluateBinaryExpressionResult((VBStringValue)leftCoercion.Result!, (VBStringValue)rightCoercion.Result!);
                }
                else if (leftCoercion.ErrorInfo is not null || rightCoercion.ErrorInfo is not null)
                {
                    return RuntimeSemanticsEvaluationResult.Error((leftCoercion.ErrorInfo ?? rightCoercion.ErrorInfo)!);
                }
                break;

            case VBNullType:
                return EvaluateNullBinaryExpressionResult();
        }

        return RuntimeSemanticsEvaluationResult.InternalError();
    }

    // a String + is the same concatenation as &: every byte of the left operand's data, then every byte of the right's.
    private static RuntimeSemanticsEvaluationResult EvaluateBinaryExpressionResult(VBStringValue lhs, VBStringValue rhs)
        => RuntimeSemanticsEvaluationResult.Success(VBStringValue.Concat(lhs, rhs));
}
