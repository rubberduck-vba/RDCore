using RDCore.SDK.Model.AST.Expressions;
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

namespace RDCore.SDK.Semantics.Runtime.Operators.Semantics.Arithmetic;

/// <summary>
/// <strong>RD-VBAL 5.6.9.3.1.1 Unary '+' Operator</strong> (runtime semantics)
/// </summary>
/// <remarks>
/// 👉 This operator is omitted from the MS-VBAL specifications, but is clearly implemented in MS-VBA and therefore deemed to be implicitly specified.
/// The effect of the unary '+' operator is the application of all the let-coercion and null-exception rules to the resulting value.
/// </remarks>
public sealed record class UnaryPlusOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider,
    IVerboseMessageBuilder FormatterService) 
    : UnaryArithmeticOperatorRuntimeSemantics(LetCoercionProvider, FormatterService)
{
    protected override double EvaluateNumericOp(double operand) => 0 + operand;

    /// <summary>
    /// Explicitly defines the <em>effective type</em> runtime semantics of the unary '+' operator, 
    /// here inferred to match those of MS-VBAL 5.6.9.3.1 runtime semantics.
    /// </summary>
    protected override DetermineOperatorEffectiveTypeResult DetermineOperatorEffectiveType(
        ISymbolResolver resolver,
        VBOperatorExpression<UnaryArithmeticOperatorSemanticContext, ArithmeticOperatorSemanticFlags> expression,
        OperatorEvaluationFrame frame) => frame[InputIndex.UnaryOperand].TypeInfo switch
        {
            VBByteType => DetermineOperatorEffectiveTypeResult.Success(VBIntegerType.TypeInfo),

            _ => DetermineOperatorEffectiveTypeResult.NotApplicable()
        };

    protected override RuntimeSemanticsEvaluationResult EvaluateExpressionResult(
        IVBExecutionContext runtime,
        SemanticContext<ArithmeticOperatorSemanticFlags> context,
        VBOperatorExpression<UnaryArithmeticOperatorSemanticContext, ArithmeticOperatorSemanticFlags> expression,
        OperatorEvaluationFrame frame) => frame.EffectiveType switch
        {
            VBNumericType numericEffectiveType when frame[InputIndex.UnaryOperand] is VBNumericTypedValue numericOperand 
                => RuntimeSemanticsEvaluationResult.Success(EvaluateRuntimeSemantics(numericEffectiveType, expression.ResultSymbol, numericOperand)!),
            
            // per specifications a VBDateValue operand was let-coerced into a VBDoubleValue during validation stage:
            VBDateType dateEffectiveType when frame[InputIndex.UnaryOperand] is VBNumericTypedValue numericOperand 
                => RuntimeSemanticsEvaluationResult.Success(EvaluateRuntimeSemantics(dateEffectiveType, expression.ResultSymbol, numericOperand)!),

            VBNullType nullEffectiveType when frame[InputIndex.UnaryOperand] is VBNullValue
                => RuntimeSemanticsEvaluationResult.Success(VBTypedValueFactory.CreateValue(nullEffectiveType, expression.ResultSymbol)!),

            _ => RuntimeSemanticsEvaluationResult.InternalError(),
        };
}
