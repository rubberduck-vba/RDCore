using System.Buffers.Binary;
using System.Text;

namespace RDCore.SDK.Workspace.Forms;

/// <summary>
/// An embedded control of a Microsoft Forms parent control, as its <c>OleSiteConcreteControl</c> describes it
/// ([MS-OFORMS] 2.2.10.12.1).
/// </summary>
/// <param name="Name">The control's name.</param>
/// <param name="Id">The control's ID, which names its own storage (<c>i</c> + ID) when it is a parent control.</param>
/// <param name="TabIndex">The control's position in the tab order; less than zero when it has none.</param>
/// <param name="ClsidCacheIndex">The control's type: an index of the known Microsoft Forms controls when less than
/// <c>0x7FFF</c>, an index into the class table plus <c>0x8000</c> when greater ([MS-OFORMS] 2.5.13).</param>
/// <param name="Depth">How many controls are between this control and the parent control ([MS-OFORMS] 2.2.10.7).</param>
internal sealed record class MSFormsSite(string Name, int Id, short TabIndex, ushort ClsidCacheIndex, int Depth);

/// <summary>
/// What a parent control's form stream says about its embedded controls: their sites, and the class table that names
/// the classes of the controls that are not Microsoft Forms' own.
/// </summary>
/// <param name="Sites">The sites of the embedded controls, in the order they are persisted.</param>
/// <param name="ClassTable">The CLSID of each class table entry, or <c>null</c> for an entry that stores none.</param>
internal sealed record class MSFormsFormStream(IReadOnlyList<MSFormsSite> Sites, IReadOnlyList<Guid?> ClassTable)
{
    // FormPropMask bits ([MS-OFORMS] 2.2.10.2) that store a DataBlock value before BooleanProperties, all 4 bytes
    private const uint FormBackColor = 1u << 1, FormForeColor = 1u << 2, FormNextAvailableId = 1u << 3;
    private const uint FormBooleanProperties = 1u << 6;
    private const uint FormMouseIcon = 1u << 15, FormFont = 1u << 20, FormPicture = 1u << 21;

    // FORM_FLAG_DONTSAVECLASSTABLE ([MS-OFORMS] 2.5.6.1)
    private const uint DontSaveClassTable = 1u << 15;

    private static readonly Guid StdFont = new("0BE35203-8F91-11CE-9DE3-00AA004BB851");
    private static readonly Guid TextProps = new("AFC20920-DA4E-11CE-B943-00AA006887B4");

    /// <summary>
    /// Reads the content of a form stream (<c>f</c>): a <c>FormControl</c> ([MS-OFORMS] 2.2.10.1).
    /// </summary>
    /// <exception cref="FormDesignerFormatException">The stream is not a <c>FormControl</c> of a known version, or ends
    /// before its sites do.</exception>
    public static MSFormsFormStream Read(ReadOnlySpan<byte> stream)
    {
        var cursor = new Cursor(stream);
        var minorVersion = cursor.ReadByte();
        var majorVersion = cursor.ReadByte();
        if (minorVersion != 0 || majorVersion != 4)
        {
            throw new FormDesignerFormatException(FormDesignerFormatProblem.UnsupportedVersion);
        }

        var cbForm = cursor.ReadUInt16();
        var endOfBlocks = cursor.Position + cbForm;
        var propMask = cursor.ReadUInt32();

        // the DataBlock values before BooleanProperties are all 4 bytes and start aligned, so no padding precedes it
        cursor.Skip(4 * (Has(propMask, FormBackColor) + Has(propMask, FormForeColor) + Has(propMask, FormNextAvailableId)));
        var booleanProperties = (propMask & FormBooleanProperties) != 0 ? cursor.ReadUInt32() : 0u;

        // the rest of the DataBlock and the ExtraDataBlock say nothing about the controls: cbForm is their size
        cursor.Seek(endOfBlocks);

        // FormStreamData ([MS-OFORMS] 2.2.10.5): MouseIcon, Font, Picture, when stored
        if ((propMask & FormMouseIcon) != 0)
        {
            SkipGuidAndPicture(ref cursor);
        }

        if ((propMask & FormFont) != 0)
        {
            SkipGuidAndFont(ref cursor);
        }

        if ((propMask & FormPicture) != 0)
        {
            SkipGuidAndPicture(ref cursor);
        }

        // FormSiteData ([MS-OFORMS] 2.2.10.6)
        var classTable = new List<Guid?>();
        if ((booleanProperties & DontSaveClassTable) == 0)
        {
            var countOfSiteClassInfo = cursor.ReadUInt16();
            for (var index = 0; index < countOfSiteClassInfo; index++)
            {
                classTable.Add(ReadSiteClassInfo(ref cursor));
            }
        }

        var countOfSites = cursor.ReadUInt32();
        cursor.ReadUInt32(); // CountOfBytes
        if (countOfSites > stream.Length)
        {
            // every site takes at least a byte of SiteDepthsAndTypes: a count larger than the stream is damage
            throw new FormDesignerFormatException(FormDesignerFormatProblem.Truncated);
        }

        var depths = new List<int>((int)countOfSites);
        var depthsStart = cursor.Position;
        while (depths.Count < countOfSites)
        {
            // FormObjectDepthTypeCount ([MS-OFORMS] 2.2.10.7): a depth, then a type or, with fCount, a count and a type
            var depth = cursor.ReadByte();
            var typeOrCount = cursor.ReadByte();
            var count = 1;
            if ((typeOrCount & 0x80) != 0)
            {
                count = typeOrCount & 0x7F;
                cursor.ReadByte(); // OptionalType
            }

            for (var index = 0; index < count && depths.Count < countOfSites; index++)
            {
                depths.Add(depth);
            }
        }

        cursor.AlignFrom(depthsStart, 4); // ArrayPadding

        var sites = new List<MSFormsSite>((int)countOfSites);
        foreach (var depth in depths)
        {
            sites.Add(ReadSite(ref cursor, depth));
        }

        return new MSFormsFormStream(sites, classTable);
    }

