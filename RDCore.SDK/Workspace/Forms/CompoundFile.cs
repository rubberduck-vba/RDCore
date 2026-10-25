using System.Buffers.Binary;
using System.Text;

namespace RDCore.SDK.Workspace.Forms;

/// <summary>
/// The kinds of directory entries of a compound file ([MS-CFB] 2.6.1).
/// </summary>
internal enum CompoundFileEntryType : byte
{
    Unallocated = 0,
    Storage = 1,
    Stream = 2,
    Root = 5,
}

/// <summary>
/// A directory entry of a compound file: a storage, a stream, or the root storage ([MS-CFB] 2.6.1).
/// </summary>
internal sealed record class CompoundFileEntry(
    int Id,
    string Name,
    CompoundFileEntryType Type,
    uint LeftSibling,
    uint RightSibling,
    uint Child,
    uint StartSector,
    long Size);

/// <summary>
/// A read-only reader of compound files, the structured storage OLE objects persist themselves to ([MS-CFB]).
/// </summary>
/// <remarks>
/// Microsoft Forms persists a form to a storage, which the VBE exports to the form's <c>.frx</c> file as a compound file.
/// This reader reads just enough of [MS-CFB] for that: the header, the FAT and its DIFAT, the mini FAT and mini stream, and
/// the directory. It runs on any platform: no OLE32. Its input is untrusted, so every sector chain is bounded by the number
/// of sectors in the file and every directory walk by the number of entries; a damaged file is a
/// <see cref="FormDesignerFormatException"/>, never a hang or an out-of-range read.
/// </remarks>
internal sealed class CompoundFile
{
    private static ReadOnlySpan<byte> Signature => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private const uint MaxRegularSector = 0xFFFFFFFA;
    private const uint EndOfChain = 0xFFFFFFFE;
    private const uint NoStream = 0xFFFFFFFF;
    private const int HeaderDifatEntries = 109;
    private const int DirectoryEntrySize = 128;

    private readonly ReadOnlyMemory<byte> _data;
    private readonly int _sectorSize;
    private readonly int _miniSectorSize;
    private readonly uint _miniStreamCutoff;
    private readonly uint[] _fat;
    private readonly uint[] _miniFat;
    private readonly IReadOnlyList<CompoundFileEntry> _entries;
    private readonly byte[] _miniStream;

    private CompoundFile(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        if (span.Length < 512 || !span[..8].SequenceEqual(Signature))
        {
            throw new FormDesignerFormatException(FormDesignerFormatProblem.NotACompoundFile);
        }

        _data = data;
        var majorVersion = BinaryPrimitives.ReadUInt16LittleEndian(span[26..]);
        var sectorShift = BinaryPrimitives.ReadUInt16LittleEndian(span[30..]);
        var miniSectorShift = BinaryPrimitives.ReadUInt16LittleEndian(span[32..]);
        if (majorVersion is not (3 or 4) || sectorShift != (majorVersion == 3 ? 9 : 12) || miniSectorShift != 6)
        {
            throw new FormDesignerFormatException(FormDesignerFormatProblem.NotACompoundFile);
        }

        _sectorSize = 1 << sectorShift;
        _miniSectorSize = 1 << miniSectorShift;
        _miniStreamCutoff = BinaryPrimitives.ReadUInt32LittleEndian(span[56..]);

        var fatSectorCount = BinaryPrimitives.ReadUInt32LittleEndian(span[44..]);
        var firstDirectorySector = BinaryPrimitives.ReadUInt32LittleEndian(span[48..]);
        var firstMiniFatSector = BinaryPrimitives.ReadUInt32LittleEndian(span[60..]);
        var firstDifatSector = BinaryPrimitives.ReadUInt32LittleEndian(span[68..]);

        _fat = ReadFat(span, fatSectorCount, firstDifatSector);
        _miniFat = firstMiniFatSector == EndOfChain
            ? []
            : ToUInt32s(ReadChain(firstMiniFatSector, long.MaxValue));
        _entries = ReadDirectory(ReadChain(firstDirectorySector, long.MaxValue), majorVersion);

        var root = _entries.Count > 0 && _entries[0].Type == CompoundFileEntryType.Root
            ? _entries[0]
            : throw new FormDesignerFormatException(FormDesignerFormatProblem.CorruptCompoundFile);
        _miniStream = root.StartSector == EndOfChain ? [] : ReadChain(root.StartSector, root.Size);
    }

