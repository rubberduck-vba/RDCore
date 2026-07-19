using System.Runtime.InteropServices;
namespace RDCore.SDK.Model.Values.Interop;

[StructLayout(LayoutKind.Explicit)]
public readonly record struct ManagedBooleanInteropValue : IManagedInteropValue
{
    public ManagedBooleanInteropValue(bool value)
    {
        StoredValue = (short)(value ? -1 : 0);
    }
    public ManagedBooleanInteropValue(short value)
    {
        StoredValue = value;
    }

    [FieldOffset(0)] public readonly short StoredValue;

    public object BoxedValue => StoredValue != 0 ? -1 : 0;

    public static explicit operator bool(ManagedBooleanInteropValue value) => value.StoredValue != 0;
}
