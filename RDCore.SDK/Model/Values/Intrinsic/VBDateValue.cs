using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Intrinsic;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <c>Date</c> value.
/// </summary>
/// <param name="Symbol">The symbol associated with this value.</param>
public sealed record class VBDateValue(Symbol Symbol) : VBTypedValue(VBDateType.TypeInfo, Symbol),
    IVBTypedValue<VBDateValue, DateTime>
{
    private static readonly Lazy<VBDateValue> _minValue = new(() => new(GlobalSymbols.VBDateMinValue) { Value = new DateTime(100, 01, 01) }, LazyThreadSafetyMode.PublicationOnly);
    public static VBDateValue MinValue => _minValue.Value;

    private static readonly Lazy<VBDateValue> _maxValue = new(() => new(GlobalSymbols.VBDateMaxValue) { Value = new DateTime(9999, 12, 31, 23, 59, 59) }, LazyThreadSafetyMode.PublicationOnly);
    public static VBDateValue MaxValue => _maxValue.Value;

    private static readonly Lazy<VBDateValue> _zero = new(() => new(GlobalSymbols.VBDateZeroValue) { Value = new DateTime(1899, 12, 30) }, LazyThreadSafetyMode.PublicationOnly);
    public static VBDateValue Zero => _zero.Value;

    /// <summary>
    /// Overflow check used in operator semantics to check error conditions without throwing exceptions.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the specified value is outside the range of the data type; <c>false</c> otherwise.
    /// </returns>
    public static bool WillOverflow(double value) => value < MinSerial || value > MaxSerial;

    public const double MinSerial = -657434;
    public const double MaxSerial = 2958465;

    public double SerialValue => Value.ToOADate();
    public double AsDouble() => SerialValue;

    public DateTime Value { get; init; } = default;
    public override int Size => 8;

    /*
    public VBDoubleValue AsCoercedDouble(ref int depth) => new VBDoubleValue(Symbol).WithValue(SerialValue);
    public VBStringValue? AsCoercedString(ref int depth)
    {
        if (Value.Date.Equals(VBDateValue.Zero.Value.Date))
        {
            // MS-VBAL 5.5.1.2.4: Let-coercion to String / from Date
            // if the date value is zero, output is the LongTime format per regional settings.
            return new VBStringValue(Symbol).WithValue(Value.ToString("T", CultureInfo));
        }

        // otherwise output is the ShortDate format per regional settings.
        return new VBStringValue(Symbol).WithValue(Value.ToString("d", CultureInfo));
    }

    public VBFixedStringValue? AsCoercedFixedLengthString(int length, ref int depth)
    {
        if (AsCoercedString(ref depth) is VBStringValue stringValue)
        {
            return new VBFixedStringValue(length, stringValue.Symbol).WithFixedValue(stringValue.Value);
        }

        return default;
    }
    */
    public VBDateValue WithValue(DateTime value)
    {
        if (value > MaxValue.Value || value < MinValue.Value)
        {
            VBRuntimeErrorException.Overflow(Symbol.SelectionRange!, $"`{TypeInfo.Name}` values must be between **{MinValue.Value}** and **{MaxValue.Value}**.");
        }
        return this with { Value = value };
    }

    public VBDateValue WithValue(double value)
    {
        if (value > MaxSerial || value < MinSerial)
        {
            ThrowWithSymbol(symbol => VBRuntimeErrorException.Overflow(Symbol.SelectionRange!, $"`{TypeInfo.Name}` values must be between **{MinValue.Value}** and **{MaxValue.Value}**."));
        }
        return this with { Value = Zero.Value.AddDays(value) };
    }

    public bool Equals(IVBTypedValue<VBDateValue, DateTime>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
