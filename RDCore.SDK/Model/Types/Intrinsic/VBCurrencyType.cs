#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Numerics;

namespace RDCore.SDK.Model.Types;

/// <summary>
/// A <see cref="VBNumericType{Decimal}"/> representing the <c>Currency</c> data type.
/// </summary>
/// <remarks>
/// The <em>managed type</em> of a value of this data type is <c>decimal</c>.<br/>
/// 👉 Implements <see cref="IFixedPointNumericType"/>.
/// </remarks>
public sealed record class VBCurrencyType() : VBNumericType<VBRuntimeCurrencyValue>(VBTypeNames.VBCurrency), IFixedPointNumericType
{
    private static readonly Lazy<VBCurrencyType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// The <c>Currency</c> data type.
    /// </summary>
    public static VBCurrencyType TypeInfo => _instance.Value;

    private static readonly Lazy<VBCurrencyValue> _defaultValue = new(() => VBCurrencyType.Zero, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    private static readonly Lazy<VBCurrencyValue> _minValue = new(() => new VBCurrencyValue(long.MinValue / 10000m), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the minimum representable value for this data type.
    /// </summary>
    public static VBCurrencyValue MinValue => _minValue.Value;
    public override double ManagedMinValue => Convert.ToDouble(VBRuntimeValue<VBRuntimeCurrencyValue>.CurrencyMinValue.Value.Value);

    private static readonly Lazy<VBCurrencyValue> _maxValue = new(() => new VBCurrencyValue(long.MaxValue / 10000m), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the maximum representable value for this data type.
    /// </summary>
    public static VBCurrencyValue MaxValue => _maxValue.Value;
    public override double ManagedMaxValue => Convert.ToDouble(VBRuntimeValue<VBRuntimeCurrencyValue>.CurrencyMaxValue.Value.Value);

    private static readonly Lazy<VBCurrencyValue> _zero = new(() => new VBCurrencyValue(0m), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the value <c>0</c> (zero) representation of this data type.
    /// </summary>
    public static VBCurrencyValue Zero => _zero.Value;



    public override VBNumericTypedValue CreateValue(double value)
        => TenThousandthsOf(value) is { } stored
            ? new VBCurrencyValue(stored / 10000m)
            : throw new ArgumentOutOfRangeException(nameof(value), value, "The value is past the range of a Currency.");

    public override VBTypedValue CreateValue(RDCore.SDK.Model.Values.Bindings.IBindingHandle handle) => new VBCurrencyValue(handle);

    // neither bound is a Double: a Double is within the range when the Currency it makes is.
    public override bool Holds(double value) => TenThousandthsOf(value) is not null;

    // a Double's exact value in ten-thousandths, rounded to the nearest, a tie to the even one: what MS-VBA makes a Currency of - 123456789012.3456 keeps its
    // four decimals, and 922337203685477.5 is within the range, where the Decimal a Double converts to has 15 significant digits; 1.03125 is 1.0312 (recorded in
    // Excel, grid oracle\s52). Nothing when it is past the range, or no number.
    private static long? TenThousandthsOf(double value)
    {
        if (!double.IsFinite(value))
        {
            return null;
        }

        // the value is mantissa * 2^exponent, exactly.
        var bits = BitConverter.DoubleToInt64Bits(value);
        var exponent = (int)((bits >> 52) & 0x7FF);
        var mantissa = bits & 0xF_FFFF_FFFF_FFFF;
        if (exponent == 0)
        {
            exponent = 1;
        }
        else
        {
            mantissa |= 1L << 52;
        }

        exponent -= 1075;
        var scaled = new BigInteger(mantissa) * 10000;
        if (exponent >= 0)
        {
            scaled <<= exponent;
        }
        else
        {
            var denominator = BigInteger.One << -exponent;
            scaled = BigInteger.DivRem(scaled, denominator, out var remainder);
            var twice = remainder * 2;
            if (twice > denominator || (twice == denominator && !scaled.IsEven))
            {
                scaled += 1;
            }
        }

        if (bits < 0)
        {
            scaled = -scaled;
        }

        return scaled >= long.MinValue && scaled <= long.MaxValue ? (long)scaled : null;
    }
}
