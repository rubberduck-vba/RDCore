using System.Diagnostics.CodeAnalysis;
namespace RDCore.SDK.Model.Values.Runtime;

public interface IRuntimeValue
{
    object BoxedValue { get; }
}
public interface IRuntimeValue<T> : IRuntimeValue 
{
    T StoredValue { get; }
}

/// <summary>
/// The managed (.net) representation of a runtime value.
/// </summary>
public readonly struct VBRuntimeValue<T>(T value) : IRuntimeValue<T>, IEquatable<VBRuntimeValue<T>>
{
    public readonly T Value = value;

    public object BoxedValue => Value!;
    public T StoredValue => Value;

    public static VBRuntimeValue<byte> ByteMinValue { get; } = new(byte.MinValue);
    public static VBRuntimeValue<byte> ByteMaxValue { get; } = new(byte.MaxValue);
    public static VBRuntimeValue<byte> ByteZeroValue { get; } = new((byte)0);
    public static VBRuntimeValue<short> Int16MinValue { get; } = new(short.MinValue);
    public static VBRuntimeValue<short> Int16MaxValue { get; } = new(short.MaxValue);
    public static VBRuntimeValue<short> Int16ZeroValue { get; } = new((short)0);
    public static VBRuntimeValue<int> Int32MinValue { get; } = new(int.MinValue);
    public static VBRuntimeValue<int> Int32MaxValue { get; } = new(int.MaxValue);
    public static VBRuntimeValue<int> Int32ZeroValue { get; } = new(0);
    public static VBRuntimeValue<long> Int64MinValue { get; } = new(long.MinValue);
    public static VBRuntimeValue<long> Int64MaxValue { get; } = new(long.MaxValue);
    public static VBRuntimeValue<long> Int64ZeroValue { get; } = new((long)0);
    public static VBRuntimeValue<float> SingleMinValue { get; } = new(float.MinValue);
    public static VBRuntimeValue<float> SingleMaxValue { get; } = new(float.MaxValue);
    public static VBRuntimeValue<float> SingleZeroValue { get; } = new(0f);
    public static VBRuntimeValue<double> DoubleMinValue { get; } = new(double.MinValue);
    public static VBRuntimeValue<double> DoubleMaxValue { get; } = new(double.MaxValue);
    public static VBRuntimeValue<double> DoubleZeroValue { get; } = new(0d);
    public static VBRuntimeValue<VBRuntimeCurrencyValue> CurrencyMinValue { get; } = new(new VBRuntimeCurrencyValue(long.MinValue));
    public static VBRuntimeValue<VBRuntimeCurrencyValue> CurrencyMaxValue { get; } = new(new VBRuntimeCurrencyValue(long.MaxValue));
    public static VBRuntimeValue<VBRuntimeCurrencyValue> CurrencyZeroValue { get; } = new(new VBRuntimeCurrencyValue((long)0));

    public static VBRuntimeValue<VBRuntimeDecimalValue> DecimalMinValue { get; } = new(new VBRuntimeDecimalValue(decimal.MinValue));
    public static VBRuntimeValue<VBRuntimeDecimalValue> DecimalMaxValue { get; } = new(new VBRuntimeDecimalValue(decimal.MaxValue));
    public static VBRuntimeValue<VBRuntimeDecimalValue> DecimalZeroValue { get; } = new(new VBRuntimeDecimalValue(0m));

    public override int GetHashCode() => EqualityComparer<T>.Default.GetHashCode(Value!);

    public override bool Equals([NotNullWhen(true)] object? obj)
        => obj is VBRuntimeValue<T> other && Equals(other);

    public bool Equals(VBRuntimeValue<T> other)
        => EqualityComparer<T>.Default.Equals(StoredValue, other.StoredValue);

    public static bool operator ==(VBRuntimeValue<T> left, VBRuntimeValue<T> right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(VBRuntimeValue<T> left, VBRuntimeValue<T> right)
    {
        return !(left == right);
    }
}
