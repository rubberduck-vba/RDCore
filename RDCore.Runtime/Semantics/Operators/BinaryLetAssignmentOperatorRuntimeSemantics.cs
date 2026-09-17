using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
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
/// <strong>MS-VBAL 5.4.3.8</strong> Let-assignment (runtime semantics) — the reserved synthetic binary
/// operator <see cref="RDCore.SDK.Model.Symbols.Operators.OperatorSymbolNames.BinaryAssignmentValueOp"/>
/// ("__let_op"). The source (right operand) is Let-coerced to the target's (left operand, a
/// <see cref="VBSymbolDescValue"/>) declared type, then written through the target's current
/// <see cref="IBindingHandle"/>.
/// </summary>
/// <remarks>
/// Scoped to a target that already resolves to a <see cref="Symbol"/> with a real runtime binding —
/// an ordinary variable, module-, global- or local-scoped. A member-access or indexed target needs to
/// invoke a <c>Property Let</c> procedure or address an array cell, which needs the call stack's
/// actual invocation machinery — that's separate, larger follow-up work. <c>Set</c>-assignment
/// (<strong>MS-VBAL 5.4.3.9</strong>) is a distinct concern with its own set-coercion rules, not this
/// class either — see the reserved <c>BinaryAssignmentReferenceOp</c> ("__set_op").
/// </remarks>
public sealed record class BinaryLetAssignmentOperatorRuntimeSemantics(
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
        params VBTypedValue[] operands)
        // TODO: assignment-specific semantic flags, once a caller (an analyzer) actually needs them.
        => builder;

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
        var targetType = frame[InputIndex.BinaryLeftOperand].GetTargetType();
        return targetType is VBUnknownType
            ? DetermineOperatorEffectiveTypeResult.Error(OnRuntimeError(VBRuntimeErrorId.TypeMismatch, expression,
                Exceptions.VBRuntimeTypeMismatch_OperationEffectiveType_Verbose.Replace("{$OPERANDS}", targetType.Name)))
            : DetermineOperatorEffectiveTypeResult.Success(targetType);
    }

    protected override LetCoercionResult ValidateOperand(
        ISymbolResolver resolver,
        VBOperatorExpression expression,
        OperatorEvaluationFrame frame,
        InputIndex index)
        // This operator performs its own let-coercion inside EvaluateExpressionResult — the source
        // (right operand) coerced to the target's (left operand) declared type. The base pipeline's
        // generic operand validation would instead try to pre-coerce BOTH operands toward
        // frame.EffectiveType, which would corrupt the left operand: it's a VBSymbolDescValue target
        // descriptor, not a value to coerce at all. Same reasoning as
        // BinaryLetCoerceOperatorRuntimeSemantics.ValidateOperand.
        => LetCoercionResult.Success(frame[index], []);

    protected override RuntimeSemanticsEvaluationResult EvaluateExpressionResult(
        ISymbolResolver resolver,
        ConversionOperationSemanticContext context,
        VBBinaryOperatorExpressionNode expression,
        OperatorEvaluationFrame frame)
    {
        var target = ((VBSymbolDescValue)frame[InputIndex.BinaryLeftOperand]).Symbol;
        var source = frame[InputIndex.BinaryRightOperand];

        var coercionResult = LetCoercionProvider.EvaluateLetCoercionSemantics(resolver, expression,
            new(NodeId: expression.Identity,
                OperandIndex: InputIndex.BinaryRightOperand,
                SourceValue: source,
                DestinationTypeDesc: new VBTypeDescValue(frame.EffectiveType)));

        if (!coercionResult.IsSuccess)
        {
            return RuntimeSemanticsEvaluationResult.Error(coercionResult.ErrorInfo
                ?? OnRuntimeError(VBRuntimeErrorId.TypeMismatch, expression, Exceptions.LetCoercionRuntimeErrorExceptionTypeMismatch_Verbose));
        }

        var handle = resolver.GetValue(target);
        if (!handle.BindingCapabilities.HasFlag(BindingCapabilities.SetValue))
        {
            // MS-VBAL static semantics should already have rejected an assignment to a read-only
            // target (a Const, chiefly) at compile time — reaching here means that check was skipped.
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        handle.SetValue(resolver, coercionResult.Result!.RuntimeValue);
        return RuntimeSemanticsEvaluationResult.Success(coercionResult.Result!);
    }
}
