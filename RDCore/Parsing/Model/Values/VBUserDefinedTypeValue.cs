using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Types.Complex;

namespace RDCore.Parsing.Model.Values;

internal record class VBUserDefinedTypeValue : VBTypedValue,
    IVBTypedValue<VBUserDefinedTypeValue, VBLongPtrValue>
{
    public VBUserDefinedTypeValue(VBUserDefinedType type, Symbol? symbol = null)
        : base(type, symbol)
    {
        Value = VBLongPtrValue.Zero;
    }

    public VBLongPtrValue Value { get; }

    // +padding...
    public override int Size => ((IVBMemberOwnerType)TypeInfo).Members.OfType<VBUserDefinedTypeMember>()
        .Sum(member => member.ResolvedType!.DefaultValue.Size);

    public bool Equals(IVBTypedValue<VBUserDefinedTypeValue, VBLongPtrValue>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
