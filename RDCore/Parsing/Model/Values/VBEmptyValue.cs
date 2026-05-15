using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Types;
using RDCore.Runtime;

namespace RDCore.Parsing.Model.Values;

internal record class VBEmptyValue(Symbol? Symbol = null) : VBTypedValue(VBEmptyType.TypeInfo, Symbol),
    IVBTypedValue<VBEmptyValue, nint>,
    INumericCoercion,
    IStringCoercion
{
    private static readonly Lazy<VBEmptyValue> _emptyValue = new(() => new(GlobalSymbols.Empty), LazyThreadSafetyMode.PublicationOnly);
    public static VBEmptyValue Empty { get; } = _emptyValue.Value;

    public nint Value => nint.Zero;
    public override int Size => sizeof(int);

    public VBDoubleValue AsCoercedDouble(ref int depth) => VBDoubleValue.Zero;
    public VBStringValue AsCoercedString(ref int depth) => VBStringValue.ZeroLengthString;
    public VBFixedStringValue AsCoercedFixedLengthString(int length, ref int depth) => new(AsCoercedString(ref depth));

    public bool Equals(IVBTypedValue<VBEmptyValue, nint>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
