using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.Abstract;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Plans;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.Operators;

public record class BinaryConcatOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider,
    IVerboseMessageBuilder FormatterService)
    : BinaryOperatorRuntimeSemantics<ConcatOperationSemanticContext, ConcatOperationSemanticFlags>(LetCoercionProvider, FormatterService)
{
    protected override OperatorAnalysisContext<ConcatOperationSemanticFlags> CreateAnalysisContext(
        SyntaxNode node,
        DetermineOperatorEffectiveTypeResult determineOperatorEffectiveTypeResult,
        LetCoercionAnalysisContext coercionResult,
        RuntimeSemanticsEvaluationResult evaluationResult,
        ConcatOperationSemanticFlags semanticFlags)
        => new(node.Identity, determineOperatorEffectiveTypeResult, coercionResult, evaluationResult, semanticFlags);

    protected override OperatorFact? CreateFact(
        string token, ExpressionNode expression, VBType? effectiveType, ConcatOperationSemanticFlags flags,
        StringComparisonRules comparison, bool isValueKnown, VBErrorInfo? error)
        => new ConcatOperatorFact(expression.Identity, expression.Location, token, effectiveType, isValueKnown, error, flags);

    protected override ISemanticContextContributor<ConcatOperationSemanticContext, ConcatOperationSemanticFlags> Analyze(
        ISymbolResolver resolver, 
        ConversionOperationSemanticContext coercionContext, 
        ISemanticContextContributor<ConcatOperationSemanticContext, ConcatOperationSemanticFlags> builder, 
        ExpressionNode expression, 
        OperatorAnalysisContext<ConcatOperationSemanticFlags> analysisContext, 
        params VBTypedValue[] operands)
    {
        if (operands.Any(NullOperand.Is))
        {
            builder.AddFlags(ConcatOperationSemanticFlags.HasNullOperand);
        }
        if (operands.OfType<VBNumericTypedValue>().Any())
        {
            builder.AddFlags(ConcatOperationSemanticFlags.HasNumericOperand);
        }
        // the same Byte() the effective type recognizes: any array of Byte elements.
        if (operands.Any(IsByteArray))
        {
            builder.AddFlags(ConcatOperationSemanticFlags.HasByteArrayOperand);
        }
        // the operands no concatenation is defined for (MS-VBAL 5.6.9.4): each is a type mismatch.
        if (operands.OfType<VBArrayValue>().Any(array => array.ItemType is not VBByteType))
        {
            builder.AddFlags(ConcatOperationSemanticFlags.HasNonByteArrayOperand);
        }
        if (operands.OfType<VBUserDefinedTypeValue>().Any())
        {
            builder.AddFlags(ConcatOperationSemanticFlags.HasUserDefinedTypeOperand);
        }
        if (operands.OfType<VBErrorValue>().Any())
        {
            builder.AddFlags(ConcatOperationSemanticFlags.HasErrorOperand);
        }

        return builder.AddFlags(analysisContext.EffectiveTypeResult.Result switch
        {
            VBStringType => ConcatOperationSemanticFlags.StringEffectiveType,
            VBNullType => ConcatOperationSemanticFlags.NullEffectiveType,
            _ => 0
        });
    }

    /// <remarks>
    /// The effective type is the operation's plan (<see cref="OperationPlanner"/>), made once for each pair of value types and kept.
    /// </remarks>
    protected override DetermineOperatorEffectiveTypeResult DetermineBinaryOperatorEffectiveType(
        ISymbolResolver resolver,
        ConcatOperationSemanticContext context,
        ExpressionNode expression,
        OperatorEvaluationFrame frame)
        => EffectiveTypeOf(OperationPlanner.ForValues(
            Tokens.ConcatOp, ValueTypeOf(frame[InputIndex.BinaryLeftOperand]), ValueTypeOf(frame[InputIndex.BinaryRightOperand])), expression);

    // a Byte() array's own TypeInfo always collapses to the generic VBArrayType (VBArrayValue's base constructor tags every array with it, regardless of
    // ItemType): the type of its value is told by its elements.
    private static VBType ValueTypeOf(VBTypedValue operand) => IsByteArray(operand) ? VBResizableByteArrayType.TypeInfo : operand.TypeInfo;

    // a Variant holding a Byte array reports its own TypeInfo as the array's, but stays a VBVariantValue
    // instance - unwrap it (recursively) the same way every other Variant-holding-a-complex-value site does.
    private static bool IsByteArray(VBTypedValue value) => value switch
    {
        VBVariantValue { TypedValue: var wrapped } => IsByteArray(wrapped),
        VBArrayValue { ItemType: VBByteType } => true,
        _ => false,
    };

    protected override RuntimeSemanticsEvaluationResult EvaluateBinaryOperatorExpressionResult(
        ISymbolResolver resolver,
        ConcatOperationSemanticContext context,
        ExpressionNode expression,
        OperatorEvaluationFrame frame) =>
        frame.EffectiveType switch
        {
            // MS-VBAL 5.6.9.4 appends the right operand's data to the left's, which is all of their bytes: a trailing
            // byte on the left pairs with the first byte on the right.
            VBStringType => RuntimeSemanticsEvaluationResult.Success(
                VBStringValue.Concat(StringOperand(frame[InputIndex.BinaryLeftOperand]), StringOperand(frame[InputIndex.BinaryRightOperand]))),

            VBNullType => EvaluateNullBinaryExpressionResult(),

            _ => RuntimeSemanticsEvaluationResult.InternalError(),
        };

    /// <summary>
    /// The pipeline exempts <c>Null</c> operands from let-coercion regardless of the operator's
    /// effective type (RD-VBAL 5.6.9.2), so a String-effective-type evaluation can still see a
    /// surviving <see cref="VBNullValue"/> operand — e.g. <c>Null &amp; "x"</c>, whose value type is
    /// String per MS-VBAL §5.6.9.4's table (only <c>Null &amp; Null</c> resolves to <c>Null</c>). Such
    /// an operand contributes the zero-length string, whose data is no bytes at all, rather than being cast
    /// as a <see cref="VBStringValue"/>.
    /// </summary>
    private static VBStringValue StringOperand(VBTypedValue operand) => operand as VBStringValue ?? VBStringValue.ZeroLengthString;
}
