using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Types.Complex;

namespace RDCore.Parsing.Model.Values;

internal record class VBUserDefinedTypeValue : VBTypedValue,
    IVBTypedValue<VBUserDefinedTypeValue, VBLongPtrValue>
{
    // primary ctor type mismatch VBUserDefinedType->VBType
    public VBUserDefinedTypeValue(VBUserDefinedType typeInfo, Symbol? symbol = null)
        : base(typeInfo, symbol) { }

    public VBLongPtrValue Value { get; } = VBLongPtrValue.Zero;

    // +padding...
    public override int Size => ((IVBMemberOwnerType)TypeInfo).Members.OfType<VBUserDefinedTypeMember>()
        .Sum(member => member.ResolvedType!.DefaultValue.Size);

    public bool Equals(IVBTypedValue<VBUserDefinedTypeValue, VBLongPtrValue>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
