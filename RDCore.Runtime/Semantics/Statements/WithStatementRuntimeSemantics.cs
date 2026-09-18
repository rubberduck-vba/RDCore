using RDCore.Runtime.Semantics.Abstract;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Runtime.Semantics.Statements;

/// <summary>
/// <strong>MS-VBAL 5.4.2.21</strong> With Statement (runtime semantics). When the evaluated target's
/// value type is a class, it is Set-assigned to an anonymous <c>With</c> block variable
/// (<strong>MS-VBAL 5.5.2</strong> Set-coercion).
/// </summary>
/// <remarks>
/// Scoped to producing the value that would be stored in that anonymous variable — it does not itself
/// allocate or track a real variable slot (no statement/procedure execution driver exists yet to bind
/// with-relative member access against one; that is separate follow-up work, the same shape as
/// <see cref="RDCore.Runtime.Semantics.Operators.BinaryLetAssignmentOperatorRuntimeSemantics"/>'s own
/// scoping note). It also does not raise MS-VBAL runtime error 91 (Object variable or With block
/// variable not set) for a <c>Nothing</c> target: Set-coercion's own Nothing-passthrough case is a
/// success (MS-VBAL 5.5.2.2.1) — error 91 is a member-access dereference concern, not part of this
/// statement's own runtime semantics, and member access isn't modeled yet either.
/// <para>
/// A <c>With</c> target whose value type is a UDT is Let-assigned instead, per the same MS-VBAL
/// section — a distinct, deliberately deferred gap here:
/// <see cref="RDCore.SDK.Runtime.Abstract.ILetCoercionRuntimeSemanticsProvider"/>'s entry point requires
/// a <c>VBOperatorExpression</c>, which a <c>With</c> statement's target expression is not guaranteed
/// to be.
/// </para>
/// </remarks>
public sealed record class WithStatementRuntimeSemantics(ISetCoercionRuntimeSemantics SetCoercionSemantics)
    : StatementRuntimeSemantics<WithStatementSemanticContext, WithStatementSemanticFlags>
{
    public override ISemanticFlagsAccumulator<WithStatementSemanticFlags> Analyze(
        IRuntimeSession session,
        ConversionOperationSemanticContext conversionContext,
        ISemanticFlagsAccumulator<WithStatementSemanticFlags> builder,
        SyntaxNode node,
        params VBTypedValue[] inputs) => builder; // TODO: With-statement-specific semantic flags, once a caller (an analyzer) actually needs them.

    public override RuntimeSemanticsEvaluationResult Evaluate(
        IRuntimeSession session,
        WithStatementSemanticContext context,
        SyntaxNode node,
        params VBTypedValue[] inputs)
    {
        if (node is not WithStatementNode withStatement || inputs is not [{ } target])
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        if (target is not VBObjectValue)
        {
            // a UDT-valued target is Let-assigned instead - see the deferred-gap remark above.
            // MS-VBAL static semantics should already have rejected anything that's neither a class nor
            // a UDT (Object/Variant resolve to one of those, or Nothing, at run time).
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        var coercionResult = SetCoercionSemantics.EvaluateSetCoercion(session, withStatement.WithExpression, target, target.TypeInfo);
        return coercionResult.IsSuccess
            ? RuntimeSemanticsEvaluationResult.Success(coercionResult.Result!)
            : RuntimeSemanticsEvaluationResult.Error(coercionResult.ErrorInfo!);
    }
}
