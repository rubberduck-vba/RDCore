using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Plans;
using RDCore.SDK;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
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
        ExpressionNode expression,
        OperatorAnalysisContext<ConversionSemanticFlags> analysisContext,
        params VBTypedValue[] operands)
        // the facts of an assignment are those of the coercion of its source to the declared type of its target: the operand
        // and conversion flags, and none at all when the source already is of that type and nothing is converted.
        => builder.AddFlags(coercionContext.Flags);

    protected override LetCoercionAnalysisContext AnalyzeValidateOperand(
        ISymbolResolver resolver,
        ILetCoercionSemanticContextBuilder builder,
        ExpressionNode expression,
        OperatorEvaluationFrame frame,
        InputIndex operandIndex)
    {
        var operand = frame[operandIndex];
        return operandIndex == InputIndex.BinaryRightOperand && !frame.EffectiveType.Equals(operand.TypeInfo)
            ? AnalyzeOperandCoercion(resolver, builder, expression, operand, operandIndex, frame.EffectiveType)
            : new LetCoercionAnalysisContext(frame.NodeId, LetCoercionResult.Success(operand, []));
    }

    protected override RuntimeSemanticsEvaluationResult EvaluateForAnalysis(
        ISymbolResolver resolver,
        ConversionOperationSemanticContext context,
        ExpressionNode expression,
        OperatorEvaluationFrame frame)
    {
        var effectiveTypeResult = DetermineOperatorEffectiveType(resolver, context, expression, frame);
        if (effectiveTypeResult.Result is null)
        {
            return RuntimeSemanticsEvaluationResult.Error(effectiveTypeResult.ErrorInfo
                ?? OnRuntimeError(VBRuntimeErrorId.TypeMismatch, expression, Exceptions.LetCoercionRuntimeErrorExceptionTypeMismatch_Verbose));
        }

        var coercionResult = CoerceSource(resolver, expression, frame with { EffectiveType = effectiveTypeResult.Result });
        return coercionResult.IsSuccess
            ? RuntimeSemanticsEvaluationResult.Success(coercionResult.Result!)
            : RuntimeSemanticsEvaluationResult.Error(coercionResult.ErrorInfo
                ?? OnRuntimeError(VBRuntimeErrorId.TypeMismatch, expression, Exceptions.LetCoercionRuntimeErrorExceptionTypeMismatch_Verbose));
    }

    private LetCoercionResult CoerceSource(ISymbolResolver resolver, ExpressionNode expression, OperatorEvaluationFrame frame)
        => LetCoercionProvider.EvaluateLetCoercionSemantics(resolver, expression,
            new(NodeId: expression.Identity,
                OperandIndex: InputIndex.BinaryRightOperand,
                SourceValue: frame[InputIndex.BinaryRightOperand],
                DestinationTypeDesc: new VBTypeDescValue(frame.EffectiveType),
                Site: frame.Site));

    protected override OperatorAnalysisContext<ConversionSemanticFlags> CreateAnalysisContext(
        SyntaxNode node,
        DetermineOperatorEffectiveTypeResult determineOperatorEffectiveTypeResult,
        LetCoercionAnalysisContext coercionResult,
        RuntimeSemanticsEvaluationResult evaluationResult,
        ConversionSemanticFlags semanticFlags) => new(node.Identity, determineOperatorEffectiveTypeResult, coercionResult, evaluationResult, semanticFlags);

    protected override DetermineOperatorEffectiveTypeResult DetermineBinaryOperatorEffectiveType(
        ISymbolResolver resolver,
        ConversionOperationSemanticContext context,
        ExpressionNode expression,
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
        ExpressionNode expression,
        OperatorEvaluationFrame frame,
        InputIndex index)
        // This operator performs its own let-coercion inside EvaluateBinaryOperatorExpressionResult — the source
        // (right operand) coerced to the target's (left operand) declared type. The base pipeline's
        // generic operand validation would instead try to pre-coerce BOTH operands toward
        // frame.EffectiveType, which would corrupt the left operand: it's a VBSymbolDescValue target
        // descriptor, not a value to coerce at all. Same reasoning as
        // BinaryLetCoerceOperatorRuntimeSemantics.ValidateOperand.
        => LetCoercionResult.Success(frame[index], []);

    protected override RuntimeSemanticsEvaluationResult EvaluateBinaryOperatorExpressionResult(
        ISymbolResolver resolver,
        ConversionOperationSemanticContext context,
        ExpressionNode expression,
        OperatorEvaluationFrame frame)
    {
        var target = ((VBSymbolDescValue)frame[InputIndex.BinaryLeftOperand]).Symbol;

        var coercionResult = CoerceSource(resolver, expression, frame);

        if (!coercionResult.IsSuccess)
        {
            return RuntimeSemanticsEvaluationResult.Error(coercionResult.ErrorInfo
                ?? OnRuntimeError(VBRuntimeErrorId.TypeMismatch, expression, Exceptions.LetCoercionRuntimeErrorExceptionTypeMismatch_Verbose));
        }

        return Store(resolver, target, coercionResult.Result!);
    }

    /// <summary>
    /// Let-assigns a value to a variable by the plan of its let-coercion (<see cref="PlanExecutor.TryConvert"/>), if the value and the declared type of the
    /// variable are ones the plan can be executed for.
    /// </summary>
    /// <param name="session">The session the assignment is evaluated in.</param>
    /// <param name="target">The variable assigned: a <see cref="VBSymbolDescValue"/>.</param>
    /// <param name="source">The value assigned.</param>
    /// <returns>The value assigned, as it was let-coerced and stored - or <see langword="null"/> when the assignment is to be evaluated the way it always
    /// is, which is also how a coercion that fails raises its error.</returns>
    internal RuntimeSemanticsEvaluationResult? TryAssignPlanned(IRuntimeSession session, VBTypedValue target, VBTypedValue source)
        => target is VBSymbolDescValue { Symbol: var symbol }
            && target.GetTargetType() is var declared and not VBUnknownType
            && PlanExecutor.TryConvert(source, declared, out var coerced)
                ? Store(session.Symbols.Resolver, symbol, coerced)
                : null;

    private static RuntimeSemanticsEvaluationResult Store(ISymbolResolver resolver, Symbol target, VBTypedValue value)
    {
        var handle = resolver.GetValue(target);
        if (!handle.BindingCapabilities.HasFlag(BindingCapabilities.SetValue))
        {
            // MS-VBAL static semantics should already have rejected an assignment to a read-only
            // target (a Const, chiefly) at compile time — reaching here means that check was skipped.
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        // an array or a record is identified by where it is, not by a value its own binding could hold: what a variable holds of one
        // is the value boxed, as it is wherever else one is stored (SymbolAddressTable.FreshBinding). A coerced array - a fixed-size
        // array let-coerced to a resizable one - and a copied record have no binding of their own to read them from.
        handle.Store(resolver, value is VBArrayValue or VBUserDefinedTypeValue
            ? SymbolAddressTable.BoxedValue(value)
            : value.RuntimeValue, !value.IsIndeterminate);
        return RuntimeSemanticsEvaluationResult.Success(value);
    }
}
