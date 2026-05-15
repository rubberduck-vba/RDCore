using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Values;

internal record class VBLongValue(Symbol? Symbol = null) : VBNumericTypedValue(VBLongType.TypeInfo, Symbol),
    IVBTypedValue<VBLongValue, int>,
    INumericValue<VBLongValue>
{
    public static VBLongValue MinValue { get; } = new VBLongValue { NumericValue = int.MinValue };
    public static VBLongValue MaxValue { get; } = new VBLongValue { NumericValue = int.MaxValue };
    public static VBLongValue Zero { get; } = new VBLongValue { NumericValue = 0 };

    VBLongValue INumericValue<VBLongValue>.MinValue => MinValue;
    VBLongValue INumericValue<VBLongValue>.Zero => Zero;
    VBLongValue INumericValue<VBLongValue>.MaxValue => MaxValue;

    public int Value => (int)NumericValue;
    public override int Size => sizeof(int);
    public override double NumericValue { get; init; }

    public new VBLongValue WithValue(double value)
    {
        if (value > MaxValue.Value || value < MinValue.Value)
        {
            throw VBRuntimeErrorException.Overflow(Symbol?.SelectionRange!, $"`{TypeInfo.Name}` values must be between **{MinValue.Value:N}** and **{MaxValue.Value:N}**.");
        }
        return this with { NumericValue = (int)value };
    }

    public VBLongValue WithValue(int value) => WithValue((double)value);

    public bool Equals(IVBTypedValue<VBLongValue, int>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
