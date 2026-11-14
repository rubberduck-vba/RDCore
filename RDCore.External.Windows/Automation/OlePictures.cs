using RDCore.External.Automation;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;

namespace RDCore.External.Windows.Automation;

/// <summary>
/// The OLE pictures a server is given for the pictures of a session: what <c>LoadPicture</c> makes of the same file in MS-VBA, which a form's control draws.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class OlePictures
{
    private static readonly Guid PictureDispatch = new("7BF80981-BF32-101A-8BBB-00AA00300CAB");
    private const int NoPicture = 0;
    private const uint DefaultColours = 0;

    /// <summary>
    /// The OLE picture of <paramref name="picture"/>: a <c>New StdPicture</c> made as MS-VBA makes one, an empty picture, or the picture of its file at the size of
    /// the image of it that was picked.
    /// </summary>
    /// <param name="picture">The session's picture.</param>
    /// <param name="newPicture">Makes a <c>New StdPicture</c>.</param>
    /// <exception cref="AutomationException">OLE does not read the file as a picture.</exception>
    public static object Of(SessionPicture picture, Func<object> newPicture)
    {
        if (picture.IsNew)
        {
            return newPicture();
        }

        if (picture.Bytes.Length == 0)
        {
            var description = new PictureDescription { Size = Marshal.SizeOf<PictureDescription>(), Type = NoPicture };
            var guid = PictureDispatch;
            return Checked(OleCreatePictureIndirect(ref description, ref guid, true, out var empty), empty);
        }

        Checked(CreateStreamOnHGlobal(IntPtr.Zero, true, out var stream), stream);
        stream.Write(picture.Bytes, picture.Bytes.Length, IntPtr.Zero);
        stream.Seek(0, 0, IntPtr.Zero);
        var iid = PictureDispatch;
        return Checked(OleLoadPictureEx(stream, picture.Bytes.Length, false, ref iid, picture.Width, picture.Height, DefaultColours, out var loaded), loaded);
    }

    private static T Checked<T>(int status, T made)
        => status < 0 ? throw new AutomationException(status, "The picture cannot be made for the server.") : made;

    // PICTDESC: its size, its PICTYPE, and the union of the handles a picture of each type is made of (no more than two pointers).
    [StructLayout(LayoutKind.Sequential)]
    private struct PictureDescription
    {
        public int Size;
        public int Type;
        public IntPtr First;
        public IntPtr Second;
    }

    [DllImport("oleaut32.dll")]
    private static extern int OleCreatePictureIndirect(ref PictureDescription description, ref Guid iid, bool own, [MarshalAs(UnmanagedType.IUnknown)] out object picture);

    [DllImport("oleaut32.dll")]
    private static extern int OleLoadPictureEx(
        IStream stream, int size, bool runMode, ref Guid iid, int width, int height, uint flags, [MarshalAs(UnmanagedType.IUnknown)] out object picture);

    [DllImport("ole32.dll")]
    private static extern int CreateStreamOnHGlobal(IntPtr global, bool deleteOnRelease, out IStream stream);
}
