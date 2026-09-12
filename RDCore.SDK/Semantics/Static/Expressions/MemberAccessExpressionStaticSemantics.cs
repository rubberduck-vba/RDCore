using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Expressions;

/// <summary>
/// MS-VBAL 5.6 Member Access Expressions (static semantics). The declared type of
/// <c>owner.member</c> is the declared type of the member <see cref="MemberAccessExpressionNode.Member"/>
/// names on <see cref="MemberAccessExpressionNode.Owner"/>'s declared type.
/// </summary>
/// <remarks>
/// Two cases are out of scope here, not forgotten: <em>dictionary access</em> (<c>!</c>) — a distinct
/// <c>l-expression</c> alternative in MS-VBAL, not representable by
/// <see cref="MemberAccessExpressionNode"/> until a parser builds its own node for it — and a
/// <em>module/project/library</em>-qualified reference (<c>Module.Member</c>), which needs "declared
/// directly in this container, no outward walk"; <see cref="ISymbolResolver"/> cannot express that —
/// its lexical walk starts at the qualifier's own scope but keeps going outward on a miss. Both are
/// follow-up work once their prerequisites land.
/// </remarks>
public sealed record class MemberAccessExpressionStaticSemantics : IStaticSemantics
{
    private static readonly Lazy<MemberAccessExpressionStaticSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);

    /// <summary>
    /// The shared instance — this rule has no state of its own.
    /// </summary>
    public static MemberAccessExpressionStaticSemantics Instance => _instance.Value;

    /// <summary>
    /// Determines the declared type of a <see cref="MemberAccessExpressionNode"/> by looking up its
    /// <see cref="MemberAccessExpressionNode.Member"/> identifier among the members of its
    /// <see cref="MemberAccessExpressionNode.Owner"/>'s declared type.
    /// </summary>
    /// <param name="context">
    /// The compile-time context this expression is evaluated against. Unused — member lookup here is
    /// structural (an owner type's declared <c>Members</c>), not lexical.
    /// </param>
    /// <param name="expression">The <see cref="MemberAccessExpressionNode"/> being evaluated.</param>
    /// <param name="operandDeclaredTypes">
    /// <see cref="Owner"/>'s already-determined declared type, at
    /// <see cref="InputIndex.MemberAccessOwner"/>. <see cref="MemberAccessExpressionNode.Member"/> has
    /// no independent declared type to pass here — it is a bare member name, not a lexically resolved
    /// expression.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="expression"/> is not a <see cref="MemberAccessExpressionNode"/>.</exception>
    /// <exception cref="NotSupportedException">
    /// <paramref name="expression"/> is a <c>with-expression</c> (<see cref="MemberAccessExpressionNode.Owner"/>
    /// is <c>null</c>) — resolving one needs the enclosing <c>With</c> block's target type, which
    /// nothing tracks yet (see <c>rdcore-with-relative-member-access-ticket.md</c>).
    /// </exception>
    public StaticSemanticsEvaluationResult DetermineDeclaredType(StaticEvaluationContext context, ExpressionNode expression, params VBType[] operandDeclaredTypes)
    {
        if (expression is not MemberAccessExpressionNode memberAccess)
        {
            throw new ArgumentException($"Expected a {nameof(MemberAccessExpressionNode)}.", nameof(expression));
        }

        if (memberAccess.Owner is null)
        {
            throw new NotSupportedException(
                "With-relative member access (an implicit owner) needs the enclosing With block's target type, which nothing tracks yet.");
        }

        var owner = operandDeclaredTypes[(int)InputIndex.MemberAccessOwner];
        if (owner is VBVariantType or VBObjectType)
        {
            // late-bound: static semantics cannot know what the run-time object actually supports.
            // Recording that fact is a semantic-analysis concern (RD-VBAL §5.0.3), not this result.
            return StaticSemanticsEvaluationResult.Success(VBVariantType.TypeInfo);
        }

        if (owner is not IVBMemberOwnerType ownerType)
        {
            // not yet a case this rule decides (e.g. a plain value type) — deferred, not an error.
            return StaticSemanticsEvaluationResult.Success(VBUnknownType.TypeInfo);
        }

        var memberName = memberAccess.Member.IdentifierName;
        var found = ownerType.Members.FirstOrDefault(candidate => string.Equals(candidate.Name, memberName, StringComparison.OrdinalIgnoreCase));
        if (found is not null)
        {
            return StaticSemanticsEvaluationResult.Success(found.ResolvedType);
        }

        // a class may gain members this rule cannot see yet (Implements, late-bound additions); a
        // UDT or Enum is a closed set of fields the parser already saw in full.
        return owner is VBClassType
            ? StaticSemanticsEvaluationResult.Success(VBUnknownType.TypeInfo)
            : StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.MethodOrDataMemberNotFound, expression.Location, memberName));
    }
}
