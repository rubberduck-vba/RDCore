using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Meta;
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

/// <summary>
/// <strong>RD-VBAL 5.6.9.9</strong> Binary Let-Coercion operator (runtime semantics).
/// </summary>
/// <param name="resolver"></param>
/// <param name="context"></param>
/// <param name="frame"></param>
/// <remarks>
/// <list type="bullet">
/// <item>The <c>BinaryLeftOperand</c> of a <em>let-coercion operator</em> is the <em>source</em> <see cref="VBTypedValue"/>.</item>
/// <item>The <c>BinaryRightOperand</c> is a <see cref="VBTypeDescValue"/> describing the <em>coercion target</em> data type.</item>
/// <item>If the <em>target data type</em> is an <see cref="VBIntrinsicType"/> or a <see cref="VBClassType"/> or <see cref="VBUserDefinedType"/>,
/// the <strong>effective type</strong> of the <em>let-coercion operator</em> is the data type of its <em>coercion target</em> (<c>BinaryRightOperand</c>).</item>
/// </list>
/// ❌ If the <em>target data type</em> is any other <see cref="VBType"/>, the <em>effective type</em> is (or remains) <see cref="VBUnknownType"/> 
/// and evaluation yields a <strong><see cref="VBRuntimeErrorId.TypeMismatch"/></strong> error result.<br/>
/// ✔️ <strong>A successful <see cref="RuntimeSemanticsEvaluationResult"/> result encapsulates the <em>source</em> value let-coerced to the <em>target data type</em></strong>.<br/>
/// </remarks>
public record class BinaryLetCoerceOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider,
    IVerboseMessageBuilder FormatterService)
    : BinaryOperatorRuntimeSemantics<ConversionOperationSemanticContext, ConversionSemanticFlags>(LetCoercionProvider, FormatterService)
{
    protected override ISemanticContextContributor<ConversionOperationSemanticContext, ConversionSemanticFlags> Analyze(
        ISymbolResolver resolver,
        ConversionOperationSemanticContext coercionContext,
        ISemanticContextContributor<ConversionOperationSemanticContext, ConversionSemanticFlags> builder,
        VBOperatorExpression expression,
        OperatorAnalysisContext<ConversionSemanticFlags> analysisContext,
        params VBTypedValue[] operands) => builder.AddFlags(ConversionSemanticFlags.Explicit);

    protected override OperatorAnalysisContext<ConversionSemanticFlags> CreateAnalysisContext(
        SyntaxNode node, 
        DetermineOperatorEffectiveTypeResult determineOperatorEffectiveTypeResult, 
        LetCoercionAnalysisContext coercionResult, 
        RuntimeSemanticsEvaluationResult evaluationResult, 
        ConversionSemanticFlags semanticFlags) => new(node.Identity, determineOperatorEffectiveTypeResult, coercionResult, evaluationResult, semanticFlags);

    protected override DetermineOperatorEffectiveTypeResult DetermineBinaryOperatorEffectiveType(
        ISymbolResolver resolver,
        ConversionOperationSemanticContext context,
        VBBinaryOperatorExpressionNode expression,
        OperatorEvaluationFrame frame)
    {
        var targetType = frame[InputIndex.BinaryRightOperand].GetTargetType();
        return targetType is VBIntrinsicType or VBClassType or VBUserDefinedType
            ? DetermineOperatorEffectiveTypeResult.Success(targetType)
            : DetermineOperatorEffectiveTypeResult.Error(OnRuntimeError(VBRuntimeErrorId.TypeMismatch, expression,
                Exceptions.VBRuntimeTypeMismatch_OperationEffectiveType_Verbose.Replace("{$OPERANDS}", targetType.Name)));
    }

    protected override LetCoercionResult ValidateOperand(
        ISymbolResolver resolver,
        VBOperatorExpression expression,
        OperatorEvaluationFrame frame,
        InputIndex index)
        // This operator performs its own let-coercion inside EvaluateExpressionResult — the left
        // operand's source value, coerced to the right operand's requested target type. The base
        // pipeline's generic operand validation would pre-coerce the left operand toward
        // frame.EffectiveType (the RESOLVED target type) before this operator gets to run its own
        // logic — e.g. forcing a Date-effective-type operand to Double first, a rule meant for
        // arithmetic operators (MS-VBAL 5.6.9.3) that doesn't apply here, corrupting the very source
        // value this operator exists to coerce. Both operands pass through unchanged.
        => LetCoercionResult.Success(frame[index], []);

    protected override RuntimeSemanticsEvaluationResult EvaluateExpressionResult(
        ISymbolResolver resolver,
        ConversionOperationSemanticContext context, 
        VBBinaryOperatorExpressionNode expression, 
        OperatorEvaluationFrame frame)
    {
        var coercionResult = LetCoercionProvider.EvaluateLetCoercionSemantics(resolver, expression, 
            new(NodeId: expression.Identity, 
                OperandIndex: InputIndex.BinaryLeftOperand, 
                SourceValue: frame[InputIndex.BinaryLeftOperand], 
                DestinationTypeDesc: new VBTypeDescValue(frame[InputIndex.BinaryRightOperand].GetTargetType())));

        return coercionResult.IsSuccess 
            ? RuntimeSemanticsEvaluationResult.Success(coercionResult.Result!)
            : RuntimeSemanticsEvaluationResult.Error(coercionResult.ErrorInfo 
                ?? OnRuntimeError(VBRuntimeErrorId.TypeMismatch, expression, 
                    Exceptions.LetCoercionRuntimeErrorExceptionTypeMismatch_Verbose), coercionResult.Result);
    }
}
