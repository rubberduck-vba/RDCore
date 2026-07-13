using System.Runtime.InteropServices;
namespace RDCore.SDK.Model.Values.Interop;

/// <summary>
/// Describes a <em>kind</em> of (intrinsic) typed value.
/// </summary>
public enum ManagedInteropValueKind
{
    Boolean,
    Byte,
    Integer,
    Long,
    LongLong,
    Single,
    Double,
    Currency,
    Decimal,
}

/// <summary>
/// The managed (.net) representation of a runtime value.
/// </summary>
/// <remarks>
/// This <em>union</em> defines all its fields at offset 0.
/// </remarks>
[StructLayout(LayoutKind.Explicit)]
public readonly struct ManagedInteropValue
{
    public ManagedInteropValue(bool booleanValue)
    {
        Boolean = booleanValue;
        Kind = ManagedInteropValueKind.Boolean;
    }
    public ManagedInteropValue(byte byteValue)
    {
        Byte = byteValue;
        Kind = ManagedInteropValueKind.Byte;
    }
    public ManagedInteropValue(short integerValue)
    {
        Int16 = integerValue;
        Kind = ManagedInteropValueKind.Integer;
    }
    public ManagedInteropValue(int longValue)
    {
        Int32 = longValue;
        Kind = ManagedInteropValueKind.Long;
    }
    public ManagedInteropValue(long longLongValue)
    {
        Int64 = longLongValue;
        Kind = ManagedInteropValueKind.LongLong;
    }
    public ManagedInteropValue(float singleValue)
    {
        Single = singleValue;
        Kind = ManagedInteropValueKind.Single;
    }
    public ManagedInteropValue(double doubleValue)
    {
        Double = doubleValue;
        Kind = ManagedInteropValueKind.Double;
    }
    public ManagedInteropValue(ManagedCurrencyInteropValue currencyValue)
    {
        Currency = currencyValue;
        Kind = ManagedInteropValueKind.Currency;
    }
    public ManagedInteropValue(ManagedDecimalInteropValue decimalValue)
    {
        Decimal = decimalValue;
        Kind = ManagedInteropValueKind.Decimal;
    }

    /// <summary>
    /// Discriminator that describes the specific type of value being represented.
    /// </summary>
    [FieldOffset(0)] public readonly ManagedInteropValueKind Kind;

    [FieldOffset(1)] public readonly System.Boolean Boolean;
    [FieldOffset(1)] public readonly System.Byte Byte;
    [FieldOffset(1)] public readonly System.Int16 Int16;
    [FieldOffset(1)] public readonly System.Int32 Int32;
    [FieldOffset(1)] public readonly System.Int64 Int64;
    [FieldOffset(1)] public readonly System.Single Single;
    [FieldOffset(1)] public readonly System.Double Double;

    [FieldOffset(1)] public readonly ManagedCurrencyInteropValue? Currency;
    [FieldOffset(1)] public readonly ManagedDecimalInteropValue? Decimal;

    public static ManagedInteropValue BooleanFalse { get; } = new(false);
    public static ManagedInteropValue BooleanTrue { get; } = new(true);
    public static ManagedInteropValue ByteMinValue { get; } = new(byte.MinValue);
    public static ManagedInteropValue ByteMaxValue { get; } = new(byte.MaxValue);
    public static ManagedInteropValue ByteZeroValue { get; } = new((byte)0);
    public static ManagedInteropValue Int16MinValue { get; } = new(short.MinValue);
    public static ManagedInteropValue Int16MaxValue { get; } = new(short.MaxValue);
    public static ManagedInteropValue Int16ZeroValue { get; } = new((short)0);
    public static ManagedInteropValue Int32MinValue { get; } = new(int.MinValue);
    public static ManagedInteropValue Int32MaxValue { get; } = new(int.MaxValue);
    public static ManagedInteropValue Int32ZeroValue { get; } = new(0);
    public static ManagedInteropValue Int64MinValue { get; } = new(long.MinValue);
    public static ManagedInteropValue Int64MaxValue { get; } = new(long.MaxValue);
    public static ManagedInteropValue Int64ZeroValue { get; } = new((long)0);
    public static ManagedInteropValue SingleMinValue { get; } = new(float.MinValue);
    public static ManagedInteropValue SingleMaxValue { get; } = new(float.MaxValue);
    public static ManagedInteropValue SingleZeroValue { get; } = new(0f);
    public static ManagedInteropValue DoubleMinValue { get; } = new(double.MinValue);
    public static ManagedInteropValue DoubleMaxValue { get; } = new(double.MaxValue);
    public static ManagedInteropValue DoubleZeroValue { get; } = new(0d);
    public static ManagedInteropValue CurrencyMinValue { get; } = new(new ManagedCurrencyInteropValue(long.MinValue));
    public static ManagedInteropValue CurrencyMaxValue { get; } = new(new ManagedCurrencyInteropValue(long.MaxValue));
    public static ManagedInteropValue CurrencyZeroValue { get; } = new(new ManagedCurrencyInteropValue((long)0));

    public static ManagedInteropValue DecimalMinValue { get; } = new(new ManagedDecimalInteropValue(decimal.MinValue));
    public static ManagedInteropValue DecimalMaxValue { get; } = new(new ManagedDecimalInteropValue(decimal.MaxValue));
    public static ManagedInteropValue DecimalZeroValue { get; } = new(new ManagedDecimalInteropValue(0m));
}
