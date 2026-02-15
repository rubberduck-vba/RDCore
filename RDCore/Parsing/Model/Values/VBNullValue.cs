using RDCore.Parsing.Model.Abstract;
using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Values;

internal record class VBNullValue : VBTypedValue, IVBTypedValue<VBNullValue, nint>
{
    public static VBNullValue Null { get; } = new VBNullValue();
    public VBNullValue(Symbol? symbol = null) : base(VBNullType.TypeInfo, symbol) { }

    public nint Value { get; } = nint.Zero;
    public override int Size => 0;
}
