using RDCore.SDK.Workspace.Pictures;
using System.Buffers.Binary;

namespace RDCore.Runtime.Execution.External.Pictures;

/// <summary>
/// What <c>SavePicture</c> writes for a picture, as MS-VBA writes it.
/// </summary>
/// <remarks>
/// A metafile is written as it was read. A bitmap is written again from its pixels: a <c>BITMAPINFOHEADER</c> that states no resolution, its colour table,
/// and its rows from the bottom up, padded with zeros. A bitmap of 32 bits per pixel is written in 24.
/// </remarks>
internal static class SavedPicture
{
    private const int FileHeader = 14;
    private const int InfoHeader = 40;

    /// <summary>
    /// The file <c>SavePicture</c> writes for <paramref name="picture"/>; <see langword="null"/> for a format it does not write.
    /// </summary>
    public static byte[]? BytesOf(StdPictureObject picture) => picture.Format switch
    {
        null => picture.IsNew ? [] : null,
        PictureFormat.Metafile or PictureFormat.EnhancedMetafile => picture.Bytes,
        PictureFormat.Bitmap => Bitmap(picture.Bytes),
        _ => null,
    };

    private const int CoreHeader = 12;

    // a bitmap file of uncompressed pixels, whatever its header: a BITMAPINFOHEADER of its size and depth (24 bits for 32), its colours, and its rows from
    // the bottom up, with the bits past a row's last pixel cleared. Null for compressed pixels, or a file shorter than its header says.
    private static byte[]? Bitmap(byte[] file)
    {
        if (file.Length < FileHeader + CoreHeader)
        {
            return null;
        }

        var dib = file.AsSpan(FileHeader);
        var size = BinaryPrimitives.ReadInt32LittleEndian(dib);
        var core = size == CoreHeader;
        if (!core && (size < InfoHeader || file.Length < FileHeader + InfoHeader || BinaryPrimitives.ReadInt32LittleEndian(dib[16..]) != 0))
        {
            return null;
        }

        var width = core ? BinaryPrimitives.ReadUInt16LittleEndian(dib[4..]) : BinaryPrimitives.ReadInt32LittleEndian(dib[4..]);
        var stated = core ? BinaryPrimitives.ReadUInt16LittleEndian(dib[6..]) : BinaryPrimitives.ReadInt32LittleEndian(dib[8..]);
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(dib[(core ? 10 : 14)..]);
        var used = core ? 0 : BinaryPrimitives.ReadInt32LittleEndian(dib[32..]);
        if (width <= 0 || stated == 0 || bits is not (1 or 4 or 8 or 16 or 24 or 32))
        {
            return null;
        }

        var height = Math.Abs(stated);
        var colours = bits <= 8 ? (used > 0 && used < 1 << bits ? used : 1 << bits) : 0;
        var entry = core ? 3 : 4;
        var stride = Stride(width, bits);
        var pixels = (long)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(10));
        var table = FileHeader + size;
        if (table + (long)colours * entry > file.Length || pixels + (long)stride * height > file.Length)
        {
            return null;
        }

        var savedBits = bits == 32 ? 24 : bits;
        var savedStride = Stride(width, savedBits);
        var offset = FileHeader + InfoHeader + colours * 4;
        var saved = new byte[offset + savedStride * height];
        "BM"u8.CopyTo(saved);
        BinaryPrimitives.WriteInt32LittleEndian(saved.AsSpan(2), saved.Length);
        BinaryPrimitives.WriteInt32LittleEndian(saved.AsSpan(10), offset);

        var header = saved.AsSpan(FileHeader, InfoHeader);
        BinaryPrimitives.WriteInt32LittleEndian(header, InfoHeader);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], width);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], height);
        BinaryPrimitives.WriteUInt16LittleEndian(header[12..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header[14..], (ushort)savedBits);
        BinaryPrimitives.WriteInt32LittleEndian(header[20..], savedStride * height);
        BinaryPrimitives.WriteInt32LittleEndian(header[32..], used);

        for (var i = 0; i < colours; i++)
        {
            file.AsSpan(table + i * entry, 3).CopyTo(saved.AsSpan(FileHeader + InfoHeader + i * 4));
        }

        var row = (int)(((long)width * savedBits + 7) / 8);
        var spare = (int)((long)width * savedBits % 8);
        for (var y = 0; y < height; y++)
        {
            // a top-down bitmap states a negative height: its first row is the top one.
            var source = file.AsSpan((int)(pixels + (long)(stated < 0 ? height - 1 - y : y) * stride), stride);
            var target = saved.AsSpan(offset + y * savedStride, row);
            if (bits == 32)
            {
                for (var x = 0; x < width; x++)
                {
                    source.Slice(x * 4, 3).CopyTo(target[(x * 3)..]);
                }
            }
            else
            {
                source[..row].CopyTo(target);
            }

            if (spare != 0)
            {
                target[^1] &= (byte)(0xFF << (8 - spare));
            }
        }

        return saved;
    }

    private static int Stride(int width, int bits) => (int)(((long)width * bits + 31) / 32 * 4);
}
