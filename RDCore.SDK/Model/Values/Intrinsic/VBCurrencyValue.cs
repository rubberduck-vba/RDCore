using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Intrinsic;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <c>Currency</c> value.
/// </summary>
/// <param name="Symbol">The symbol associated with this value.</param>
public sealed record class VBCurrencyValue(Symbol Symbol) : VBNumericTypedValue(VBCurrencyType.TypeInfo, Symbol),
    IVBTypedValue<VBCurrencyValue, decimal>, INumericValue<VBCurrencyValue>
{
    private static readonly Lazy<VBCurrencyValue> _minValue = new(() => new VBCurrencyValue(GlobalSymbols.VBCurrencyMinValue) { ManagedValue = (double)(long.MinValue * Math.Pow(10, -4)) }, LazyThreadSafetyMode.PublicationOnly);
    public static VBCurrencyValue MinValue => _minValue.Value;

    private static readonly Lazy<VBCurrencyValue> _maxValue = new(() => new VBCurrencyValue(GlobalSymbols.VBCurrencyMaxValue) { ManagedValue = (double)(long.MaxValue * Math.Pow(10, -4)) }, LazyThreadSafetyMode.PublicationOnly);
    public static VBCurrencyValue MaxValue { get; } = _maxValue.Value;

    private static readonly Lazy<VBCurrencyValue> _zero = new(() => new VBCurrencyValue(GlobalSymbols.VBCurrencyZeroValue) { ManagedValue = (double)0 }, LazyThreadSafetyMode.PublicationOnly);
    public static VBCurrencyValue Zero { get; } = _zero.Value;

    VBCurrencyValue INumericValue<VBCurrencyValue>.MinValue => MinValue;
    VBCurrencyValue INumericValue<VBCurrencyValue>.Zero => Zero;
    VBCurrencyValue INumericValue<VBCurrencyValue>.MaxValue => MaxValue;

    public decimal Value => (decimal)ManagedValue;
    public override int Size { get; } = sizeof(long);
    public override double ManagedValue { get; init; }

    public VBCurrencyValue WithValue(decimal value) => this with { ManagedValue = (double)value };

    public bool Equals(IVBTypedValue<VBCurrencyValue, decimal>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
