using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Unbound;
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.SDK.Model.Types.Complex;

/// <summary>
/// An object type that can be iterated in a <c>For Each...Next</c> loop.
/// </summary>
public record class VBCollectionType : VBClassType, IEnumerableType
{
    public VBCollectionType(VBClassType vbClass)
        : this(vbClass.Symbol, vbClass.Members, vbClass.Members.OfType<VBReturningMemberSymbol>().Single(e => e.GetProperty(SymbolProperties.UserMemId) == WellKnownDispIds.NewEnum)) { }

    public VBCollectionType(VBClassModuleSymbol symbol, IEnumerable<VBTypeMemberSymbol>? members = null, VBReturningMemberSymbol? newEnumMember = null)
        : base(symbol, [.. (members ?? []).Where(m => m is not null)])
    {
        NewEnumMember = newEnumMember;
    }

    public bool IsArray { get; } = false;
    /// <summary>
    /// The member that provides the enumerator for this collection.
    /// </summary>
    /// <remarks>
    /// Controlled by the <c>VB_UserMemId</c> attribute with a value of <c>-4</c> or the <c>@NewEnum</c> annotation.
    /// </remarks>
    public VBReturningMemberSymbol? NewEnumMember { get; init; }
}
