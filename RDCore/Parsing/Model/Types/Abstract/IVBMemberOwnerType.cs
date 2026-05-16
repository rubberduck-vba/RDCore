using RDCore.Parsing.Model.Symbols.Abstract;
using System.Collections.Immutable;

namespace RDCore.Parsing.Model.Types.Abstract;

internal interface IVBMemberOwnerType
{
    ImmutableArray<VBTypeMemberSymbol> Members { get; init; }
    IVBMemberOwnerType WithMembers(IEnumerable<VBTypeMemberSymbol> members);
}
