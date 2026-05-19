using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Intrinsic;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Diagnostics;

namespace RDCore.SDK.Model.Values.Abstract;

/// <summary>
/// Represents any data type that is specified as a <em>numeric type</em>, mapping directly to "Any numeric type" specifications.
/// </summary>
/// <param name="TypeInfo"></param>
/// <param name="Symbol"></param>
public abstract record class VBNumericTypedValue(VBType TypeInfo, Symbol Symbol) : VBTypedValue(TypeInfo, Symbol),
    IComparable<INumericValue>, IEquatable<VBNumericTypedValue>, INumericValue
{
    /// <summary>
    /// The maximum possible number of significant digits retained in a String representation of a value of this type.
    /// </summary>
    public const int SignificantIntegerDigits = VBDoubleType.SignificantIntegerDigits;

    public abstract double ManagedValue { get; init; }
    /*
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
    */
    public int CompareTo(INumericValue? other) => other is null ? 1 : ManagedValue.CompareTo(other.ManagedValue);

    public INumericValue WithValue(double value)
        => this switch
        {
            PrecompilerConstantValue constValue => constValue.WithValue(value),
            VBByteValue byteValue => byteValue.WithValue(value),
            VBIntegerValue integerValue => integerValue.WithValue(value),
            VBLongValue longValue => longValue.WithValue(value),
            VBLongLongValue longLongValue => longLongValue.WithValue(value),
            VBSingleValue singleValue => singleValue.WithValue(value),
            VBDoubleValue doubleValue => doubleValue.WithValue(value),
            VBCurrencyValue currencyValue => currencyValue.WithValue(value),
            VBDecimalValue decimalValue => decimalValue.WithValue(value),
            _ => this with { ManagedValue = value },
        };

    public override int GetHashCode() => ManagedValue.GetHashCode();
    public virtual bool Equals(VBNumericTypedValue? other) => other != null && other.ManagedValue == ManagedValue;
}
