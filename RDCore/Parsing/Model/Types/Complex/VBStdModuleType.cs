using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values;
using System.Collections.Immutable;

namespace RDCore.Parsing.Model.Types.Complex;

internal record class VBStdModuleType : VBType, IVBMemberOwnerType
{
    public VBStdModuleType(string name, bool isUserDefined = true, IEnumerable<VBTypeMember>? members = null, bool isHidden = false)
        : base(typeof(object), name, isUserDefined, isHidden)
    {
        Members = [.. members ?? []];
    }

    public override VBTypedValue DefaultValue => VBLongPtrType.TypeInfo.DefaultValue;

    public ImmutableArray<VBTypeMember> Members { get; init; }

    public IVBMemberOwnerType WithMembers(IEnumerable<VBTypeMember> members) => this with { Members = [.. members] };
}