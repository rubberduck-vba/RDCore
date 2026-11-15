using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using System.Collections.Immutable;
using RDCore.SDK.Semantics;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Runtime.LanguageCore.Static.Expressions;

// MS-VBAL §5.6
public sealed record class MemberAccessExpressionStaticSemantics : IStaticSemantics
{
    private static readonly Lazy<MemberAccessExpressionStaticSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);

    public static MemberAccessExpressionStaticSemantics Instance => _instance.Value;

    // MS-VBAL §5.6.15
    // members are read from the declaration by identity, not from the type's snapshot: a class typed after itself cannot carry a complete one.
    public StaticSemanticsEvaluationResult DetermineDeclaredType(StaticEvaluationContext context, ExpressionNode expression, params VBType[] operandDeclaredTypes)
    {
        if (expression is not MemberAccessExpressionNode memberAccess)
        {
            throw new ArgumentException($"Expected a {nameof(MemberAccessExpressionNode)}.", nameof(expression));
        }

        // a type a library names before it reads it (a member that returns the class being read) is the type of that name, resolved where it is used.
        var owner = operandDeclaredTypes[(int)InputIndex.MemberAccessOwner];
        owner = owner is VBUnresolvedType ? CurrentTypes.Of(context.Resolver, owner) : owner;
        if (owner is VBVariantType or VBObjectType)
        {
            // late-bound: static semantics cannot know what the run-time object actually supports.
            // Recording that fact is a semantic-analysis concern (RD-VBAL §5.0.3), not this result.
            return StaticSemanticsEvaluationResult.Success(VBVariantType.TypeInfo);
        }

        if (owner is not IVBMemberOwnerType ownerType)
        {
            // a number, a String, a Boolean or a Date has no members, nor does an array, fixed-size or resizable, read or assigned: MS-VBA refuses a member of one at
            // compile time (observed in Excel). Any other type that is not known to own members is deferred, not an error.
            return owner is VBNumericType or VBStringType or VBBooleanType or VBDateType or VBArrayType
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
            return StaticSemanticsEvaluationResult.Success(found.ResolvedType is VBUnresolvedType ? CurrentTypes.Of(context.Resolver, found.ResolvedType) : found.ResolvedType);
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
    // resolver; the snapshot is what remains only when the declaration cannot be found. A document module's
    // members are its host's class's too (VBProjectSymbol.InterfaceMembersOf).
    internal static ImmutableArray<VBTypeMemberSymbol> CurrentMembersOf(StaticEvaluationContext context, IVBMemberOwnerType ownerType)
        => ownerType switch
        {
            VBClassType classType when VBProjectSymbol.ResolveClass(context.Resolver, classType.Symbol) is { } current
                => current.HostClass is null ? current.DefaultInterfaceMembers : [.. VBProjectSymbol.InterfaceMembersOf(context.Resolver, current)],
            VBUserDefinedType udtType when context.Resolver.ResolveType(udtType.Symbol.Name, ScopeKind.Global, udtType.Symbol.ParentUri).Symbol
                is VBUserDefinedTypeMemberSymbol current => current.Members,
            _ => ownerType.Members,
        };
}
