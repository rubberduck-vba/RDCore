using RDCore.Parsing.Model.Symbols.Abstract;
using RDCore.Parsing.Model.Types.Intrinsic;
using RDCore.Parsing.Model.Values.Abstract;

namespace RDCore.Parsing.Model.Values.Intrinsic;

internal record class VBLongLongValue(Symbol? Symbol = null) : VBNumericTypedValue(VBLongLongType.TypeInfo, Symbol),
    IVBTypedValue<VBLongLongValue, long>,
    INumericValue<VBLongLongValue>
{
    public static VBLongLongValue MinValue { get; } = new VBLongLongValue { NumericValue = long.MinValue };
    public static VBLongLongValue MaxValue { get; } = new VBLongLongValue { NumericValue = long.MaxValue };
    public static VBLongLongValue Zero { get; } = new VBLongLongValue { NumericValue = 0 };

    VBLongLongValue INumericValue<VBLongLongValue>.MinValue => MinValue;
    VBLongLongValue INumericValue<VBLongLongValue>.Zero => Zero;
    VBLongLongValue INumericValue<VBLongLongValue>.MaxValue => MaxValue;

    public long Value => (long)NumericValue;
    public override int Size => sizeof(long);
    public override double NumericValue { get; init; }

    public new VBLongLongValue WithValue(double value) => this with { NumericValue = (long)value };
    public VBLongLongValue WithValue(long value) => this with { NumericValue = value };

    public bool Equals(IVBTypedValue<VBLongLongValue, long>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
