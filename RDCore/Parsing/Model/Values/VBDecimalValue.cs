using RDCore.Parsing.Model.Abstract;
using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Values;

internal record class VBDecimalValue : VBNumericTypedValue,
    IVBTypedValue<VBDecimalValue, decimal>,
    INumericValue<VBDecimalValue>
{
    public VBDecimalValue(Symbol? declarationSymbol = null)
        : base(VBDecimalType.TypeInfo, declarationSymbol) { }

    public static VBDecimalValue MinValue { get; } = new VBDecimalValue { NumericValue = (double)(long.MinValue * Math.Pow(10, -4)) };
    public static VBDecimalValue MaxValue { get; } = new VBDecimalValue { NumericValue = (double)(long.MaxValue * Math.Pow(10, -4)) };
    public static VBDecimalValue Zero { get; } = new VBDecimalValue { NumericValue = 0 };

    VBDecimalValue INumericValue<VBDecimalValue>.MinValue => MinValue;
    VBDecimalValue INumericValue<VBDecimalValue>.Zero => Zero;
    VBDecimalValue INumericValue<VBDecimalValue>.MaxValue => MaxValue;

    public decimal Value => (decimal)NumericValue;
    public override int Size => 14;
    public override double NumericValue { get; init; }

    public new VBDecimalValue WithValue(double value) => this with { NumericValue = (double)value };
}