    private static int Has(uint mask, uint bit) => (mask & bit) != 0 ? 1 : 0;

    // GuidAndPicture ([MS-OFORMS] 2.4.8): a GUID, then a StdPicture: a preamble, a size, and that many bytes
    private static void SkipGuidAndPicture(ref Cursor cursor)
    {
        cursor.Skip(16 + 4);
        cursor.Skip((int)Math.Min(cursor.ReadUInt32(), int.MaxValue)); // a size past the stream's end is Truncated
    }

    // GuidAndFont ([MS-OFORMS] 2.4.7): a GUID, then a StdFont (2.4.12) or a TextProps, by that GUID
    private static void SkipGuidAndFont(ref Cursor cursor)
    {
        var fontGuid = cursor.ReadGuid();
        if (fontGuid == StdFont)
        {
            cursor.Skip(1 + 2 + 1 + 2 + 4); // Version, sCharset, bFlags, sWeight, ulHeight
            cursor.Skip(cursor.ReadByte()); // bFaceLen, FontFace
        }
        else if (fontGuid == TextProps)
        {
            cursor.Skip(2); // MinorVersion, MajorVersion
            cursor.Skip(cursor.ReadUInt16()); // cbTextProps, then the TextProps' blocks
        }
        else
        {
            throw new FormDesignerFormatException(FormDesignerFormatProblem.UnsupportedVersion);
        }
    }

    // SiteClassInfo ([MS-OFORMS] 2.2.10.10.1): only its ClsID matters here
    private static Guid? ReadSiteClassInfo(ref Cursor cursor)
    {
        var start = cursor.Position;
        cursor.ReadUInt16(); // Version
        var cbClassTable = cursor.ReadUInt16();
        var end = cursor.Position + cbClassTable;
        var propMask = cursor.ReadUInt32();

        // ClassInfoDataBlock (2.2.10.10.3): its values, in mask order, each aligned to its size from the structure's start
        Guid? classId = null;
        if ((propMask & 1u) != 0)
        {
            var sizes = new (uint Bit, int Size)[]
            {
                (1u << 4, 2), (1u << 4, 2), // fClassFlags: ClassTableFlags, VarFlags
                (1u << 5, 4), (1u << 6, 4), // CountOfMethods, DispidBind
                (1u << 7, 2), (1u << 8, 2), (1u << 9, 2), (1u << 10, 2), (1u << 11, 2), (1u << 12, 2), // GetBindIndex … ValueType
                (1u << 13, 4), (1u << 14, 2), // DispidRowset, SetRowset
            };
            foreach (var (bit, size) in sizes)
            {
                if ((propMask & bit) != 0)
                {
                    cursor.AlignFrom(start, size);
                    cursor.Skip(size);
                }
            }

            cursor.AlignFrom(start, 4);
            classId = cursor.ReadGuid(); // ClassInfoExtraDataBlock.ClsID, its first value
        }

        cursor.Seek(end);
        return classId;
    }

