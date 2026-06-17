using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
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
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.Operators.Arithmetic;

/// <summary>
/// MS-VBAL 5.6.9.3.1 Unary '-' Operator (runtime semantics)
/// </summary>
public sealed record class UnaryNegationOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider, 
    IVerboseMessageBuilder FormatterService) 
    : UnaryArithmeticOperatorRuntimeSemantics(LetCoercionProvider, FormatterService)
{
    protected override DetermineOperatorEffectiveTypeResult DetermineOperatorEffectiveType(
        ISymbolResolver resolver,
        VBOperatorExpression<UnaryArithmeticOperatorSemanticContext, ArithmeticOperatorSemanticFlags> expression,
        OperatorEvaluationFrame frame) => frame[InputIndex.UnaryOperand].TypeInfo switch
        {
            VBByteType  => DetermineOperatorEffectiveTypeResult.Success(VBIntegerType.TypeInfo),

            _ => DetermineOperatorEffectiveTypeResult.NotApplicable()
        };

    protected override double EvaluateNumericOp(double operand) => 0 - operand;

    protected override RuntimeSemanticsEvaluationResult EvaluateExpressionResult(
        IVBExecutionContext runtime,
        SemanticContext<ArithmeticOperatorSemanticFlags> context,
        VBOperatorExpression<UnaryArithmeticOperatorSemanticContext, ArithmeticOperatorSemanticFlags> expression,
        OperatorEvaluationFrame frame) => frame.EffectiveType switch
        {
            /* 
             * NOTE ----------------------------------------------------------------------------------------/
             * 
                MS-VBAL 5.6.9.3.1 explicitly enumerates each of the following data types:
            
                    VBByteType or 
                    VBIntegerType or 
                    VBLongType or 
                    VBLongLongType or 
                    VBSingleType or 
                    VBDoubleType or 
                    VBCurrencyType or 
                    VBDecimalType
                
                The very same MS-VBAL specifications also explicitly classify every last one of these types 
                as "numeric data types" in many places, and the RDCore.SDK language core has an abstraction 
                for it that simplifies how this reads a bit. 
                If RDCore.SDK is extended with new numeric types not specified in MS-VBAL, 
                leaving this semantic switch with an explicit enumeration of numeric data types 
                would inevitably break the numeric operation for the extended numeric type.
        
                MS-VBAL does explicitly specify "all numeric data types" or "any numeric data type" elsewhere, 
                so implementing this way... fixes a possibly unintended redundancy in the specification,
                that would have been a useless language extensibility blocker.
        
                Any "when" clauses here about the operand, are semantically implicit in the specifications,
                because the effective type is always determined from the types of the operands.
            */

            VBNumericType numericEffectiveType when frame[InputIndex.UnaryOperand] is VBNumericTypedValue numericOperand =>
                RuntimeSemanticsEvaluationResult.Success(EvaluateRuntimeSemantics(numericEffectiveType, expression.ResultSymbol, numericOperand)!),

            // per specifications a VBDateValue operand was let-coerced into a VBDoubleValue by this point:
            VBDateType dateEffectiveType when frame[InputIndex.UnaryOperand] is VBNumericTypedValue numericOperand =>
                RuntimeSemanticsEvaluationResult.Success(EvaluateRuntimeSemantics(dateEffectiveType, expression.ResultSymbol, numericOperand)!),

            VBNullType nullEffectiveType when frame[InputIndex.UnaryOperand] is VBNullValue
                => RuntimeSemanticsEvaluationResult.Success(VBTypedValueFactory.CreateValue(nullEffectiveType, expression.ResultSymbol)!),

            _ => RuntimeSemanticsEvaluationResult.InternalError(),
        };
}
