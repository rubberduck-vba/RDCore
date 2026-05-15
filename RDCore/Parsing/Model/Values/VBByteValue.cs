using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Values;

internal record class VBByteValue(Symbol? Symbol = null) : VBNumericTypedValue(VBByteType.TypeInfo, Symbol),
    IVBTypedValue<VBByteValue, byte>,
    INumericValue<VBByteValue>
{
    public static VBByteValue MinValue { get; } = new VBByteValue { NumericValue = byte.MinValue };
    public static VBByteValue MaxValue { get; } = new VBByteValue { NumericValue = byte.MaxValue };
    public static VBByteValue Zero { get; } = new VBByteValue { NumericValue = 0 };

    VBByteValue INumericValue<VBByteValue>.MinValue => MinValue;
    VBByteValue INumericValue<VBByteValue>.Zero => Zero;
    VBByteValue INumericValue<VBByteValue>.MaxValue => MaxValue;

    public byte Value => (byte)NumericValue;
    public override int Size { get; } = 1;
    public override double NumericValue { get; init; }

    public new VBByteValue WithValue(double value)
    {
        if (value > MaxValue.Value || value < MinValue.Value)
        {
            throw VBRuntimeErrorException.Overflow(Symbol?.SelectionRange!, $"`{TypeInfo.Name}` values must be between **{MinValue.Value:N}** and **{MaxValue.Value:N}**.");
        }
        return this with { NumericValue = (byte)value };
    }

    public bool Equals(IVBTypedValue<VBByteValue, byte>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
