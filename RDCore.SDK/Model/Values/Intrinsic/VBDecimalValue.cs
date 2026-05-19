using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Intrinsic;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <c>Decimal</c> value.
/// </summary>
/// <param name="Symbol">The symbol associated with this value.</param>
public sealed record class VBDecimalValue(Symbol Symbol) : VBNumericTypedValue(VBDecimalType.TypeInfo, Symbol),
    IVBTypedValue<VBDecimalValue, decimal>, INumericValue<VBDecimalValue>
{
    private static readonly Lazy<VBDecimalValue> _minValue = new(() => new VBDecimalValue(GlobalSymbols.VBDecimalMinValue) { ManagedValue = (double)(long.MinValue * Math.Pow(10, -4))}, LazyThreadSafetyMode.PublicationOnly);
    public static VBDecimalValue MinValue => _minValue.Value;

    private static readonly Lazy<VBDecimalValue> _maxValue = new(() => new VBDecimalValue(GlobalSymbols.VBDecimalMaxValue) { ManagedValue = (double)(long.MaxValue * Math.Pow(10, -4)) }, LazyThreadSafetyMode.PublicationOnly);
    public static VBDecimalValue MaxValue => _maxValue.Value;

    private static readonly Lazy<VBDecimalValue> _zero = new(() => new VBDecimalValue(GlobalSymbols.VBDecimalZeroValue) { ManagedValue = 0 }, LazyThreadSafetyMode.PublicationOnly);
    public static VBDecimalValue Zero => _zero.Value;

    VBDecimalValue INumericValue<VBDecimalValue>.MinValue => MinValue;
    VBDecimalValue INumericValue<VBDecimalValue>.Zero => Zero;
    VBDecimalValue INumericValue<VBDecimalValue>.MaxValue => MaxValue;

    public decimal Value => (decimal)ManagedValue;
    public override int Size => 14;
    public override double ManagedValue { get; init; }

    public VBDecimalValue WithValue(decimal value) => this with { ManagedValue = (double)value };

    public bool Equals(IVBTypedValue<VBDecimalValue, decimal>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
