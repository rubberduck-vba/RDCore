using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RDCore.External.Windows.Automation;

// The VARIANTs of a call made through IDispatch::Invoke: what the server is given, and what it returns. The runtime's own converter
// (Marshal.GetObjectForNativeVariant) reads most of them as MS-VBA does, and is what is used for those. The ones it reads otherwise are read here: an
// Error (VT_ERROR), which it makes an Int32 that nothing tells from a Long, also as an element of an array of Variants; a Missing argument, which is a
// VT_ERROR of DISP_E_PARAMNOTFOUND; a null BSTR, which MS-VBA reads as ""; and a reference to a VARIANT, which is the VARIANT it refers to.
[SupportedOSPlatform("windows")]
internal static class NativeVariant
{
    // the size of a VARIANT, and of the union of its value, which is where its value starts.
    public static readonly int Size = IntPtr.Size == 8 ? 24 : 16;
    private const int ValueOffset = 8;

    private const ushort Empty = 0;
    private const ushort Bstr = 8;
    private const ushort Dispatch = 9;
    private const ushort Error = 10;
    private const ushort Variant = 12;
    private const ushort Unknown = 13;
    private const ushort ArrayFlag = 0x2000;
    private const ushort ByReference = 0x4000;
    private const ushort TypeMask = 0x0FFF;

    private const int ParameterNotFound = unchecked((int)0x80020004);

    /// <summary>Allocates <paramref name="count"/> VARIANTs, each VT_EMPTY.</summary>
    public static IntPtr Allocate(int count)
    {
        var block = Marshal.AllocCoTaskMem(Math.Max(1, count) * Size);
        for (var index = 0; index < count; index++)
        {
            Clear(block + (index * Size), owned: false);
        }

        return block;
    }

    /// <summary>Writes <paramref name="value"/> to the VARIANT at <paramref name="location"/>, which holds nothing.</summary>
    public static void Write(object? value, IntPtr location) => Marshal.GetNativeVariantForObject(value, location);

    /// <summary>Reads the VARIANT at <paramref name="location"/>, a reference to one read as the VARIANT it refers to.</summary>
    public static object? Read(IntPtr location)
    {
        var kind = (ushort)Marshal.ReadInt16(location);
        if ((kind & ByReference) != 0 && (kind & TypeMask) == Variant && (kind & ArrayFlag) == 0)
        {
            return Read(Marshal.ReadIntPtr(location, ValueOffset));
        }

        var referenced = (kind & ByReference) != 0;
        var data = referenced ? Marshal.ReadIntPtr(location, ValueOffset) : location + ValueOffset;
        switch (kind & ~ByReference)
        {
            case Error:
                var code = Marshal.ReadInt32(data);
                return code == ParameterNotFound ? Missing.Value : new ErrorWrapper(code);

            case Bstr:
                var text = Marshal.ReadIntPtr(data);
                return text == IntPtr.Zero ? string.Empty : Marshal.PtrToStringBSTR(text);

            case Dispatch or Unknown:
                var unknown = Marshal.ReadIntPtr(data);
                return unknown == IntPtr.Zero ? null : Marshal.GetObjectForIUnknown(unknown);

            case ArrayFlag | Variant:
                var array = Marshal.ReadIntPtr(data);
                return array == IntPtr.Zero ? null : ReadArray(array);

            default:
                return Marshal.GetObjectForNativeVariant(location);
        }
    }

    // an array of Variants, each element read as a VARIANT is: its bounds are the SAFEARRAY's, and its elements are stored with the first subscript
    // changing fastest.
    private static Array ReadArray(IntPtr safeArray)
    {
        var rank = (int)SafeArrayGetDim(safeArray);
        var lengths = new int[rank];
        var lowerBounds = new int[rank];
        for (var dimension = 0; dimension < rank; dimension++)
        {
            Check(SafeArrayGetLBound(safeArray, (uint)(dimension + 1), out var lower));
            Check(SafeArrayGetUBound(safeArray, (uint)(dimension + 1), out var upper));
            lowerBounds[dimension] = lower;
            lengths[dimension] = upper - lower + 1;
        }

        var result = Array.CreateInstance(typeof(object), lengths, lowerBounds);
        var count = lengths.Aggregate(1L, (total, length) => total * length);
        if (count == 0)
        {
            return result;
        }

        Check(SafeArrayAccessData(safeArray, out var data));
        try
        {
            var subscripts = (int[])lowerBounds.Clone();
            for (var index = 0L; index < count; index++)
            {
                result.SetValue(Read(data + (int)(index * Size)), subscripts);
                for (var dimension = 0; dimension < rank; dimension++)
                {
                    if (++subscripts[dimension] < lowerBounds[dimension] + lengths[dimension])
                    {
                        break;
                    }

                    subscripts[dimension] = lowerBounds[dimension];
                }
            }
        }
        finally
        {
            _ = SafeArrayUnaccessData(safeArray);
        }

        return result;
    }

    /// <summary>Frees what the VARIANT at <paramref name="location"/> holds, and makes it VT_EMPTY.</summary>
    public static void Clear(IntPtr location, bool owned = true)
    {
        if (owned)
        {
            _ = VariantClear(location);
        }

        Marshal.WriteInt16(location, 0, (short)Empty);
        for (var offset = 2; offset < Size; offset += 2)
        {
            Marshal.WriteInt16(location, offset, 0);
        }
    }

    private static void Check(int status)
    {
        if (status < 0)
        {
            Marshal.ThrowExceptionForHR(status);
        }
    }

    [DllImport("oleaut32.dll")]
    private static extern int VariantClear(IntPtr variant);

    [DllImport("oleaut32.dll")]
    private static extern uint SafeArrayGetDim(IntPtr array);

    [DllImport("oleaut32.dll")]
    private static extern int SafeArrayGetLBound(IntPtr array, uint dimension, out int bound);

    [DllImport("oleaut32.dll")]
    private static extern int SafeArrayGetUBound(IntPtr array, uint dimension, out int bound);

    [DllImport("oleaut32.dll")]
    private static extern int SafeArrayAccessData(IntPtr array, out IntPtr data);

    [DllImport("oleaut32.dll")]
    private static extern int SafeArrayUnaccessData(IntPtr array);
}
