using RDCore.Parsing.Model.Symbols;
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

    public VBDoubleValue AsCoercedNumeric(ref int depth) => VBDoubleValue.Zero;
    public VBStringValue AsCoercedString(ref int depth) => VBStringValue.ZeroLengthString;
    public VBFixedStringValue AsCoercedFixedLengthString(int length, ref int depth) => new(AsCoercedString(ref depth));

    public bool Equals(IVBTypedValue<VBEmptyValue, nint>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
