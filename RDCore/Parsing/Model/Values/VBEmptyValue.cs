using RDCore.Parsing.Model.Types;
using RDCore.Runtime;

namespace RDCore.Parsing.Model.Values;

internal record class VBEmptyValue : VBTypedValue,
    IVBTypedValue<VBEmptyValue, nint>,
    INumericCoercion,
    IStringCoercion
{
    public VBEmptyValue(): base(VBEmptyType.TypeInfo, GlobalSymbols.Empty) { }

    public static VBEmptyValue Empty { get; } = new() { TypeInfo = VBEmptyType.TypeInfo, Symbol = GlobalSymbols.Empty };

    public nint Value => nint.Zero;
    public override int Size => sizeof(int);

    public VBDoubleValue AsCoercedDouble(ref int depth) => VBDoubleValue.Zero;
    public VBStringValue AsCoercedString(ref int depth) => VBStringValue.ZeroLengthString;
    public VBFixedStringValue AsCoercedFixedLengthString(int length, ref int depth) => new(AsCoercedString(ref depth));

    public bool Equals(IVBTypedValue<VBEmptyValue, nint>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
