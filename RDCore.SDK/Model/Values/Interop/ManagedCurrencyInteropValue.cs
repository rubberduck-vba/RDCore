using System.Runtime.InteropServices;
namespace RDCore.SDK.Model.Values.Interop;

[StructLayout(LayoutKind.Explicit)]
public readonly record struct ManagedCurrencyInteropValue : IManagedInteropValue
{
    public ManagedCurrencyInteropValue(long storedValue)
    {
        StoredValue = storedValue;
    }
    public ManagedCurrencyInteropValue(decimal scaledValue)
    {
        StoredValue = Convert.ToInt64(scaledValue * ScaleFactor);
    }

    [FieldOffset(0)] public readonly long StoredValue;
 
    /// <summary>
    /// Gets the scaled decimal representation of the stored value.
    /// </summary>
    public decimal Value => StoredValue / ScaleFactor;

    /// <summary>
    /// Gets the scale factor.
    /// </summary>
    public static int ScaleFactor => 10000;

    public object BoxedValue => Value;
}
