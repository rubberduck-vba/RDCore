using Microsoft.Extensions.Primitives;
using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Types;
using System.Diagnostics;

namespace RDCore.Parsing.Model.Values;

internal abstract record class VBNumericTypedValue(VBType TypeInfo, Symbol? Symbol = null) : VBTypedValue(TypeInfo, Symbol),
    IComparable<INumericValue>, IEquatable<VBNumericTypedValue>,
    INumericValue, INumericCoercion, 
    IStringCoercion, 
    IBooleanCoercion, 
    IDateCoercion
{
    /// <summary>
    /// The maximum possible number of significant digits retained in a String representation of a value of this type.
    /// </summary>
    public const int SignificantIntegerDigits = VBDoubleType.SignificantIntegerDigits;

    public abstract double NumericValue { get; init; }

    public virtual VBDoubleValue AsCoercedDouble(ref int depth) => AsDouble();
    public virtual VBStringValue AsCoercedString(ref int depth)
    {
        var isNegative = NumericValue < 0;
        var sign = isNegative ? "-" : string.Empty;

        var absoluteValue = Math.Abs(NumericValue);
        var stringValue = absoluteValue.ToString(CultureInfo);

        var dot = CultureInfo.NumberFormat.NumberDecimalSeparator;
        var decimalIndex = stringValue.IndexOf(dot);

        if (decimalIndex >= 0)
        {
            var integerString = stringValue[..(decimalIndex - 1)];
            // MS-VBAL 5.5.1.2.4 Let-coercion to and from String

            // VBSingleValue uses normal notation for values up to 7 integer digits, scientific notation otherwise:
            if (this is VBSingleValue && integerString.Length > VBSingleType.SignificantIntegerDigits)
            {
                var significantIntegerDigits = VBSingleType.SignificantIntegerDigits;
                stringValue = ToVBScientificNotation(NumericValue, significantIntegerDigits, dot);

            }
            else if (integerString.Length > VBDoubleType.SignificantIntegerDigits)
            {
                // Double (or any other numeric type for that matter): truncate to 15 significant digits
                var significantIntegerDigits = VBDoubleType.SignificantIntegerDigits;
                stringValue = ToVBScientificNotation(NumericValue, significantIntegerDigits, dot);
            }
            else
            {
                // we're taking a small shortcut above by presuming of the equality of two constants:
                Debug.Assert(VBDoubleType.SignificantIntegerDigits == VBNumericTypedValue.SignificantIntegerDigits);
            }
        }
        else
        {
            stringValue = $"{sign}{stringValue}";
        }

        return new VBStringValue(Symbol).WithValue(stringValue);
    }

    public static string ToVBScientificNotation(double value, int significantIntegerDigits, string decimalSeparator)
    {
        var absoluteValue = Math.Abs(value);
        var sign = value < 0 ? "-" : string.Empty;

        var stringValue = absoluteValue.ToString(CultureInfo);
        var decimalIndex = stringValue.IndexOf(decimalSeparator);

        var integerString = stringValue[..(decimalIndex - 1)];
        var decimalString = stringValue[(decimalIndex + 1)..];

        // s * 10^e
        char s;
        int e;
        if (absoluteValue >= 1)
        {
            s = integerString[0];
            e = decimalIndex; // magnitude is just where the decimal separator is at (positive)
        }
        else
        {
            // s is the first non-zero digit
            var nzIndex = decimalString.IndexOfAny(['1', '2', '3', '4', '5', '6', '7', '8', '9']);
            s = decimalString[nzIndex];
            e = -(nzIndex + 1); // magnitude is the (negative) number of decimal positions shifted
        }

        // combined integer+decimal parts cannot exceed a length of 15:
        decimalString = $"{integerString[1..]}{decimalString}";
        var fullValue = $"{integerString}{decimalSeparator}{decimalString}";

        var fullValueLength = fullValue.Length;
        decimalString = fullValueLength > significantIntegerDigits
            ? decimalString[..significantIntegerDigits]
            : decimalString;

        return $"{sign}{s}{decimalSeparator}{decimalString}E{e}";
    }

    public virtual VBFixedStringValue AsCoercedFixedLengthString(int length, ref int depth) => new VBFixedStringValue(length, this.Symbol).WithFixedValue(this.AsCoercedString(ref depth).Value);
    public virtual VBBooleanValue AsCoercedBoolean(ref int depth) => AsBoolean();
    public virtual VBDateValue AsCoercedDate(ref int depth) => new VBDateValue(Symbol).WithValue(NumericValue);

    public VBBooleanValue AsBoolean() => new VBBooleanValue(Symbol).WithValue(NumericValue != 0);
    public VBByteValue AsByte() => new VBByteValue(Symbol).WithValue(NumericValue);
    public VBCurrencyValue AsCurrency() => new VBCurrencyValue(Symbol).WithValue(NumericValue);
    public VBDecimalValue AsDecimal() => new VBDecimalValue(Symbol).WithValue(NumericValue);
    public VBDoubleValue AsDouble() => new VBDoubleValue(Symbol).WithValue(NumericValue);
    public VBIntegerValue AsInteger() => new VBIntegerValue(Symbol).WithValue(NumericValue);
    public VBLongValue AsLong() => new VBLongValue(Symbol).WithValue(NumericValue);
    public VBLongLongValue AsLongLong() => new VBLongLongValue(Symbol).WithValue(NumericValue);
    public VBSingleValue AsSingle() => new VBSingleValue(Symbol).WithValue(NumericValue);

    public int CompareTo(INumericValue? other) => other is null ? 1 : NumericValue.CompareTo(other.NumericValue);

    public INumericValue WithValue(double value)
        => this switch
        {
            VBByteValue byteValue => byteValue.WithValue(value),
            VBCurrencyValue currencyValue => currencyValue.WithValue(value),
            VBDecimalValue decimalValue => decimalValue.WithValue(value),
            VBDoubleValue doubleValue => doubleValue.WithValue(value),
            VBIntegerValue integerValue => integerValue.WithValue(value),
            VBLongValue longValue => longValue.WithValue(value),
            VBLongLongValue longLongValue => longLongValue.WithValue(value),
            _ => this with { NumericValue = value },
        };

    public override int GetHashCode() => NumericValue.GetHashCode();
    public virtual bool Equals(VBNumericTypedValue? other) => other != null && other.NumericValue == NumericValue;
}
