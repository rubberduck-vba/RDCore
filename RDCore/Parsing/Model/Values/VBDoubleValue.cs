using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Values;

internal sealed record class VBDoubleValue : VBNumericTypedValue,
    IVBTypedValue<VBDoubleValue, double>,
    INumericValue<VBDoubleValue>
{
    public VBDoubleValue(Symbol? symbol = null)
        : base(VBDoubleType.TypeInfo, symbol) { }

    public static VBDoubleValue MinValue => new(){ NumericValue = double.MinValue * Math.Pow(10, -4), TypeInfo = VBDoubleType.TypeInfo };
    public static VBDoubleValue MaxValue => new(){ NumericValue = double.MaxValue * Math.Pow(10, -4), TypeInfo = VBDoubleType.TypeInfo };
    public static VBDoubleValue Zero => new(){ NumericValue = 0, TypeInfo = VBDoubleType.TypeInfo };

    VBDoubleValue INumericValue<VBDoubleValue>.MinValue => MinValue;
    VBDoubleValue INumericValue<VBDoubleValue>.Zero => Zero;
    VBDoubleValue INumericValue<VBDoubleValue>.MaxValue => MaxValue;

    public double Value => NumericValue;
    public override int Size => 8;
    public override double NumericValue { get; init; }

    public new VBDoubleValue WithValue(double value) => this with { NumericValue = value };

    public bool Equals(IVBTypedValue<VBDoubleValue, double>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
