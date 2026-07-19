using System.Runtime.InteropServices;

namespace RDCore.SDK.Model.Values.Interop;

/// <summary>
/// Represents a <c>decimal</c> value using 14 bytes, including a 96-bit numerator.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct ManagedDecimalInteropValue : IManagedInteropValue
{
    public ManagedDecimalInteropValue(decimal value)
    {
        var values = decimal.GetBits(value);
        Numerator1 = values[0];
        Numerator2 = values[1];
        Numerator3 = values[2];
        Denominator = Convert.ToInt16(values[3] >> 16); // lower bits are all zeroes
    }

    public readonly Int32 Numerator1;
    public readonly Int32 Numerator2;
    public readonly Int32 Numerator3;
    public readonly Int16 Denominator;

    public decimal ManagedValue 
    {
        get 
        { 
            return new([Numerator1, Numerator2, Numerator3, (int)(Denominator << 16)]);
        } 
    }
    public object BoxedValue
    {
        get
        {
            return ManagedValue;
        }
    }
}
