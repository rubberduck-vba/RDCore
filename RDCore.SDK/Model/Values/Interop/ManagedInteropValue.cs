using System.Diagnostics.CodeAnalysis;
namespace RDCore.SDK.Model.Values.Interop;

public interface IManagedInteropValue
{
    object BoxedValue { get; }
}
public interface IManagedInteropValue<T> : IManagedInteropValue 
    where T : struct
{
    T StoredValue { get; }
}

/// <summary>
/// The managed (.net) representation of a runtime value.
/// </summary>
public readonly struct ManagedInteropValue<T>(T value) : IManagedInteropValue<T>, IEquatable<ManagedInteropValue<T>>
    where T : struct
{
    public readonly T Value = value;

    public object BoxedValue => Value;
    public T StoredValue => Value;

    public static ManagedInteropValue<bool> BooleanFalse { get; } = new(false);
    public static ManagedInteropValue<bool> BooleanTrue { get; } = new(true);
    public static ManagedInteropValue<byte> ByteMinValue { get; } = new(byte.MinValue);
    public static ManagedInteropValue<byte> ByteMaxValue { get; } = new(byte.MaxValue);
    public static ManagedInteropValue<byte> ByteZeroValue { get; } = new((byte)0);
    public static ManagedInteropValue<short> Int16MinValue { get; } = new(short.MinValue);
    public static ManagedInteropValue<short> Int16MaxValue { get; } = new(short.MaxValue);
    public static ManagedInteropValue<short> Int16ZeroValue { get; } = new((short)0);
    public static ManagedInteropValue<int> Int32MinValue { get; } = new(int.MinValue);
    public static ManagedInteropValue<int> Int32MaxValue { get; } = new(int.MaxValue);
    public static ManagedInteropValue<int> Int32ZeroValue { get; } = new(0);
    public static ManagedInteropValue<long> Int64MinValue { get; } = new(long.MinValue);
    public static ManagedInteropValue<long> Int64MaxValue { get; } = new(long.MaxValue);
    public static ManagedInteropValue<long> Int64ZeroValue { get; } = new((long)0);
    public static ManagedInteropValue<float> SingleMinValue { get; } = new(float.MinValue);
    public static ManagedInteropValue<float> SingleMaxValue { get; } = new(float.MaxValue);
    public static ManagedInteropValue<float> SingleZeroValue { get; } = new(0f);
    public static ManagedInteropValue<double> DoubleMinValue { get; } = new(double.MinValue);
    public static ManagedInteropValue<double> DoubleMaxValue { get; } = new(double.MaxValue);
    public static ManagedInteropValue<double> DoubleZeroValue { get; } = new(0d);
    public static ManagedInteropValue<ManagedCurrencyInteropValue> CurrencyMinValue { get; } = new(new ManagedCurrencyInteropValue(long.MinValue));
    public static ManagedInteropValue<ManagedCurrencyInteropValue> CurrencyMaxValue { get; } = new(new ManagedCurrencyInteropValue(long.MaxValue));
    public static ManagedInteropValue<ManagedCurrencyInteropValue> CurrencyZeroValue { get; } = new(new ManagedCurrencyInteropValue((long)0));

    public static ManagedInteropValue<ManagedDecimalInteropValue> DecimalMinValue { get; } = new(new ManagedDecimalInteropValue(decimal.MinValue));
    public static ManagedInteropValue<ManagedDecimalInteropValue> DecimalMaxValue { get; } = new(new ManagedDecimalInteropValue(decimal.MaxValue));
    public static ManagedInteropValue<ManagedDecimalInteropValue> DecimalZeroValue { get; } = new(new ManagedDecimalInteropValue(0m));

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        if (obj is ManagedInteropValue<T> other)
        {
            return Equals(other);
        }

        return false;
    }

    public bool Equals(ManagedInteropValue<T> other)
    {
        return other.StoredValue.Equals(StoredValue);
    }

    public static bool operator ==(ManagedInteropValue<T> left, ManagedInteropValue<T> right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ManagedInteropValue<T> left, ManagedInteropValue<T> right)
    {
        return !(left == right);
    }
}
