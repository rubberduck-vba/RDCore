using RDCore.External.Native;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RDCore.External.Windows.Native;

/// <summary>
/// What a native call needs of Windows: <c>BSTR</c>s and <c>VARIANT</c>s, which OLE Automation allocates, and functions found by their ordinal.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsNativePlatform : INativePlatform
{
    /// <inheritdoc/>
    public bool HasAutomationTypes => true;

    /// <inheritdoc/>
    public int VariantSize => IntPtr.Size == 8 ? 24 : 16;

    /// <inheritdoc/>
    public bool TryGetOrdinal(IntPtr library, int ordinal, out IntPtr function)
    {
        function = GetProcAddressByOrdinal(library, ordinal);
        return function != IntPtr.Zero;
    }

    /// <inheritdoc/>
    public IntPtr AllocateByteString(byte[] bytes) => SysAllocStringByteLen(bytes, (uint)bytes.Length);

    /// <inheritdoc/>
    public byte[] ReadByteString(IntPtr bstr)
    {
        if (bstr == IntPtr.Zero)
        {
            return [];
        }

        var bytes = new byte[SysStringByteLen(bstr)];
        Marshal.Copy(bstr, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <inheritdoc/>
    public void FreeString(IntPtr bstr)
    {
        if (bstr != IntPtr.Zero)
        {
            SysFreeString(bstr);
        }
    }

    /// <inheritdoc/>
    public void WriteVariant(object? value, IntPtr variant) => Marshal.GetNativeVariantForObject(value, variant);

    /// <inheritdoc/>
    public object? ReadVariant(IntPtr variant) => Marshal.GetObjectForNativeVariant(variant);

    /// <inheritdoc/>
    public void ClearVariant(IntPtr variant) => _ = VariantClear(variant);

    [DllImport("kernel32", EntryPoint = "GetProcAddress", ExactSpelling = true)]
    private static extern IntPtr GetProcAddressByOrdinal(IntPtr module, nint ordinal);

    [DllImport("oleaut32", ExactSpelling = true)]
    private static extern IntPtr SysAllocStringByteLen(byte[] bytes, uint length);

    [DllImport("oleaut32", ExactSpelling = true)]
    private static extern void SysFreeString(IntPtr bstr);

    [DllImport("oleaut32", ExactSpelling = true)]
    private static extern uint SysStringByteLen(IntPtr bstr);

    [DllImport("oleaut32", ExactSpelling = true)]
    private static extern int VariantClear(IntPtr variant);
}