    /// <summary>
    /// Opens the compound file in <paramref name="data"/>.
    /// </summary>
    /// <exception cref="FormDesignerFormatException"><paramref name="data"/> is not a compound file, or a damaged one.</exception>
    public static CompoundFile Open(ReadOnlyMemory<byte> data) => new(data);

    /// <summary>
    /// The root storage.
    /// </summary>
    public CompoundFileEntry Root => _entries[0];

    /// <summary>
    /// The entries of <paramref name="storage"/>: the red-black tree of siblings under its child ([MS-CFB] 2.6.4), in tree
    /// order.
    /// </summary>
    public IReadOnlyList<CompoundFileEntry> EntriesOf(CompoundFileEntry storage)
    {
        var result = new List<CompoundFileEntry>();
        var visited = new HashSet<uint>();
        var pending = new Stack<uint>();
        var current = storage.Child;
        while (current != NoStream || pending.Count > 0)
        {
            // in-order walk of the sibling tree, bounded: a revisited entry is a cycle, which only a damaged file has
            while (current != NoStream)
            {
                if (current >= _entries.Count || !visited.Add(current))
                {
                    throw new FormDesignerFormatException(FormDesignerFormatProblem.CorruptCompoundFile);
                }

                pending.Push(current);
                current = _entries[(int)current].LeftSibling;
            }

            var entry = _entries[(int)pending.Pop()];
            result.Add(entry);
            current = entry.RightSibling;
        }

        return result;
    }

    /// <summary>
    /// The entry of <paramref name="storage"/> named <paramref name="name"/>, or <c>null</c> when there is none. Names
    /// compare case-insensitively, as [MS-CFB] 2.6.4 orders them.
    /// </summary>
    public CompoundFileEntry? Find(CompoundFileEntry storage, string name)
        => EntriesOf(storage).FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The content of <paramref name="stream"/>.
    /// </summary>
    public byte[] Read(CompoundFileEntry stream)
    {
        if (stream.Type != CompoundFileEntryType.Stream)
        {
            throw new FormDesignerFormatException(FormDesignerFormatProblem.CorruptCompoundFile);
        }

        if (stream.Size == 0)
        {
            return [];
        }

        return stream.Size < _miniStreamCutoff ? ReadMiniChain(stream.StartSector, stream.Size) : ReadChain(stream.StartSector, stream.Size);
    }

    private uint[] ReadFat(ReadOnlySpan<byte> span, uint fatSectorCount, uint firstDifatSector)
    {
        var sectorCount = (uint)((_data.Length - _sectorSize) / _sectorSize);
        if (fatSectorCount > sectorCount)
        {
            throw new FormDesignerFormatException(FormDesignerFormatProblem.CorruptCompoundFile);
        }

        // the FAT's own sectors: the first 109 in the header, the rest in the DIFAT chain ([MS-CFB] 2.5)
        var fatSectors = new List<uint>();
        for (var index = 0; index < HeaderDifatEntries && fatSectors.Count < fatSectorCount; index++)
        {
            fatSectors.Add(BinaryPrimitives.ReadUInt32LittleEndian(span[(76 + index * 4)..]));
        }

        var difatSector = firstDifatSector;
        var entriesPerDifat = _sectorSize / 4 - 1;
        for (var hops = 0u; fatSectors.Count < fatSectorCount; hops++)
        {
            if (difatSector > MaxRegularSector || hops > sectorCount)
            {
                throw new FormDesignerFormatException(FormDesignerFormatProblem.CorruptCompoundFile);
            }

            var difat = Sector(difatSector);
            for (var index = 0; index < entriesPerDifat && fatSectors.Count < fatSectorCount; index++)
            {
                fatSectors.Add(BinaryPrimitives.ReadUInt32LittleEndian(difat[(index * 4)..]));
            }

            difatSector = BinaryPrimitives.ReadUInt32LittleEndian(difat[(entriesPerDifat * 4)..]);
        }

        var fat = new uint[fatSectors.Count * (_sectorSize / 4)];
        for (var index = 0; index < fatSectors.Count; index++)
        {
            var sector = Sector(fatSectors[index]);
            for (var entry = 0; entry < _sectorSize / 4; entry++)
            {
                fat[index * (_sectorSize / 4) + entry] = BinaryPrimitives.ReadUInt32LittleEndian(sector[(entry * 4)..]);
            }
        }

        return fat;
    }

