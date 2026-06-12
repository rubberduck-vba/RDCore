using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Abstract.Operators;
using RDCore.SDK.Semantics.Runtime.LetCoercion;
using RDCore.SDK.Semantics.Runtime.Operators.Context;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.SDK.Semantics.Runtime.Operators.Semantics.Relational
{
    /// <summary>
    /// MS-VBAL 5.6.9.7 Binary 'Is' Operator
    /// </summary>
    public record class BinaryIsRelationalOperatorRuntimeSemantics(
        ILetCoercionRuntimeSemanticsProvider LetCoercionSemanticsProvider,
        IVerboseMessageBuilder FormatterService)
        : BinaryRelationalOperatorRuntimeSemantics(LetCoercionSemanticsProvider, FormatterService)
    {
        protected override bool ComparisonOp(string lhs, string rhs, StringComparison comparison) => throw new NotSupportedException();
        protected override bool ComparisonOp(double lhs, double rhs) => throw new NotSupportedException();

        protected override DetermineOperatorEffectiveTypeResult DetermineBinaryOperatorEffectiveType(
            ISymbolResolver resolver,
            SemanticContext<ComparisonOperatorSemanticFlags> context,
            VBBinaryOperatorExpression<BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags>, ComparisonOperatorSemanticFlags> expression,
            OperatorEvaluationFrame frame) => DetermineOperatorEffectiveTypeResult.Success(VBBooleanType.TypeInfo);

        protected override RuntimeSemanticsEvaluationResult EvaluateExpressionResult(
            IVBExecutionContext runtime, 
            SemanticContext<ComparisonOperatorSemanticFlags> context, 
            VBBinaryOperatorExpression<BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags>, ComparisonOperatorSemanticFlags> expression, 
            OperatorEvaluationFrame frame)
        {
            var lhs = frame[OperandIndex.BinaryLeftOperand];
            var rhs = frame[OperandIndex.BinaryRightOperand];

            // just to read like MS-VBAL: VBNothingValue is a VBObjectValue (similar w/ string & fixedString)
            if (lhs is not VBObjectValue and not VBNothingValue)
            {
                OnObjectRequired(expression, Exceptions.VBIsOp_ObjectRequired);
            }
            if (rhs is not VBObjectValue and not VBNothingValue)
            {
                OnObjectRequired(expression, Exceptions.VBIsOp_ObjectRequired);
            }

            if (lhs.ResolvedSymbol != null && lhs is VBObjectValue or VBVariantValue &&
                rhs.ResolvedSymbol != null && rhs is VBObjectValue or VBVariantValue)
            {
                return RuntimeSemanticsEvaluationResult.Success(
                    VBTypedValueFactory.CreateBooleanValue(expression.ResultSymbol, lhs.RawAddress == rhs.RawAddress));
            }

            return RuntimeSemanticsEvaluationResult.InternalError();
        }
    }
}
