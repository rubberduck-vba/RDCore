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
            // a number, a String, a Boolean or a Date has no members: MS-VBA refuses a member of one at compile time (Excel-verified). Any other type that
            // is not known to own members is deferred, not an error.
            return owner is VBNumericType or VBStringType or VBBooleanType or VBDateType
                ? StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.InvalidQualifier, expression.Location, memberAccess.Member.IdentifierName))
                : StaticSemanticsEvaluationResult.Success(VBUnknownType.TypeInfo);
        }

        var memberName = memberAccess.Member.IdentifierName;
        // MS-VBAL §5.6.2.1: a property is evaluated as a data value through its Property Get, which a Property Let or Set of the same name declared before it
        // is not.
        var named = CurrentMembersOf(context, ownerType).Where(candidate => string.Equals(candidate.Name, memberName, StringComparison.OrdinalIgnoreCase)).ToArray();
        var found = named.OfType<VBPropertyGetMemberSymbol>().FirstOrDefault() ?? named.FirstOrDefault();
        if (found is not null)
        {
            return StaticSemanticsEvaluationResult.Success(found.ResolvedType);
        }

        // an extensible class can have members its declaration does not: a library's, whose host adds members to its objects (`Excel.Application.XLookup`,
        // and MS-VBA compiles `Err.Nope` and `c.Nope` of a Collection, raising 438 at run time), or a class module with Attribute VB_Extensible. Any other
        // class module of the workspace is a closed set of members the declaration pass saw in full, as a UDT or an Enum is, and MS-VBA rejects an unknown
        // member of it at compile time (Excel-verified).
        return owner is VBClassType classType && IsExtensible(context, classType)
            ? StaticSemanticsEvaluationResult.Success(VBUnknownType.TypeInfo)
            : StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.MethodOrDataMemberNotFound, expression.Location, memberName));
    }

    // whether a class says it is extensible (SymbolProperties.Extensible), as its declaration is now.
    private static bool IsExtensible(StaticEvaluationContext context, VBClassType classType)
        => (VBProjectSymbol.ResolveClass(context.Resolver, classType.Symbol) ?? classType.Symbol).GetProperty(SymbolProperties.Extensible);

    // A declared type carries the members its declaration had when the type was built, and a class whose
    // member is typed as the class itself - or two classes typed after each other - cannot be built by
    // value at all: some members of that snapshot hold types that were not bound yet. The declaration is
    // the source of truth, so the members are read from it, by the type's own identity, through the
    // resolver; the snapshot is what remains only when the declaration cannot be found.
    internal static ImmutableArray<VBTypeMemberSymbol> CurrentMembersOf(StaticEvaluationContext context, IVBMemberOwnerType ownerType)
        => ownerType switch
        {
            VBClassType classType when VBProjectSymbol.ResolveClass(context.Resolver, classType.Symbol) is { } current => current.DefaultInterfaceMembers,
            VBUserDefinedType udtType when context.Resolver.ResolveType(udtType.Symbol.Name, ScopeKind.Global, udtType.Symbol.ParentUri).Symbol
                is VBUserDefinedTypeMemberSymbol current => current.Members,
            _ => ownerType.Members,
        };
}
