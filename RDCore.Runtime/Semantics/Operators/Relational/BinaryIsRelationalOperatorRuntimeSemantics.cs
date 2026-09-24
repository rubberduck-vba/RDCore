using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.Operators.Relational;

/// <summary>
/// MS-VBAL 5.6.9.7 Binary 'Is' Operator
/// </summary>
public record class BinaryIsRelationalOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionSemanticsProvider,
    IVerboseMessageBuilder FormatterService)
    : BinaryRelationalOperatorRuntimeSemantics(LetCoercionSemanticsProvider, FormatterService)
{
    protected override bool ComparisonOp(string lhs, string rhs, StringComparisonRules rules) => throw new NotSupportedException();
    protected override bool ComparisonOp<T>(T lhs, T rhs) => throw new NotSupportedException();

    protected override DetermineOperatorEffectiveTypeResult DetermineBinaryOperatorEffectiveType(
        ISymbolResolver resolver,
        BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags> context,
        ExpressionNode expression,
        OperatorEvaluationFrame frame) => DetermineOperatorEffectiveTypeResult.Success(VBBooleanType.TypeInfo);

    protected override RuntimeSemanticsEvaluationResult EvaluateBinaryOperatorExpressionResult(
        ISymbolResolver resolver,
        BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags> context, 
        ExpressionNode expression, 
        OperatorEvaluationFrame frame)
    {
        var lhs = frame[InputIndex.BinaryLeftOperand];
        var rhs = frame[InputIndex.BinaryRightOperand];

        // an operand is comparable by reference identity when it is currently bound to one — true of
        // VBObjectValue/VBNothingValue always, and of a VBVariantValue currently holding an object.
        if (lhs.RuntimeValue is not VBRuntimeValue<VBRuntimeObjectId> lhsReference)
        {
            return OnObjectRequired(expression, Exceptions.VBIsOp_ObjectRequired);
        }
        if (rhs.RuntimeValue is not VBRuntimeValue<VBRuntimeObjectId> rhsReference)
        {
            return OnObjectRequired(expression, Exceptions.VBIsOp_ObjectRequired);
        }

        return RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(lhsReference.StoredValue.Equals(rhsReference.StoredValue)));
    }
}
