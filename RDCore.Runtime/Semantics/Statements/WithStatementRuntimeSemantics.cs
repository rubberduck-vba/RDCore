using RDCore.Runtime.Semantics.Abstract;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Runtime.Semantics.Statements;

/// <summary>
/// <strong>MS-VBAL 5.4.2.21</strong> With Statement (runtime semantics). When the evaluated target's
/// value type is a class, it is Set-assigned to an anonymous <c>With</c> block variable
/// (<strong>MS-VBAL 5.5.2</strong> Set-coercion); a UDT-valued target is Let-assigned instead, per the
/// same MS-VBAL section.
/// </summary>
/// <remarks>
/// Scoped to producing the value that would be stored in that anonymous variable — the caller (the
/// executor) is the one that stashes it against a real frame slot, keyed by the <c>With</c>
/// instruction's own offset, so with-relative member access can read it back
/// (<see cref="RDCore.SDK.Runtime.Abstract.Execution.ICallStackFrame.TryGetBlockState"/>). This class
/// also does not raise MS-VBAL runtime error 91 (Object variable or With block variable not set) for a
/// <c>Nothing</c> target: Set-coercion's own Nothing-passthrough case is a success (MS-VBAL 5.5.2.2.1) —
/// error 91 is a member-access dereference concern, not part of this statement's own runtime semantics.
/// </remarks>
public sealed record class WithStatementRuntimeSemantics(
    ISetCoercionRuntimeSemantics SetCoercionSemantics,
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider)
    : StatementRuntimeSemantics<WithStatementSemanticContext, WithStatementSemanticFlags>
{
    public override ISemanticFlagsAccumulator<WithStatementSemanticFlags> Analyze(
        IRuntimeSession session,
        ConversionOperationSemanticContext conversionContext,
        ISemanticFlagsAccumulator<WithStatementSemanticFlags> builder,
        SyntaxNode node,
        params VBTypedValue[] inputs)
    {
        if (node is not WithStatementNode || inputs is not [{ } target])
        {
            return builder;
        }

        // the same two forms Evaluate tells apart, of what a Variant holds; anything else is no form of the statement.
        while (target is VBVariantValue { TypedValue: { } wrapped })
        {
            target = wrapped;
        }

        builder.AddFlags(target switch
        {
            VBObjectValue => WithStatementSemanticFlags.ClassTarget,
            VBUserDefinedTypeValue => WithStatementSemanticFlags.UserDefinedTypeTarget,
            _ => WithStatementSemanticFlags.Failed,
        });

        return builder;
    }

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

        // a Variant is what it holds: an object, a record, or a value of neither kind.
        var held = target;
        while (held is VBVariantValue { TypedValue: { } wrapped })
        {
            held = wrapped;
        }

        if (held is VBObjectValue)
        {
            var setResult = SetCoercionSemantics.EvaluateSetCoercion(session, withStatement.WithExpression, held, held.TypeInfo);
            return setResult.IsSuccess
                ? RuntimeSemanticsEvaluationResult.Success(setResult.Result!)
                : RuntimeSemanticsEvaluationResult.Error(setResult.ErrorInfo!);
        }

        if (held is not VBUserDefinedTypeValue)
        {
            // a Variant that holds neither is the block's target all the same, and a member of it is error 424 (MS-VBA, Excel-verified); any other type is
            // one MS-VBAL static semantics rejects.
            return target is VBVariantValue
                ? RuntimeSemanticsEvaluationResult.Success(target)
                : RuntimeSemanticsEvaluationResult.InternalError();
        }

        target = held;
        var frame = new LetCoercionStackFrame(withStatement.WithExpression.Identity, InputIndex.CoercionSourceValue, target, new VBTypeDescValue(target.TypeInfo), ConversionSite.WithTarget);
        var letResult = LetCoercionProvider.EvaluateLetCoercionSemantics(session.Symbols.Resolver, withStatement.WithExpression, frame);
        return letResult.IsSuccess
            ? RuntimeSemanticsEvaluationResult.Success(letResult.Result!)
            : RuntimeSemanticsEvaluationResult.Error(letResult.ErrorInfo!);
    }
}
