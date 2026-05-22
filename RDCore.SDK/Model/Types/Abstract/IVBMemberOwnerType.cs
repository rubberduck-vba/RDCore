using RDCore.SDK.Model.Symbols.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Types.Abstract;

/// <summary>
/// An interface representing any <c>VBType</c> that describes a <c>Symbol</c> may declare members.
/// </summary>
public interface IVBMemberOwnerType
{
    /// <summary>
    /// Gets the members of this type.
    /// </summary>
    ImmutableArray<VBTypeMemberSymbol> Members { get; init; }
    /// <summary>
    /// Gets a copy of this type with the specified <c>members</c>.
    /// </summary>
    /// <param name="members">The members of the new type.</param>
    IVBMemberOwnerType WithMembers(IEnumerable<VBTypeMemberSymbol> members);
}
