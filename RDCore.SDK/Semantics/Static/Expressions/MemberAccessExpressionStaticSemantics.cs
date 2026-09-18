using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics.Static.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Static.Expressions;

/// <summary>
/// MS-VBAL 5.6 Member Access Expressions (static semantics). The declared type of
/// <c>owner.member</c> is the declared type of the member <see cref="MemberAccessExpressionNode.Member"/>
/// names on <see cref="MemberAccessExpressionNode.Owner"/>'s declared type.
/// </summary>
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
    /// The compile-time context this expression is evaluated against. Member lookup is structural (an
    /// owner type's own members), not lexical; the context's resolver is only used to read those members
    /// from the owner class's or user-defined type's declaration, by identity, rather than from the
    /// snapshot the type was built with — a class typed after itself cannot carry a complete snapshot.
    /// </param>
    /// <param name="expression">The <see cref="MemberAccessExpressionNode"/> being evaluated.</param>
    /// <param name="operandDeclaredTypes">
    /// The effective owner's already-determined declared type, at
    /// <see cref="InputIndex.MemberAccessOwner"/> — <see cref="MemberAccessExpressionNode.Owner"/>'s
    /// declared type, or, for a <c>with-expression</c> (<see cref="MemberAccessExpressionNode.Owner"/>
    /// is <c>null</c>), the innermost enclosing <c>With</c> block's target type (MS-VBAL §5.6.15),
    /// which the caller is responsible for substituting in — this rule doesn't care which it was.
    /// <see cref="MemberAccessExpressionNode.Member"/> has no independent declared type to pass here —
    /// it is a bare member name, not a lexically resolved expression.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="expression"/> is not a <see cref="MemberAccessExpressionNode"/>.</exception>
    public StaticSemanticsEvaluationResult DetermineDeclaredType(StaticEvaluationContext context, ExpressionNode expression, params VBType[] operandDeclaredTypes)
    {
        if (expression is not MemberAccessExpressionNode memberAccess)
        {
            throw new ArgumentException($"Expected a {nameof(MemberAccessExpressionNode)}.", nameof(expression));
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
            // a plain value type has no members to look up; deferred, not an error.
            return StaticSemanticsEvaluationResult.Success(VBUnknownType.TypeInfo);
        }

        var memberName = memberAccess.Member.IdentifierName;
        var found = CurrentMembersOf(context, ownerType).FirstOrDefault(candidate => string.Equals(candidate.Name, memberName, StringComparison.OrdinalIgnoreCase));
        if (found is not null)
        {
            return StaticSemanticsEvaluationResult.Success(found.ResolvedType);
        }

        // a class can have members this rule doesn't see (Implements, late-bound additions); a UDT
        // or Enum is a closed set of fields the parser already saw in full.
        return owner is VBClassType
            ? StaticSemanticsEvaluationResult.Success(VBUnknownType.TypeInfo)
            : StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.MethodOrDataMemberNotFound, expression.Location, memberName));
    }

    // A declared type carries the members its declaration had when the type was built, and a class whose
    // member is typed as the class itself - or two classes typed after each other - cannot be built by
    // value at all: some members of that snapshot hold types that were not bound yet. The declaration is
    // the source of truth, so the members are read from it, by the type's own identity, through the
    // resolver; the snapshot is what remains only when the declaration cannot be found.
    private static ImmutableArray<VBTypeMemberSymbol> CurrentMembersOf(StaticEvaluationContext context, IVBMemberOwnerType ownerType)
        => ownerType switch
        {
            VBClassType classType when context.Resolver.ResolveType(classType.Symbol.Name, ScopeKind.Global, StaticSymbol.GlobalUri).Symbol
                is VBClassModuleSymbol current => current.DefaultInterfaceMembers,
            VBUserDefinedType udtType when context.Resolver.ResolveType(udtType.Symbol.Name, ScopeKind.Global, udtType.Symbol.ParentUri).Symbol
                is VBUserDefinedTypeMemberSymbol current => current.Members,
            _ => ownerType.Members,
        };
}
