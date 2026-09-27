using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;

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
/// elements". This is the second one. The first belongs to whatever writes the record — a <c>Put</c>
/// statement concatenates the fields with no padding at all, so a file is never laid out like memory is.
/// <para>
/// 🎯 The padding is "implementation-specific" by the specification's own word, so RDCore is free to choose
/// it — and chooses MS-VBA's, because a UDT is exactly the kind of thing whose in-memory size a program
/// measures with <c>LenB</c> and then relies on. Each field is aligned to its own natural boundary, and the
/// type as a whole is padded up to the strictest boundary any of its fields asked for.
/// </para>
/// <para>
/// 👉 Widths here are <em>memory</em> widths, which are not always the widths the same value takes in a file:
/// a variable-length <c>String</c> field is a pointer in memory and its characters in a record, and a
/// fixed-length <c>String</c> field is Unicode in memory and ANSI in a record. That is the whole reason
/// <c>Len</c> and <c>LenB</c> disagree about a UDT.
/// </para>
/// </remarks>
public sealed class VBUserDefinedTypeLayout
{
    /// <summary>
    /// The pointer width a layout assumes when the caller does not say — 32-bit, which is the width every
    /// MS-VBA UDT is laid out for, and therefore the one a file or a <c>LenB</c> from MS-VBA agrees with.
    /// </summary>
    /// <remarks>
    /// 🚧 A 64-bit host lays the same declaration out differently (a pointer field, and
    /// <c>LongPtr</c>, both widen). TODO thread the environment's own pointer width through to here, the
    /// way <c>CLngPtr</c>'s declared type already is, once a UDT can be declared in a 64-bit host session.
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

    private VBUserDefinedTypeLayout(IReadOnlyList<VBUserDefinedTypeField> fields, int size, int alignment)
    {
        Fields = fields;
        Size = size;
        Alignment = alignment;
    }

    /// <summary>
    /// Lays <paramref name="type"/> out.
    /// </summary>
    /// <param name="type">The type to lay out.</param>
    /// <param name="pointerWidth">The host's pointer width in bytes; <see cref="DefaultPointerWidth"/>
    /// unless the caller knows better.</param>
    public static VBUserDefinedTypeLayout Of(VBUserDefinedType type, int pointerWidth = DefaultPointerWidth)
    {
        var fields = new List<VBUserDefinedTypeField>();
        var offset = 0;
        var alignment = 1;

        foreach (var field in type.Fields())
        {
            var (width, fieldAlignment) = MeasureOf(field.ResolvedType, pointerWidth, [type]);

            offset = AlignedTo(offset, fieldAlignment);
            fields.Add(new VBUserDefinedTypeField(field, offset, width));

            offset += width;
            alignment = Math.Max(alignment, fieldAlignment);
        }

        // "a linear concatenation of the aggregated data values possibly with implementation defined padding
        // between data values" - and trailing padding too, so that an array of the type keeps every element
        // aligned, which is what makes the type's own size a multiple of its own alignment.
        return new VBUserDefinedTypeLayout(fields, AlignedTo(offset, alignment), alignment);
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
    /// The boundary the type as a whole is aligned to: the strictest any of its fields asked for.
    /// </summary>
    public int Alignment { get; }

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
    private static (int Width, int Alignment) MeasureOf(VBType? type, int pointerWidth, HashSet<VBUserDefinedType> nesting) => type switch
    {
        null => (0, 1),

        VBByteType => (1, 1),
        // a Boolean is two bytes in memory as it is in a record: MS-VBAL 5.4.5.11 writes FF FF or 00 00.
        VBBooleanType or VBIntegerType => (2, 2),
        VBLongType or VBSingleType => (4, 4),
        VBCurrencyType or VBDateType or VBDoubleType or VBLongLongType => (8, 8),
        // sixteen bytes of VARIANT, aligned to eight - the type descriptor and the value it describes.
        VBVariantType or VBDecimalType => (16, 8),

        // in memory a fixed-length String field is its characters inline, and VBA characters are Unicode -
        // which is exactly why LenB disagrees with Len about a UDT holding one, Len counting the ANSI bytes
        // the same field takes in a file.
        VBFixedStringType fixedString => (2 * fixedString.Length, 1),
        // a variable-length String field is a pointer to its characters, never the characters.
        VBStringType or VBObjectType or VBClassType => (pointerWidth, pointerWidth),

        // a fixed-size array field is its elements inline, so its width is theirs and so is its alignment.
        VBFixedSizeArrayType array => MeasureArray(array, pointerWidth, nesting),
        // a resizable array field is a descriptor pointer, like a String.
        VBArrayType => (pointerWidth, pointerWidth),

        VBUserDefinedType nested => MeasureNested(nested, pointerWidth, nesting),

        // an Enum member is a Long data value (MS-VBAL 2.1), and anything this does not know is given a
        // pointer's worth so that the fields after it still land somewhere plausible.
        VBEnumType => (4, 4),
        _ => (pointerWidth, pointerWidth),
    };

    private static (int Width, int Alignment) MeasureArray(
        VBFixedSizeArrayType array, int pointerWidth, HashSet<VBUserDefinedType> nesting)
    {
        var (elementWidth, elementAlignment) = MeasureOf(array.ItemType, pointerWidth, nesting);
        var elements = array.DefaultValue is Values.Intrinsic.VBArrayValue { Length: var length } ? length : 0;
        return (elementWidth * elements, elementAlignment);
    }

    // a nested UDT contributes its own padded size and its own alignment, so a field after it starts on a
    // boundary the nested type would itself have been placed on.
    private static (int Width, int Alignment) MeasureNested(
        VBUserDefinedType nested, int pointerWidth, HashSet<VBUserDefinedType> nesting)
    {
        // VBA forbids a UDT that contains itself by value, but nothing in this model does, and a declaration
        // that does would otherwise recur until the stack ran out. A cycle contributes nothing rather than
        // being measured: there is no finite size it could have.
        if (!nesting.Add(nested))
        {
            return (0, 1);
        }

        var inner = Of(nested, pointerWidth);
        nesting.Remove(nested);
        return (inner.Size, inner.Alignment);
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
