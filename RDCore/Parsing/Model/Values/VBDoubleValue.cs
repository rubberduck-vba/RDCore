using RDCore.Parsing.Model.Abstract;
using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Values;

internal record class VBDoubleValue : VBNumericTypedValue,
    IVBTypedValue<VBDoubleValue, double>,
    INumericValue<VBDoubleValue>
{
    public VBDoubleValue(Symbol? symbol = null)
        : base(VBDoubleType.TypeInfo, symbol) { }

    public static VBDoubleValue MinValue { get; } = new VBDoubleValue { NumericValue = double.MinValue * Math.Pow(10, -4) };
    public static VBDoubleValue MaxValue { get; } = new VBDoubleValue { NumericValue = double.MaxValue * Math.Pow(10, -4) };
    public static VBDoubleValue Zero { get; } = new VBDoubleValue { NumericValue = 0 };

    VBDoubleValue INumericValue<VBDoubleValue>.MinValue => MinValue;
    VBDoubleValue INumericValue<VBDoubleValue>.Zero => Zero;
    VBDoubleValue INumericValue<VBDoubleValue>.MaxValue => MaxValue;

    public double Value => NumericValue;
    public override int Size => 8;
    public override double NumericValue { get; init; }

    public new VBDoubleValue WithValue(double value) => this with { NumericValue = value };
}
