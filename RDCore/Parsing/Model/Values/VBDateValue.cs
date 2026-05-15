using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Values;

internal record class VBDateValue(Symbol? Symbol = null) : VBTypedValue(VBDateType.TypeInfo, Symbol),
    IVBTypedValue<VBDateValue, DateTime>,
    INumericCoercion,
    IStringCoercion
{
    public static VBDateValue MinValue { get; } = new() { Value = new DateTime(100, 01, 01) };
    public static VBDateValue MaxValue { get; } = new() { Value = new DateTime(9999, 12, 31, 23, 59, 59) };
    public static VBDateValue Zero { get; } = new() { Value = new DateTime(1899, 12, 30) };

    /// <summary>
    /// Overflow check used in operator semantics to check error conditions without throwing exceptions.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the specified value is outside the range of the data type; <c>false</c> otherwise.
    /// </returns>
    public static bool WillOverflow(double value) => value < MinSerial || value > MaxSerial;

    public const double MinSerial = -657434;
    public const double MaxSerial = 2958465;
    public static VBDateValue FromSerial(double value) => new() { Value = DateTime.FromOADate(value) };

    public double SerialValue => Value.ToOADate();
    public double AsDouble() => SerialValue;

    public DateTime Value { get; init; } = default;
    public override int Size => 8;

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

    public VBDateValue WithValue(DateTime value)
    {
        if (value > MaxValue.Value || value < MinValue.Value)
        {
            ThrowWithSymbol(symbol => VBRuntimeErrorException.Overflow(symbol.SelectionRange!, $"`{TypeInfo.Name}` values must be between **{MinValue.Value}** and **{MaxValue.Value}**."));
        }
        return this with { Value = value };
    }

    public VBDateValue WithValue(double value)
    {
        if (value > MaxSerial || value < MinSerial)
        {
            ThrowWithSymbol(symbol => VBRuntimeErrorException.Overflow(symbol.SelectionRange!, $"`{TypeInfo.Name}` values must be between **{MinValue.Value}** and **{MaxValue.Value}**."));
        }
        return this with { Value = Zero.Value.AddDays(value) };
    }

    public bool Equals(IVBTypedValue<VBDateValue, DateTime>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
