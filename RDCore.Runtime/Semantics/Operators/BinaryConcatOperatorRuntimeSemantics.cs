using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Flags;
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

    protected override ISemanticContextContributor<ConcatOperationSemanticContext, ConcatOperationSemanticFlags> Analyze(
        ISymbolResolver resolver, 
        ConversionOperationSemanticContext coercionContext, 
        ISemanticContextContributor<ConcatOperationSemanticContext, ConcatOperationSemanticFlags> builder, 
        ExpressionNode expression, 
        OperatorAnalysisContext<ConcatOperationSemanticFlags> analysisContext, 
        params VBTypedValue[] operands)
    {
        if(operands.OfType<VBNullValue>().Any())
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

    protected override DetermineOperatorEffectiveTypeResult DetermineBinaryOperatorEffectiveType(
        ISymbolResolver resolver,
        ConcatOperationSemanticContext context,
        ExpressionNode expression,
        OperatorEvaluationFrame frame)
    {
        var lhsOperand = frame[InputIndex.BinaryLeftOperand];
        var rhsOperand = frame[InputIndex.BinaryRightOperand];
        var lhs = lhsOperand.TypeInfo;
        var rhs = rhsOperand.TypeInfo;
        return lhs switch
        {
            VBNumericType or VBStringType or VBDateType or VBNullType or VBEmptyType
                when rhs is VBNumericType or VBStringType or VBDateType or VBEmptyType
                    => DetermineOperatorEffectiveTypeResult.Success(VBStringType.TypeInfo),

            VBNumericType or VBStringType or VBDateType or VBEmptyType
                when rhs is VBNumericType or VBStringType or VBDateType or VBNullType or VBEmptyType
                    => DetermineOperatorEffectiveTypeResult.Success(VBStringType.TypeInfo),

            // A Byte() array's own TypeInfo always collapses to the generic VBArrayType (VBArrayValue's
            // base constructor tags every array with it, regardless of ItemType), so the pair can't be
            // recognized through lhs/rhs above - it's checked against the actual operand values instead.
            _ when IsByteArray(lhsOperand) && IsByteArray(rhsOperand)
                => DetermineOperatorEffectiveTypeResult.Success(VBStringType.TypeInfo),

            VBNullType
                when rhs is VBNullType
                    => DetermineOperatorEffectiveTypeResult.Success(VBNullType.TypeInfo),

            _ => DetermineOperatorEffectiveTypeResult.Error(OnRuntimeError(VBRuntimeErrorId.TypeMismatch, expression,
                Exceptions.VBRuntimeTypeMismatch_OperationEffectiveType_Verbose.Replace("{$OPERANDS}", string.Join(", ", [lhs.Name, rhs.Name]))))
        };
    }

    private static bool IsByteArray(VBTypedValue value) => value is VBArrayValue { ItemType: VBByteType };

    protected override RuntimeSemanticsEvaluationResult EvaluateBinaryOperatorExpressionResult(
        ISymbolResolver resolver,
        ConcatOperationSemanticContext context,
        ExpressionNode expression,
        OperatorEvaluationFrame frame) =>
        frame.EffectiveType switch
        {
            VBStringType => RuntimeSemanticsEvaluationResult.Success(
                new VBStringValue($"{StringOperand(frame[InputIndex.BinaryLeftOperand])}{StringOperand(frame[InputIndex.BinaryRightOperand])}")),

            VBNullType => EvaluateNullBinaryExpressionResult(),

            _ => RuntimeSemanticsEvaluationResult.InternalError(),
        };

    /// <summary>
    /// The pipeline exempts <c>Null</c> operands from let-coercion regardless of the operator's
    /// effective type (RD-VBAL 5.6.9.2), so a String-effective-type evaluation can still see a
    /// surviving <see cref="VBNullValue"/> operand — e.g. <c>Null &amp; "x"</c>, whose value type is
    /// String per MS-VBAL §5.6.9.4's table (only <c>Null &amp; Null</c> resolves to <c>Null</c>). Such
    /// an operand contributes an empty string rather than being cast as a <see cref="VBStringValue"/>.
    /// </summary>
    private static string StringOperand(VBTypedValue operand) => operand is VBStringValue value ? value.Value! : string.Empty;
}
