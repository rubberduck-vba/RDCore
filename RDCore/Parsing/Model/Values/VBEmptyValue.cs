using RDCore.Parsing.Model.Abstract;
using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Values;

internal record class VBEmptyValue : VBTypedValue,
    IVBTypedValue<VBEmptyValue, nint>,
    INumericCoercion,
    IStringCoercion
{
    public VBEmptyValue(Symbol? symbol = null)
        : base(VBEmptyType.TypeInfo, symbol) { }

    public static VBEmptyValue Empty { get; } = new VBEmptyValue();

    public nint Value => nint.Zero;
    public override int Size => sizeof(int);

    public VBDoubleValue AsCoercedNumeric(int depth = 0) => VBDoubleValue.Zero;
    public VBStringValue AsCoercedString(int depth = 0) => VBStringValue.ZeroLengthString;
}
