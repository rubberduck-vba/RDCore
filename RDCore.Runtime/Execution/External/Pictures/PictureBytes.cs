using System.Runtime.InteropServices;
using RDCore.External.Automation;
using RDCore.SDK.Runtime.Stdole;

namespace RDCore.Runtime.Execution.External.Pictures;

// a picture's bytes where an array is wanted: the array they are, with no copy, when they are one whole; and what a server's side is given of a picture.
internal static class PictureBytes
{
    public static byte[] ArrayOf(ReadOnlyMemory<byte> bytes)
        => MemoryMarshal.TryGetArray(bytes, out var segment) && segment is { Array: { } array, Offset: 0 } && segment.Count == array.Length
            ? array
            : bytes.ToArray();

    // a form's control on the server's side draws it.
    public static SessionPicture ContentOf(StdPictureObject picture)
        => new(ArrayOf(picture.Bytes), picture.Image?.Width ?? 0, picture.Image?.Height ?? 0, picture.IsNew);
}
