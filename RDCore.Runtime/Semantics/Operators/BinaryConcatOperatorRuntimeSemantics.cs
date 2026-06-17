using RDCore.Runtime.Execution.Frames;
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
        BoundNode node,
        DetermineOperatorEffectiveTypeResult determineOperatorEffectiveTypeResult,
        LetCoercionAnalysisContext coercionResult,
        RuntimeSemanticsEvaluationResult evaluationResult,
        ConcatOperationSemanticFlags semanticFlags) 
        => new(node.SemanticId, determineOperatorEffectiveTypeResult, coercionResult, evaluationResult, semanticFlags);

    protected override ISemanticContextContributor<ConcatOperationSemanticContext, ConcatOperationSemanticFlags> Analyze(
        ISymbolResolver resolver, 
        ConversionOperationSemanticContext coercionContext, 
        ISemanticContextContributor<ConcatOperationSemanticContext, ConcatOperationSemanticFlags> builder, 
        VBOperatorExpression<ConcatOperationSemanticContext, ConcatOperationSemanticFlags> expression, 
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
        if (operands.OfType<VBResizableByteArrayValue>().Any())
        {
            builder.AddFlags(ConcatOperationSemanticFlags.HasByteArrayOperand);
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
        SemanticContext<ConcatOperationSemanticFlags> context, 
        VBBinaryOperatorExpression<ConcatOperationSemanticContext, ConcatOperationSemanticFlags> expression, 
        OperatorEvaluationFrame frame)
    {
        var rhs = frame[InputIndex.BinaryRightOperand].TypeInfo;
        return frame[InputIndex.BinaryLeftOperand].TypeInfo switch
        {
            VBNumericType or VBStringType or VBDateType or VBNullType or VBEmptyType
                when rhs is VBNumericType or VBStringType or VBDateType or VBEmptyType
                    => DetermineOperatorEffectiveTypeResult.Success(VBStringType.TypeInfo),

            VBNumericType or VBStringType or VBDateType or VBEmptyType
                when rhs is VBNumericType or VBStringType or VBDateType or VBNullType or VBEmptyType
                    => DetermineOperatorEffectiveTypeResult.Success(VBStringType.TypeInfo),

            VBResizableByteArrayType
                when rhs is VBResizableByteArrayType
                    => DetermineOperatorEffectiveTypeResult.Success(VBStringType.TypeInfo),

            VBNullType
                when rhs is VBNullType
                    => DetermineOperatorEffectiveTypeResult.Success(VBNullType.TypeInfo),

            _ => DetermineOperatorEffectiveTypeResult.NotApplicable()
        };
    }

    protected override RuntimeSemanticsEvaluationResult EvaluateExpressionResult(
        IVBExecutionContext runtime, 
        SemanticContext<ConcatOperationSemanticFlags> context, 
        VBBinaryOperatorExpression<ConcatOperationSemanticContext, ConcatOperationSemanticFlags> expression, 
        OperatorEvaluationFrame frame) =>
        frame.EffectiveType switch
        {
            VBStringType => RuntimeSemanticsEvaluationResult.Success(
                VBTypedValueFactory.CreateStringValue(expression.ResultSymbol,
                    $"{((VBStringValue)frame[InputIndex.BinaryLeftOperand]).Value}{((VBStringValue)frame[InputIndex.BinaryRightOperand]).Value}")),

            VBNullType => EvaluateNullBinaryExpressionResult(expression.ResultSymbol),

            _ => RuntimeSemanticsEvaluationResult.InternalError(),
        };
}
