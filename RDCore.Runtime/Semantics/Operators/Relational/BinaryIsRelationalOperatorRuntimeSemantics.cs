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
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Builders;
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

    // the operands are object references compared as they are (MS-VBAL 5.6.9.7): the Boolean effective type of the result is
    // not a type to let-coerce them to - which would coerce an object to a Boolean, and fail.
    protected override LetCoercionResult ValidateOperand(
        ISymbolResolver resolver,
        ExpressionNode expression,
        OperatorEvaluationFrame frame,
        InputIndex index)
        => LetCoercionResult.Success(frame[index], []);

    protected override LetCoercionAnalysisContext AnalyzeValidateOperand(
        ISymbolResolver resolver,
        ILetCoercionSemanticContextBuilder builder,
        ExpressionNode expression,
        OperatorEvaluationFrame frame,
        InputIndex operandIndex)
        => new(frame.NodeId, LetCoercionResult.Success(frame[operandIndex], []));

    protected override RuntimeSemanticsEvaluationResult EvaluateBinaryOperatorExpressionResult(
        ISymbolResolver resolver,
        BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags> context, 
        ExpressionNode expression, 
        OperatorEvaluationFrame frame)
    {
        // MS-VBAL 5.6.9.7: an operand may be a Variant, and a Variant is compared by the object it holds.
        var lhs = Held(frame[InputIndex.BinaryLeftOperand]);
        var rhs = Held(frame[InputIndex.BinaryRightOperand]);

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

    private static SDK.Model.Values.Abstract.VBTypedValue Held(SDK.Model.Values.Abstract.VBTypedValue operand)
    {
        while (operand is VBVariantValue { TypedValue: { } held })
        {
            operand = held;
        }

        return operand;
    }
}
