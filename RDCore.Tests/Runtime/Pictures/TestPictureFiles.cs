using System.Buffers.Binary;

namespace RDCore.Tests.Runtime.Pictures;

// picture files written byte by byte, as a program would find them on disk.
internal static class TestPictureFiles
{
    // a bitmap file: a BITMAPINFOHEADER of the given resolution, bottom-up or top-down, or a BITMAPCOREHEADER; a colour table for 8 bits or fewer; then rows
    // whose padding is 0xEE.
    public static byte[] Bitmap(int width, int height, int bits = 24, int pixelsPerMeter = 3780, bool topDown = false, bool core = false)
    {
        var stride = (width * bits + 31) / 32 * 4;
        var row = (width * bits + 7) / 8;
        var colours = bits <= 8 ? 1 << bits : 0;
        var header = core ? 12 : 40;
        var entry = core ? 3 : 4;
        var offset = 14 + header + colours * entry;
        var bytes = new byte[offset + stride * height];
        "BM"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2), bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10), offset);
        var dib = bytes.AsSpan(14);
        BinaryPrimitives.WriteInt32LittleEndian(dib, header);
        if (core)
        {
            BinaryPrimitives.WriteInt16LittleEndian(dib[4..], (short)width);
            BinaryPrimitives.WriteInt16LittleEndian(dib[6..], (short)height);
            BinaryPrimitives.WriteInt16LittleEndian(dib[8..], 1);
            BinaryPrimitives.WriteInt16LittleEndian(dib[10..], (short)bits);
        }
        else
        {
            BinaryPrimitives.WriteInt32LittleEndian(dib[4..], width);
            BinaryPrimitives.WriteInt32LittleEndian(dib[8..], topDown ? -height : height);
            BinaryPrimitives.WriteInt16LittleEndian(dib[12..], 1);
            BinaryPrimitives.WriteInt16LittleEndian(dib[14..], (short)bits);
            BinaryPrimitives.WriteInt32LittleEndian(dib[20..], stride * height);
            BinaryPrimitives.WriteInt32LittleEndian(dib[24..], pixelsPerMeter);
            BinaryPrimitives.WriteInt32LittleEndian(dib[28..], pixelsPerMeter);
        }

        for (var i = 0; i < colours; i++)
        {
            var at = 14 + header + i * entry;
            bytes[at] = (byte)(i * 37);
            bytes[at + 1] = (byte)(i * 91);
            bytes[at + 2] = (byte)(255 - i);
        }

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < stride; x++)
            {
                bytes[offset + y * stride + x] = x < row ? (byte)((x * 7 + y * 13) & 0xFF) : (byte)0xEE;
            }
        }

        return bytes;
    }

    // an icon (type 1) or a cursor (type 2) of 32-bit images of the given sides, in that order.
    public static byte[] Icon(short type, params int[] sides)
    {
        var images = sides.Select(IconImage).ToArray();
        var header = 6 + 16 * sides.Length;
        var bytes = new byte[header + images.Sum(image => image.Length)];
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(2), type);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(4), (short)sides.Length);
        var offset = header;
        for (var i = 0; i < sides.Length; i++)
        {
            var entry = bytes.AsSpan(6 + 16 * i);
            entry[0] = entry[1] = (byte)sides[i];
            BinaryPrimitives.WriteInt16LittleEndian(entry[4..], 1);
            BinaryPrimitives.WriteInt16LittleEndian(entry[6..], 32);
            BinaryPrimitives.WriteInt32LittleEndian(entry[8..], images[i].Length);
            BinaryPrimitives.WriteInt32LittleEndian(entry[12..], offset);
            images[i].CopyTo(bytes, offset);
            offset += images[i].Length;
        }

        return bytes;
    }

    private static byte[] IconImage(int side)
    {
        var mask = (side + 31) / 32 * 4;
        var bytes = new byte[40 + side * side * 4 + mask * side];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), side);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), side * 2);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(12), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(14), 32);
        return bytes;
    }

    // a metafile: a placeable header with a bounding box at the given units per inch, or none; then a header and its end record.
    public static byte[] Metafile(bool placeable, short right = 1440, short bottom = 720, ushort inch = 1440)
    {
        // its type, header size and version, its size in words, its objects, its largest record and its parameters; then the end record.
        var plain = Convert.FromHexString(string.Concat("0100", "0900", "0003", "0C000000", "0000", "03000000", "0000", "03000000", "0000"));
        if (!placeable)
        {
            return plain;
        }

        var head = new ushort[] { 0xCDD7, 0x9AC6, 0, 0, 0, (ushort)right, (ushort)bottom, inch, 0, 0 };
        var sum = head.Aggregate((ushort)0, (all, word) => (ushort)(all ^ word));
        var bytes = new byte[22 + plain.Length];
        for (var i = 0; i < head.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), head[i]);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), sum);
        plain.CopyTo(bytes, 22);
        return bytes;
    }

    // an enhanced metafile's header record, whose frame is the given size in HIMETRIC units.
    public static byte[] EnhancedMetafile(int width, int height)
    {
        var bytes = new byte[108];
        bytes[0] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 108);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(32), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(36), height);
        " EMF"u8.CopyTo(bytes.AsSpan(40));
        return bytes;
    }

    // the smallest GIF, of the size given in its screen descriptor.
    public static byte[] Gif(short width, short height)
    {
        var bytes = Convert.FromHexString(string.Concat(
            "474946383961", "0100", "0100", "80", "00", "00", "FFFFFF", "000000", "21F904", "01", "0000", "00", "00", "2C", "0000", "0000", "0100", "0100", "00", "02",
            "0244", "0100", "3B"));
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(6), width);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(8), height);
        return bytes;
    }

    // a JPEG's start of image, an APP0 segment, and a baseline start of frame of the given size.
    public static byte[] Jpeg(int width, int height)
    {
        var bytes = Convert.FromHexString("FFD8FFE000104A46494600010100000100010000" + "FFC0001108000000000301220002110103110100");
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(25), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(27), (ushort)width);
        return bytes;
    }

    // a PNG's signature and IHDR chunk.
    public static byte[] Png(int width, int height)
    {
        var bytes = Convert.FromHexString("89504E470D0A1A0A0000000D49484452000000000000000008060000000000000000");
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }
}
