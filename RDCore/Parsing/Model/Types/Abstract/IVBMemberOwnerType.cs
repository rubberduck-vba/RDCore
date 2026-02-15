using RDCore.Parsing.Model.Types.Complex;
using System.Collections.Immutable;

namespace RDCore.Parsing.Model.Types.Abstract;

internal interface IVBMemberOwnerType
{
    ImmutableArray<VBTypeMember> Members { get; init; }
    IVBMemberOwnerType WithMembers(IEnumerable<VBTypeMember> members);
}