    // OleSiteConcreteControl ([MS-OFORMS] 2.2.10.12.1)
    private static MSFormsSite ReadSite(ref Cursor cursor, int depth)
    {
        var start = cursor.Position;
        cursor.ReadUInt16(); // Version
        var cbSite = cursor.ReadUInt16();
        var end = cursor.Position + cbSite;
        var propMask = cursor.ReadUInt32();

        // SiteDataBlock (2.2.10.12.3), in mask order, each value aligned to its size from the structure's start
        var nameData = 0u;
        var id = 0;
        short tabIndex = -1;
        ushort clsidCacheIndex = 0x7FFF;
        var stringData = new List<uint>();
        for (var bit = 0; bit <= 14; bit++)
        {
            if ((propMask & (1u << bit)) == 0)
            {
                continue;
            }

            switch (bit)
            {
                case 0:
                    cursor.AlignFrom(start, 4);
                    nameData = cursor.ReadUInt32();
                    break;
                case 2:
                    cursor.AlignFrom(start, 4);
                    id = (int)cursor.ReadUInt32();
                    break;
                case 1 or 11 or 12 or 13 or 14:
                    // Tag, ControlTipText, RuntimeLicKey, ControlSource, RowSource: a string's size, its value later
                    cursor.AlignFrom(start, 4);
                    stringData.Add(cursor.ReadUInt32());
                    break;
                case 3 or 4 or 5:
                    // HelpContextID, BitFlags, ObjectStreamSize
                    cursor.AlignFrom(start, 4);
                    cursor.Skip(4);
                    break;
                case 6:
                    cursor.AlignFrom(start, 2);
                    tabIndex = (short)cursor.ReadUInt16();
                    break;
                case 7:
                    cursor.AlignFrom(start, 2);
                    clsidCacheIndex = cursor.ReadUInt16();
                    break;
                case 9:
                    cursor.AlignFrom(start, 2);
                    cursor.Skip(2); // GroupID
                    break;
            }
        }

        cursor.AlignFrom(start, 4);

        // SiteExtraDataBlock (2.2.10.12.4): the Name string comes first
        var name = (propMask & 1u) != 0 ? cursor.ReadString(nameData) : string.Empty;

        cursor.Seek(end);
        return new MSFormsSite(name, id, tabIndex, clsidCacheIndex, depth);
    }

    /// <summary>
    /// A forward-only reader over a stream's bytes, whose every read is bounds-checked: running out of bytes is a
    /// <see cref="FormDesignerFormatProblem.Truncated"/> stream.
    /// </summary>
    private ref struct Cursor(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;

        public int Position { get; private set; }

        public byte ReadByte() => Take(1)[0];

        public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));

        public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

        public Guid ReadGuid() => new(Take(16));

        // an fmString ([MS-OFORMS] 2.4.14.4) of the size its CountOfBytesWithCompressionFlag gives, padded to 4 bytes:
        // compressed, every character is one byte (its high-order byte, zero, removed); uncompressed, UTF-16
        public string ReadString(uint countAndCompression)
        {
            var size = (int)(countAndCompression & 0x7FFFFFFF);
            var compressed = (countAndCompression & 0x80000000) != 0;
            var bytes = Take(size);
            Skip((4 - size % 4) % 4);
            return compressed ? Encoding.Latin1.GetString(bytes) : Encoding.Unicode.GetString(bytes);
        }

        public void Skip(int count) => Take(count);

        public void Seek(int position)
        {
            if (position < Position || position > _data.Length)
            {
                throw new FormDesignerFormatException(FormDesignerFormatProblem.Truncated);
            }

            Position = position;
        }

        public void AlignFrom(int origin, int alignment)
            => Skip((alignment - (Position - origin) % alignment) % alignment);

        private ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || count > _data.Length - Position)
            {
                throw new FormDesignerFormatException(FormDesignerFormatProblem.Truncated);
            }

            var result = _data.Slice(Position, count);
            Position += count;
            return result;
        }
    }
}
