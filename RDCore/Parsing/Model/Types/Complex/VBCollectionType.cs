using RDCore.Parsing.Model.Types.Abstract;

namespace RDCore.Parsing.Model.Types.Complex;

/// <summary>
/// An object type that can be iterated in a <c>For Each...Next</c> loop.
/// </summary>
internal record class VBCollectionType : VBClassType, IEnumerableType
{
    public VBCollectionType(VBClassType vbClass)
        : this(vbClass.Symbol, vbClass.IsUserDefined, vbClass.Members, vbClass.Members.OfType<VBReturningMember>().Single(e => e.UserMemId == WellKnownDispIds.NewEnum)) { }

    public VBCollectionType(ClassModuleSymbol symbol, bool isUserDefined = false, IEnumerable<VBTypeMember>? members = null, VBReturningMember? newEnumMember = null)
        : base(symbol, isUserDefined, members)
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
    public VBReturningMember? NewEnumMember { get; init; }
}
