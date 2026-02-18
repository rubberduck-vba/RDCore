using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Types.Complex;

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

    public bool Equals(IVBTypedValue<VBUserDefinedTypeValue, Guid>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