    private ReadOnlySpan<byte> Sector(uint sector)
    {
        var offset = (sector + 1L) * _sectorSize;
        if (sector > MaxRegularSector || offset + _sectorSize > _data.Length)
        {
            throw new FormDesignerFormatException(FormDesignerFormatProblem.CorruptCompoundFile);
        }

        return _data.Span.Slice((int)offset, _sectorSize);
    }

    // the sectors of a FAT chain, up to `size` bytes (long.MaxValue: the whole chain). a chain longer than the FAT has
    // entries loops, which only a damaged file has.
    private byte[] ReadChain(uint start, long size)
    {
        if (size != long.MaxValue && (size < 0 || size > _data.Length))
        {
            throw new FormDesignerFormatException(FormDesignerFormatProblem.CorruptCompoundFile);
        }

        var buffer = new List<byte>();
        var sector = start;
        for (var hops = 0; sector != EndOfChain && buffer.Count < size; hops++)
        {
            if (sector >= _fat.Length || hops > _fat.Length)
            {
                throw new FormDesignerFormatException(FormDesignerFormatProblem.CorruptCompoundFile);
            }

            buffer.AddRange(Sector(sector));
            sector = _fat[sector];
        }

        if (size != long.MaxValue && buffer.Count < size)
        {
            throw new FormDesignerFormatException(FormDesignerFormatProblem.CorruptCompoundFile);
        }

        return size == long.MaxValue ? [.. buffer] : [.. buffer.Take((int)size)];
    }

    private byte[] ReadMiniChain(uint start, long size)
    {
        if (size < 0 || size > _miniStream.Length)
        {
            throw new FormDesignerFormatException(FormDesignerFormatProblem.CorruptCompoundFile);
        }

        var buffer = new byte[size];
        var written = 0;
        var sector = start;
        for (var hops = 0; written < size; hops++)
        {
            var offset = (long)sector * _miniSectorSize;
            if (sector >= _miniFat.Length || hops > _miniFat.Length || offset + _miniSectorSize > _miniStream.Length)
            {
                throw new FormDesignerFormatException(FormDesignerFormatProblem.CorruptCompoundFile);
            }

            var count = (int)Math.Min(_miniSectorSize, size - written);
            _miniStream.AsSpan((int)offset, count).CopyTo(buffer.AsSpan(written));
            written += count;
            sector = _miniFat[sector];
        }

        return buffer;
    }

    private static uint[] ToUInt32s(byte[] bytes)
    {
        var result = new uint[bytes.Length / 4];
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index * 4));
        }

        return result;
    }

    private static List<CompoundFileEntry> ReadDirectory(byte[] directory, ushort majorVersion)
    {
        var entries = new List<CompoundFileEntry>();
        for (var offset = 0; offset + DirectoryEntrySize <= directory.Length; offset += DirectoryEntrySize)
        {
            var entry = directory.AsSpan(offset, DirectoryEntrySize);
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(entry[64..]);
            if (nameLength > 64 || nameLength % 2 != 0)
            {
                throw new FormDesignerFormatException(FormDesignerFormatProblem.CorruptCompoundFile);
            }

            // the length counts the terminating null character; a version 3 file's size has undefined high 32 bits
            var name = nameLength == 0 ? string.Empty : Encoding.Unicode.GetString(entry[..(nameLength - 2)]);
            var size = (long)BinaryPrimitives.ReadUInt64LittleEndian(entry[120..]);
            if (majorVersion == 3)
            {
                size &= 0xFFFFFFFF;
            }

            entries.Add(new CompoundFileEntry(
                entries.Count,
                name,
                (CompoundFileEntryType)entry[66],
                BinaryPrimitives.ReadUInt32LittleEndian(entry[68..]),
                BinaryPrimitives.ReadUInt32LittleEndian(entry[72..]),
                BinaryPrimitives.ReadUInt32LittleEndian(entry[76..]),
                BinaryPrimitives.ReadUInt32LittleEndian(entry[116..]),
                size));
        }

        return entries;
    }
}
