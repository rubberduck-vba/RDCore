using RDCore.SDK.Model.Symbols.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Types.Abstract;

public interface IVBMemberOwnerType
{
    ImmutableArray<VBTypeMemberSymbol> Members { get; init; }
    IVBMemberOwnerType WithMembers(IEnumerable<VBTypeMemberSymbol> members);
}
