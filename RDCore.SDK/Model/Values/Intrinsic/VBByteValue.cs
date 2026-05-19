using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Intrinsic;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <c>Byte</c> value.
/// </summary>
/// <param name="Symbol"></param>
public sealed record class VBByteValue(Symbol Symbol) : VBNumericTypedValue(VBByteType.TypeInfo, Symbol),
    IVBTypedValue<VBByteValue, byte>, INumericValue<VBByteValue>
{
    private static readonly Lazy<VBByteValue> _minValue = new(() => new VBByteValue(GlobalSymbols.VBByteMinValue) { ManagedValue = byte.MinValue }, LazyThreadSafetyMode.PublicationOnly);
    public static VBByteValue MinValue => _minValue.Value;

    private static readonly Lazy<VBByteValue> _maxValue = new(() => new VBByteValue(GlobalSymbols.VBByteMaxValue) { ManagedValue = byte.MaxValue }, LazyThreadSafetyMode.PublicationOnly);
    public static VBByteValue MaxValue { get; } = _maxValue.Value;

    private static readonly Lazy<VBByteValue> _zero = new(() => new VBByteValue(GlobalSymbols.VBByteZeroValue) { ManagedValue = 0 }, LazyThreadSafetyMode.PublicationOnly);
    public static VBByteValue Zero => _zero.Value;

    VBByteValue INumericValue<VBByteValue>.MinValue => MinValue;
    VBByteValue INumericValue<VBByteValue>.Zero => Zero;
    VBByteValue INumericValue<VBByteValue>.MaxValue => MaxValue;

    public byte Value => (byte)ManagedValue;
    public override int Size { get; } = 1;
    public override double ManagedValue { get; init; }

    public new VBByteValue WithValue(double value)
    {
        if (value > MaxValue.Value || value < MinValue.Value)
        {
            throw VBRuntimeErrorException.Overflow(Symbol?.SelectionRange!, $"`{TypeInfo.Name}` values must be between **{MinValue.Value:N}** and **{MaxValue.Value:N}**.");
        }
        return this with { ManagedValue = (byte)value };
    }

    public bool Equals(IVBTypedValue<VBByteValue, byte>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
