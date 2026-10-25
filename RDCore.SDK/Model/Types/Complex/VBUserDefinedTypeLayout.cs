using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace RDCore.SDK.Model.Types;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Where each field of a <see cref="VBUserDefinedType"/> sits in memory, and how much memory the whole
/// type occupies — <strong>MS-VBAL §2.1</strong>'s "linear concatenation of the aggregated data values
/// possibly with implementation defined padding between data values".
/// </summary>
/// <remarks>
/// A UDT has <em>two</em> sizes, and <strong>MS-VBAL §6.1.2.11</strong> says so where it defines <c>Len</c>
/// and <c>LenB</c>: "with user-defined types, <c>Len</c> returns the size as it will be written to the file",
/// while "<c>LenB</c> returns the in-memory size, including any implementation-specific padding between
/// elements". This is the second one, <see cref="Size"/>. MS-VBA's <c>Len</c> is not the size a <c>Put</c> statement
/// writes either, but the fields' memory widths with no padding, <see cref="UnpaddedSize"/>.
/// <para>
/// 🎯 The padding is "implementation-specific" by the specification's own word, so RDCore is free to choose
/// it — and chooses MS-VBA's, because a UDT is exactly the kind of thing whose in-memory size a program
/// measures with <c>LenB</c> and then relies on. Each field is aligned to its own natural boundary, and the
/// type as a whole is padded up to the strictest boundary any of its fields asked for.
/// </para>
/// <para>
/// 👉 Widths here are <em>memory</em> widths, which are not always the widths the same value takes in a file:
/// a variable-length <c>String</c> field is a pointer in memory and its characters in a record, and a
/// fixed-length <c>String</c> field is Unicode in memory and ANSI in a record. <c>Len</c> and <c>LenB</c> disagree
/// about a UDT for the padding, and for a fixed-length <c>String</c> field, which <c>Len</c> counts as its characters.
/// </para>
/// </remarks>
public sealed class VBUserDefinedTypeLayout
{
    /// <summary>
    /// The pointer width a layout assumes when the caller does not say — 32-bit, which is the width every
    /// MS-VBA UDT is laid out for, and therefore the one a file or a <c>LenB</c> from MS-VBA agrees with.
    /// </summary>
    /// <remarks>
    /// 🚧 A 64-bit host lays the same declaration out differently (a pointer field, <c>LongPtr</c> and a
    /// <c>Variant</c> all widen). <c>Len</c> and <c>LenB</c> pass the environment's own width; TODO the record's own
    /// <see cref="VBUserDefinedTypeValue.Size"/> and memory image still lay it out for 32 bits.
    /// </remarks>
    public const int DefaultPointerWidth = 4;

    /// <summary>
    /// The strictest boundary any field is aligned to, whatever its natural alignment would be.
    /// </summary>
    /// <remarks>
    /// An 8-byte boundary, which is what a <c>Double</c>, a <c>Currency</c> and a <c>Date</c> each ask for.
    /// Nothing in a UDT asks for more: a <c>Variant</c> is sixteen bytes of data but is itself aligned to
    /// eight, and a fixed-length <c>String</c> or a <c>Byte</c> to one.
    /// </remarks>
    public const int MaxAlignment = 8;

    private VBUserDefinedTypeLayout(IReadOnlyList<VBUserDefinedTypeField> fields, int size, int unpaddedSize, int alignment)
    {
        Fields = fields;
        Size = size;
        UnpaddedSize = unpaddedSize;
        Alignment = alignment;
    }

    /// <summary>
    /// Lays <paramref name="type"/> out.
    /// </summary>
    /// <param name="type">The type to lay out.</param>
    /// <param name="pointerWidth">The host's pointer width in bytes; <see cref="DefaultPointerWidth"/>
    /// unless the caller knows better.</param>
    /// <remarks>
    /// A type does not know how many elements a fixed-size array field has: its bounds are reduced where a record of it is allocated, and a
    /// field laid out from the type alone has none. <see cref="Of(VBUserDefinedTypeValue, int)"/> lays out a record that has them.
    /// </remarks>
    public static VBUserDefinedTypeLayout Of(VBUserDefinedType type, int pointerWidth = DefaultPointerWidth)
        => Of(type, record: null, pointerWidth, [type]);

