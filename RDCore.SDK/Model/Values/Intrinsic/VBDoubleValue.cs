using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Intrinsic;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <c>Double</c> value.
/// </summary>
/// <param name="Symbol">The symbol associated with this value.</param>
public sealed record class VBDoubleValue(Symbol Symbol) : VBNumericTypedValue(VBDoubleType.TypeInfo, Symbol),
    IVBTypedValue<VBDoubleValue, double>, INumericValue<VBDoubleValue>
{
    private static readonly Lazy<VBDoubleValue> _minValue = new(() => new(GlobalSymbols.VBDoubleMinValue) { ManagedValue = double.MinValue * Math.Pow(10, -4), TypeInfo = VBDoubleType.TypeInfo }, LazyThreadSafetyMode.PublicationOnly);
    public static VBDoubleValue MinValue => _minValue.Value;

    private static readonly Lazy<VBDoubleValue> _maxValue = new(() => new(GlobalSymbols.VBDoubleMaxValue) { ManagedValue = double.MaxValue * Math.Pow(10, -4), TypeInfo = VBDoubleType.TypeInfo }, LazyThreadSafetyMode.PublicationOnly);
    public static VBDoubleValue MaxValue => _maxValue.Value;

    private static readonly Lazy<VBDoubleValue> _zero = new(() => new(GlobalSymbols.VBDoubleZeroValue) { ManagedValue = 0, TypeInfo = VBDoubleType.TypeInfo }, LazyThreadSafetyMode.PublicationOnly);
    public static VBDoubleValue Zero => _zero.Value;

    VBDoubleValue INumericValue<VBDoubleValue>.MinValue => MinValue;
    VBDoubleValue INumericValue<VBDoubleValue>.Zero => Zero;
    VBDoubleValue INumericValue<VBDoubleValue>.MaxValue => MaxValue;

    public double Value => ManagedValue;
    public override int Size => 8;
    public override double ManagedValue { get; init; }

    public new VBDoubleValue WithValue(double value) => this with { ManagedValue = value };

    public bool Equals(IVBTypedValue<VBDoubleValue, double>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
