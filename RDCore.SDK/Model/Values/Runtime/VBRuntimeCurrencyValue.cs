using System.Runtime.InteropServices;
namespace RDCore.SDK.Model.Values.Runtime;

[StructLayout(LayoutKind.Explicit)]
public readonly record struct VBRuntimeCurrencyValue : IRuntimeValue
{
    public VBRuntimeCurrencyValue(long storedValue)
    {
        StoredValue = storedValue;
    }
    public VBRuntimeCurrencyValue(decimal scaledValue)
    {
        StoredValue = Convert.ToInt64(scaledValue * ScaleFactor);
    }

    [FieldOffset(0)] public readonly long StoredValue;
 
    /// <summary>
    /// Gets the scaled decimal representation of the stored value.
    /// </summary>
    public decimal Value => (decimal)StoredValue / ScaleFactor;

    /// <summary>
    /// Gets the scale factor.
    /// </summary>
    public static int ScaleFactor => 10000;

    public object BoxedValue => Value;
}