    /// <summary>
    /// Lays <paramref name="record"/> out: as its type is, each fixed-size array field as wide as the elements the record's own has, and each
    /// nested record as it is.
    /// </summary>
    /// <param name="record">The record to lay out.</param>
    /// <param name="pointerWidth">The host's pointer width in bytes; <see cref="DefaultPointerWidth"/>
    /// unless the caller knows better.</param>
    public static VBUserDefinedTypeLayout Of(VBUserDefinedTypeValue record, int pointerWidth = DefaultPointerWidth)
        => Of((VBUserDefinedType)record.TypeInfo, record, pointerWidth, [(VBUserDefinedType)record.TypeInfo]);

    private static VBUserDefinedTypeLayout Of(VBUserDefinedType type, VBUserDefinedTypeValue? record, int pointerWidth, HashSet<VBUserDefinedType> nesting)
    {
        var fields = new List<VBUserDefinedTypeField>();
        var offset = 0;
        var unpadded = 0;
        var alignment = 1;

        var index = 0;
        foreach (var field in type.Fields())
        {
            var (width, fieldAlignment, unpaddedWidth) = MeasureOf(field.ResolvedType, record?.StoredFieldAt(index++), pointerWidth, nesting);

            offset = AlignedTo(offset, fieldAlignment);
            fields.Add(new VBUserDefinedTypeField(field, offset, width));

            offset += width;
            unpadded += unpaddedWidth;
            alignment = Math.Max(alignment, fieldAlignment);
        }

        // "a linear concatenation of the aggregated data values possibly with implementation defined padding
        // between data values" - and trailing padding too, so that an array of the type keeps every element
        // aligned, which is what makes the type's own size a multiple of its own alignment.
        return new VBUserDefinedTypeLayout(fields, AlignedTo(offset, alignment), unpadded, alignment);
    }

    /// <summary>
    /// Each field, in declaration order, with the offset it sits at.
    /// </summary>
    public IReadOnlyList<VBUserDefinedTypeField> Fields { get; }

    /// <summary>
    /// The in-memory size of the whole type, padding included — what <c>LenB</c> reports.
    /// </summary>
    public int Size { get; }

    /// <summary>
    /// The fields' widths added up with no padding between or after them, a fixed-length <c>String</c> counted as its characters
    /// and a nested record or an array's elements as their own unpadded size — what <c>Len</c> reports of a record.
    /// </summary>
    /// <remarks>
    /// MS-VBAL §6.1.2.11.1.22 says <c>Len</c> of a record "returns the size as it will be written to the file"; MS-VBA reports this
    /// instead, a variable-length <c>String</c>, an object or a resizable array field counting the pointer it is and not what it
    /// points at, and RD-VBA does as MS-VBA does: <c>Len</c> of a record of a <c>Long</c> and a <c>String</c> is 12 in a 64-bit
    /// host, whatever the <c>String</c> holds.
    /// </remarks>
    public int UnpaddedSize { get; }

    /// <summary>
    /// The boundary the type as a whole is aligned to: the strictest any of its fields asked for.
    /// </summary>
    public int Alignment { get; }

    /// <summary>
    /// How many bytes a value of <paramref name="type"/> occupies in memory, as a field of it would: what an element of an array of the type is wide, each
    /// element being at the address of the one before it plus this (<strong>MS-VBA</strong>, recorded in Excel: 1 for a <c>Byte</c>, 2 for an <c>Integer</c>
    /// or a <c>Boolean</c>, 4 for a <c>Long</c>, 8 for a <c>Double</c> or a <c>Currency</c>, a pointer for a <c>String</c> or an object, a <c>VARIANT</c>
    /// for a <c>Variant</c>).
    /// </summary>
    /// <param name="type">The declared type.</param>
    /// <param name="pointerWidth">The host's pointer width in bytes.</param>
    /// <remarks>
    /// A record is as wide as its type's layout, padding included; one whose fields are fixed-size arrays is as wide as a record of it says
    /// (<see cref="Of(VBUserDefinedTypeValue, int)"/>), which a type alone does not know.
    /// </remarks>
    public static int WidthOf(VBType type, int pointerWidth) => MeasureOf(type, held: null, pointerWidth, []).Width;

