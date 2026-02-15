using RDCore.Parsing.Model.Abstract;
using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Types.Complex;
using RDCore.Parsing.Model.Types.Members;

namespace RDCore.Parsing.Model.Values;

internal record class VBUserDefinedTypeValue : VBTypedValue,
    IVBTypedValue<VBUserDefinedTypeValue, Guid>
{
    public VBUserDefinedTypeValue(VBUserDefinedType type, Symbol? symbol = null)
        : base(type, symbol)
    {
        Value = Guid.NewGuid();
    }

    public Guid Value { get; }

    public override int Size => ((IVBMemberOwnerType)TypeInfo).Members.OfType<VBUserDefinedTypeMember>()
        .Sum(member => member.ResolvedType!.DefaultValue.Size);
}
