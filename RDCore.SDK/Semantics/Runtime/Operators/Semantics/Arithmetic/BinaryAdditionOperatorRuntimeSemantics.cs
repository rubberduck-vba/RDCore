using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Abstract.Operators;
using RDCore.SDK.Semantics.Runtime.LetCoercion;
using RDCore.SDK.Semantics.Runtime.Operators.Context;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.SDK.Semantics.Runtime.Operators.Semantics.Arithmetic
{
    /// <summary>
    /// MS-VBAL 5.6.9.3.2 Binary '+' operator (runtime semantics)
    /// </summary>
    public sealed record class BinaryAdditionOperatorRuntimeSemantics(
        ILetCoercionRuntimeSemanticsProvider LetCoercionProvider, 
        IVerboseMessageBuilder FormatterService)
        : BinaryArithmeticOperatorRuntimeSemantics(LetCoercionProvider, FormatterService)
    {
        protected sealed override double EvaluateManagedNumericOp(double lhs, double rhs) => lhs + rhs;

        protected override DetermineOperatorEffectiveTypeResult DetermineArithmeticOperatorEffectiveType(
            ISymbolResolver resolver, 
            BinaryArithmeticOperatorSemanticContext context, 
            VBBinaryOperatorExpression<BinaryArithmeticOperatorSemanticContext, ArithmeticOperatorSemanticFlags> expression, 
            OperatorEvaluationFrame frame) => frame[OperandIndex.BinaryLeftOperand].TypeInfo switch
            {
                VBStringType when frame[OperandIndex.BinaryRightOperand].GetTargetType() is VBStringType
                    => DetermineOperatorEffectiveTypeResult.Success(VBStringType.TypeInfo),

                _ => DetermineOperatorEffectiveTypeResult.NotApplicable()
            };

        protected override RuntimeSemanticsEvaluationResult EvaluateExpressionResult(
            IVBExecutionContext runtime,
            SemanticContext<ArithmeticOperatorSemanticFlags> context,
            VBBinaryOperatorExpression<BinaryArithmeticOperatorSemanticContext, ArithmeticOperatorSemanticFlags> expression,
            OperatorEvaluationFrame frame)
        {
            switch (frame.EffectiveType)
            {
                case VBNumericType numericEffectiveType:
                    return EvaluateBinaryExpressionResult(numericEffectiveType, expression.ResultSymbol,
                        (VBNumericTypedValue)frame[OperandIndex.BinaryLeftOperand],
                        (VBNumericTypedValue)frame[OperandIndex.BinaryRightOperand]);

                case VBDateType dateEffectiveType:
                    return EvaluateBinaryExpressionResult(dateEffectiveType, expression.ResultSymbol,
                        (VBNumericTypedValue)frame[OperandIndex.BinaryLeftOperand],
                        (VBNumericTypedValue)frame[OperandIndex.BinaryRightOperand]);

                case VBStringType stringEffectiveType:
                    // The result is the right operand string concatenated to the left operand string
                    // IMPLEMENTATION NOTE: it isn't clear whether operands should be let-coerced left-to-right or right-to-left.
                    // However, neither coercion would actually throw us out of the evaluation pipeline in case of error.

                    var leftOperand = frame[OperandIndex.BinaryLeftOperand];
                    var leftCoercion = LetCoercionProvider.EvaluateLetCoercionSemantics(
                        resolver: runtime.Memory, 
                        expression: expression, 
                        frame: new(expression.SemanticId, expression.Symbol, OperandIndex.BinaryLeftOperand, leftOperand, 
                            VBTypedValueFactory.DescribeType(stringEffectiveType, leftOperand.ResolvedSymbol)));

                    var rightOperand = frame[OperandIndex.BinaryRightOperand];
                    var rightCoercion = LetCoercionProvider.EvaluateLetCoercionSemantics(
                        resolver: runtime.Memory,
                        expression: expression,
                        frame: new(expression.SemanticId, expression.Symbol, OperandIndex.BinaryRightOperand, rightOperand,
                            VBTypedValueFactory.DescribeType(stringEffectiveType, rightOperand.ResolvedSymbol)));

                    if (leftCoercion.IsSuccess && rightCoercion.IsSuccess)
                    {
                        return EvaluateBinaryExpressionResult(expression.ResultSymbol, (VBStringValue)leftCoercion.Result!,
                            (VBStringValue)rightCoercion.Result!);
                    }
                    else if (leftCoercion.ErrorInfo is not null || rightCoercion.ErrorInfo is not null)
                    {
                        return RuntimeSemanticsEvaluationResult.Error((leftCoercion.ErrorInfo ?? rightCoercion.ErrorInfo)!);
                    }
                    break;

                case VBNullType:
                    return EvaluateNullBinaryExpressionResult(expression.ResultSymbol);
            }

            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        private static RuntimeSemanticsEvaluationResult EvaluateBinaryExpressionResult(Symbol symbol, VBStringValue lhs, VBStringValue rhs)
            => RuntimeSemanticsEvaluationResult.Success(VBTypedValueFactory.CreateStringValue(symbol, $"{lhs.Value}{rhs.Value}"));
    }
}