    /// <summary>
    /// The offset of the field named <paramref name="name"/>, or <c>null</c> when the type has no such field.
    /// </summary>
    /// <param name="name">The field name, compared the way VBA compares identifiers.</param>
    public int? OffsetOf(string name)
        => Fields.FirstOrDefault(field => field.Symbol.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            is { Symbol: not null } found ? found.Offset : null;

    // the memory width and natural alignment of one field's declared type. A type is measured once, here,
    // rather than by asking its DefaultValue.Size: a default value answers for the value it is and not for
    // the slot a field of that type occupies - vbNullString is the default String and occupies no characters,
    // where a String field is a pointer whether it points at anything or not.
    // A field's value, when there is one, measures what its type cannot: a fixed-size array's elements, and a nested record's own.
    // The unpadded width is the field's share of UnpaddedSize: its width, but for the fields that hold padding or Unicode inline.
    private static (int Width, int Alignment, int UnpaddedWidth) MeasureOf(VBType? type, VBTypedValue? held, int pointerWidth, HashSet<VBUserDefinedType> nesting) => type switch
    {
        null => (0, 1, 0),

        VBByteType => (1, 1, 1),
        // a Boolean is two bytes in memory as it is in a record: MS-VBAL 5.4.5.11 writes FF FF or 00 00.
        VBBooleanType or VBIntegerType => (2, 2, 2),
        VBLongType or VBSingleType => (4, 4, 4),
        VBCurrencyType or VBDateType or VBDoubleType or VBLongLongType => (8, 8, 8),
        // a VARIANT, aligned to eight: sixteen bytes in a 32-bit host and twenty-four in a 64-bit one, as MS-VBA lays it out.
        VBVariantType or VBDecimalType => (VariantWidth(pointerWidth), 8, VariantWidth(pointerWidth)),

        // in memory a fixed-length String field is its characters inline, and VBA characters are Unicode -
        // which is exactly why LenB disagrees with Len about a UDT holding one, Len counting its characters.
        VBFixedStringType fixedString => (2 * fixedString.Length, 1, fixedString.Length),
        // a variable-length String field is a pointer to its characters, never the characters.
        VBStringType or VBObjectType or VBClassType => (pointerWidth, pointerWidth, pointerWidth),

        // a fixed-size array field is its elements inline, so its width is theirs and so is its alignment.
        VBFixedSizeArrayType array => MeasureArray(array, held as VBArrayValue, pointerWidth, nesting),
        // a resizable array field is a descriptor pointer, like a String.
        VBArrayType => (pointerWidth, pointerWidth, pointerWidth),

        VBUserDefinedType nested => MeasureNested(nested, held as VBUserDefinedTypeValue, pointerWidth, nesting),

        // an Enum member is a Long data value (MS-VBAL 2.1), and anything this does not know is given a
        // pointer's worth so that the fields after it still land somewhere plausible.
        VBEnumType => (4, 4, 4),
        _ => (pointerWidth, pointerWidth, pointerWidth),
    };

    private static int VariantWidth(int pointerWidth) => pointerWidth == 8 ? 24 : 16;

    private static (int Width, int Alignment, int UnpaddedWidth) MeasureArray(
        VBFixedSizeArrayType array, VBArrayValue? held, int pointerWidth, HashSet<VBUserDefinedType> nesting)
    {
        // the elements are measured by their type: an element that is a record is as wide as any other of its type.
        var (elementWidth, elementAlignment, unpaddedElementWidth) = MeasureOf(array.ItemType, held: null, pointerWidth, nesting);
        var count = held?.Length ?? 0;
        return (elementWidth * count, elementAlignment, unpaddedElementWidth * count);
    }

    // a nested UDT contributes its own padded size and its own alignment, so a field after it starts on a
    // boundary the nested type would itself have been placed on.
    private static (int Width, int Alignment, int UnpaddedWidth) MeasureNested(
        VBUserDefinedType nested, VBUserDefinedTypeValue? held, int pointerWidth, HashSet<VBUserDefinedType> nesting)
    {
        // VBA forbids a UDT that contains itself by value, but nothing in this model does, and a declaration
        // that does would otherwise recur until the stack ran out. A cycle contributes nothing rather than
        // being measured: there is no finite size it could have.
        if (!nesting.Add(nested))
        {
            return (0, 1, 0);
        }

        var inner = Of(nested, held, pointerWidth, nesting);
        nesting.Remove(nested);
        return (inner.Size, inner.Alignment, inner.UnpaddedSize);
    }

    private static int AlignedTo(int offset, int alignment)
    {
        var boundary = Math.Min(Math.Max(alignment, 1), MaxAlignment);
        var overshoot = offset % boundary;
        return overshoot == 0 ? offset : offset + boundary - overshoot;
    }
}

/// <summary>
/// One field of a <see cref="VBUserDefinedTypeLayout"/>, and where it sits.
/// </summary>
/// <param name="Symbol">The field's own symbol.</param>
/// <param name="Offset">How many bytes from the start of the type the field begins at.</param>
/// <param name="Width">How many bytes the field occupies in memory.</param>
public readonly record struct VBUserDefinedTypeField(VBUserDefinedTypeFieldSymbol Symbol, int Offset, int Width);
