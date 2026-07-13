using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
namespace RDCore.SDK.Model.Values.Interop;

[StructLayout(LayoutKind.Explicit)]
public readonly record struct ManagedCurrencyInteropValue
{
    private static readonly int ScaleFactor = 10000;

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
}

[StructLayout(LayoutKind.Explicit)]
public readonly record struct ManagedDecimalInteropValue
{
    public ManagedDecimalInteropValue(decimal storedValue)
    {
        StoredValue = storedValue;
    }

    [FieldOffset(0)] public readonly decimal StoredValue; // FIXME this is a probably-workable but inaccurate representation of a VB Decimal value
}
