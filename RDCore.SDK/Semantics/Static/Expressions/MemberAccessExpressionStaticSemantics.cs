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
/// MS-VBAL 5.6.x Member Access Expressions (static semantics). The declared type of a qualified
/// member-access expression (<c>lhs.member</c>) is the declared type of the member its right-hand
/// identifier names on the left-hand side's owner type.
/// </summary>
/// <remarks>
/// Two behaviours the old, deleted <c>MemberAccessOperatorExpressionNode</c> runtime-eval sketch
/// described are out of scope here, not forgotten: <em>dictionary access</em> (<c>!</c>) and a
/// <em>module/project/library</em>-qualified reference (<c>Module.Member</c>). Neither is
/// representable yet — the AST has no way to distinguish <c>!</c> from <c>.</c>
/// (<see cref="MemberAccessOperatorExpressionNode"/> always carries the <c>.</c> token), and a
/// qualifier lookup needs "declared directly in this module, no outward walk", which
/// <see cref="ISymbolResolver"/> cannot express (its lexical walk starts at the qualifier's own
/// scope but keeps going outward on a miss). Both are follow-up work once their prerequisites land.
/// </remarks>
public sealed record class MemberAccessExpressionStaticSemantics : IStaticSemantics
{
    private static readonly Lazy<MemberAccessExpressionStaticSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);

    /// <summary>
    /// The shared instance — this rule has no state of its own.
    /// </summary>
    public static MemberAccessExpressionStaticSemantics Instance => _instance.Value;

    /// <summary>
    /// Determines the declared type of a <see cref="MemberAccessOperatorExpressionNode"/> by looking
    /// up its right-hand identifier among the members of its left-hand side's declared type.
    /// </summary>
    /// <param name="context">
    /// The compile-time context this expression is evaluated against. Unused — member lookup here is
    /// structural (an owner type's declared <c>Members</c>), not lexical.
    /// </param>
    /// <param name="expression">The <see cref="MemberAccessOperatorExpressionNode"/> being evaluated.</param>
    /// <param name="operandDeclaredTypes">
    /// The left-hand side's already-determined declared type, at
    /// <see cref="InputIndex.BinaryLeftOperand"/>. The right-hand side has no independent declared
    /// type to pass here — it is a bare member name, not a lexically resolved expression.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="expression"/> is not a <see cref="MemberAccessOperatorExpressionNode"/>, or its
    /// right-hand side is not a <see cref="SimpleNameExpressionNode"/> naming the member.
    /// </exception>
    public StaticSemanticsEvaluationResult DetermineDeclaredType(StaticEvaluationContext context, ExpressionNode expression, params VBType[] operandDeclaredTypes)
    {
        if (expression is not MemberAccessOperatorExpressionNode memberAccess)
        {
            throw new ArgumentException($"Expected a {nameof(MemberAccessOperatorExpressionNode)}.", nameof(expression));
        }

        if (memberAccess.Right is not SimpleNameExpressionNode member)
        {
            throw new ArgumentException(
                $"The right-hand side of a {nameof(MemberAccessOperatorExpressionNode)} must be a {nameof(SimpleNameExpressionNode)} naming the member.", nameof(expression));
        }

        var owner = operandDeclaredTypes[(int)InputIndex.BinaryLeftOperand];
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

        var found = ownerType.Members.FirstOrDefault(candidate => string.Equals(candidate.Name, member.IdentifierName, StringComparison.OrdinalIgnoreCase));
        if (found is not null)
        {
            return StaticSemanticsEvaluationResult.Success(found.ResolvedType);
        }

        // a class may gain members this rule cannot see yet (Implements, late-bound additions); a
        // UDT or Enum is a closed set of fields the parser already saw in full.
        return owner is VBClassType
            ? StaticSemanticsEvaluationResult.Success(VBUnknownType.TypeInfo)
            : StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.MethodOrDataMemberNotFound, expression.Location, member.IdentifierName));
    }
}
