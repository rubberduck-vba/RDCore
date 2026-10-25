using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace RDCore.SDK.Model.Types;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// The bytes a <see cref="VBUserDefinedTypeValue"/> occupies in memory, laid out by
/// <see cref="VBUserDefinedTypeLayout"/> — what an <c>LSet</c> between two UDT variables copies
/// (<strong>MS-VBAL §5.4.3.6</strong>).
/// </summary>
/// <remarks>
/// The specification says twice that this is implementation-defined: "the data in <c>expression</c> (as
/// stored in memory <em>in an implementation-defined manner</em>) is copied into <c>bound-variable-expression</c>
/// <em>in an implementation-defined manner</em>". What it does not say, and what every VBA program using
/// <c>LSet</c> on a UDT relies on, is that the copy <em>reinterprets</em>: the source's bytes are laid out by
/// the source's own type and read back through the destination's, which is how VBA fakes a union. So this is
/// a byte image and not a field-by-field assignment — between two UDTs of the same shape the two would agree,
/// and between different shapes only the byte image does what the program meant.
/// <para>
/// 👉 These are <em>memory</em> widths, the ones <c>LenB</c> counts. They are not the widths the same value
/// takes in a record: <see cref="VBUserDefinedTypeLayout"/>'s own remarks say why the two differ, and
/// <c>RecordDataFormat</c> is the other one.
/// </para>
/// </remarks>
public static class VBUserDefinedTypeImage
{
    /// <summary>A <c>Boolean</c> is <c>FF FF</c> when true, in memory as in a record.</summary>
    private const short BooleanTrue = -1;

    /// <summary>VBA scales a <c>Currency</c> by 10,000 and stores the result as a 64-bit integer.</summary>
    private const decimal CurrencyScale = 10000m;

    /// <summary>
    /// The memory image of <paramref name="value"/>: each field's bytes at the offset its layout assigns,
    /// and zeroes in the padding between them.
    /// </summary>
    /// <remarks>
    /// A field with no byte representation of its own — a variable-length <c>String</c>, an object, an array,
    /// a <c>Variant</c> — is a <em>pointer</em> in memory, and a pointer's bytes mean nothing to a process
    /// that did not allocate them. Those bytes are left zero here and the values are carried across by
    /// <see cref="Copy"/> instead, which is the one place the reinterpretation can be done safely.
    /// </remarks>
    /// <param name="value">The value to lay out.</param>
    public static byte[] Of(VBUserDefinedTypeValue value) => Of(value, VBUserDefinedTypeLayout.DefaultPointerWidth);

    /// <summary>
    /// The memory image of <paramref name="value"/>, laid out for a host whose pointers are <paramref name="pointerWidth"/> bytes wide: what the bytes at the
    /// address of a record are (<c>VarPtr</c>), where a 64-bit host's pointer and <c>Variant</c> fields are wider than a 32-bit one's.
    /// </summary>
    /// <param name="value">The value to lay out.</param>
    /// <param name="pointerWidth">The host's pointer width in bytes.</param>
    public static byte[] Of(VBUserDefinedTypeValue value, int pointerWidth)
    {
        var layout = VBUserDefinedTypeLayout.Of(value, pointerWidth);
        var image = new byte[layout.Size];

        for (var index = 0; index < layout.Fields.Count; index++)
        {
            var field = layout.Fields[index];
            if (value.FieldAt(index) is { } held && field.Symbol.ResolvedType is { } type
                && Encoded(type, held, field.Width, pointerWidth) is { } bytes)
            {
                bytes.CopyTo(image.AsSpan(field.Offset, Math.Min(bytes.Length, field.Width)));
            }
        }

        return image;
    }

    /// <summary>
    /// Reads an image back into <paramref name="destination"/>, in place: each field that has bytes of its own takes what the image holds at its offset - what a
    /// <c>POKE</c> at the address of a record changes. A field that is a pointer (a variable-length <c>String</c>, an object, a <c>Variant</c>, an array) keeps
    /// the value it has.
    /// </summary>
    /// <param name="destination">The record written.</param>
    /// <param name="image">The bytes, laid out as <see cref="Of(VBUserDefinedTypeValue, int)"/> lays them out.</param>
    /// <param name="pointerWidth">The host's pointer width in bytes.</param>
    public static void WriteInto(VBUserDefinedTypeValue destination, ReadOnlySpan<byte> image, int pointerWidth)
    {
        var layout = VBUserDefinedTypeLayout.Of(destination, pointerWidth);
        for (var index = 0; index < layout.Fields.Count; index++)
        {
            var field = layout.Fields[index];
            if (field.Symbol.ResolvedType is not { } type || !HasImage(type) || field.Offset + field.Width > image.Length)
            {
                continue;
            }

            var bytes = image.Slice(field.Offset, field.Width);
            if (type is VBUserDefinedType && destination.FieldAt(index) is VBUserDefinedTypeValue nested)
            {
                WriteInto(nested, bytes, pointerWidth);
                continue;
            }

            destination.TrySetFieldAt(index, Decoded(type, bytes));
        }
    }

    /// <summary>
    /// The bytes a value of <paramref name="type"/> occupies in memory, <paramref name="width"/> of them, or <see langword="null"/> when a value of the type is a
    /// pointer to its bytes rather than the bytes (<see cref="HasBytesOfItsOwn"/>): what an element of an array is at its address.
    /// </summary>
    /// <param name="type">The declared type, which says how the value is laid out.</param>
    /// <param name="value">The value.</param>
    /// <param name="width">How many bytes a value of the type occupies (<see cref="VBUserDefinedTypeLayout.WidthOf"/>).</param>
    /// <param name="pointerWidth">The host's pointer width in bytes, which a record's layout depends on.</param>
    public static byte[]? Encode(VBType type, VBTypedValue value, int width, int pointerWidth) => Encoded(type, value, width, pointerWidth);

    /// <summary>
    /// The value of <paramref name="type"/> that <paramref name="bytes"/> are the memory image of, as <see cref="Encode"/> laid it out.
    /// </summary>
    /// <param name="type">The declared type.</param>
    /// <param name="bytes">The bytes.</param>
    public static VBTypedValue Decode(VBType type, ReadOnlySpan<byte> bytes) => Decoded(type, bytes);

    /// <summary>
    /// Whether a value of <paramref name="type"/> is its bytes in memory, rather than a pointer to them: a number, a <c>Boolean</c>, a <c>Date</c>, a
    /// fixed-length <c>String</c>, and a record of such fields are; a variable-length <c>String</c>, an object, a <c>Variant</c> and an array are not.
    /// </summary>
    /// <param name="type">The declared type.</param>
    public static bool HasBytesOfItsOwn(VBType type) => HasImage(type);

    /// <summary>
    /// Whether <paramref name="type"/> has a variable-length <c>String</c> member, directly or in a user-defined type that it has as a member.
    /// </summary>
    /// <remarks>
    /// A fixed-length <c>String</c> member is not one: its characters are in the record. A variable-length one is a pointer, and it is what
    /// makes a byte copy of the record (<c>LSet</c>, <strong>MS-VBAL §5.4.3.6</strong>) unsafe in MS-VBA.
    /// </remarks>
    /// <param name="type">The user-defined type to look into.</param>
    public static bool HoldsVariableLengthString(VBUserDefinedType type)
        => type.Fields().Any(field => field.ResolvedType switch
        {
            // the order matters: a fixed-length String is a VBStringType too.
            VBFixedStringType => false,
            VBStringType => true,
            VBUserDefinedType nested => HoldsVariableLengthString(nested),
            _ => false,
        });

    /// <summary>
    /// Copies <paramref name="source"/> into <paramref name="destination"/> as bytes
    /// (<strong>MS-VBAL §5.4.3.6</strong>), reinterpreting them through the destination's own layout.
    /// </summary>
    /// <remarks>
    /// Only the bytes both types have are copied, as a copy between records of different sizes must be: a
    /// destination field that does not fit entirely inside the source's image is left at its declared type's
    /// default rather than being filled with half a value.
    /// </remarks>
    /// <param name="source">The value read.</param>
    /// <param name="destination">The value written, field by field, in place.</param>
    public static void Copy(VBUserDefinedTypeValue source, VBUserDefinedTypeValue destination)
    {
        var image = Of(source);
        var sourceLayout = source.Layout;
        var layout = destination.Layout;

        for (var index = 0; index < layout.Fields.Count; index++)
        {
            var field = layout.Fields[index];
            var type = field.Symbol.ResolvedType;

            // a field with no byte representation takes the source's own value when the source has a field of
            // the same type at the same offset - the only reading of "copied" that is both meaningful and
            // safe, since the alternative is handing the destination a pointer it does not own. (MS-VBA copies
            // the pointer of a variable-length String, which is what HoldsVariableLengthString is for telling
            // a program about: an analyzer flags it, FixedAssignmentSemanticFlags.)
            if (type is null || !HasImage(type))
            {
                // an array is data: the destination's elements are a copy of the source's, not the source's own cells.
                destination.TrySetFieldAt(index, ReferenceAt(source, sourceLayout, field.Offset, type) switch
                {
                    VBArrayValue array => DataValues.Copy(array),
                    { } held => held,
                    null => type?.DefaultValue ?? VBEmptyValue.Empty,
                });
                continue;
            }

            destination.TrySetFieldAt(index, field.Offset + field.Width <= image.Length
                ? Decoded(type, image.AsSpan(field.Offset, field.Width))
                : type.DefaultValue);
        }
    }

    // the source field sitting at the same offset, when it is of the same declared type. Anything else would
    // be reinterpreting a reference as a different kind of reference, which is the one thing a byte copy
    // cannot be allowed to do.
    private static VBTypedValue? ReferenceAt(
        VBUserDefinedTypeValue source, VBUserDefinedTypeLayout layout, int offset, VBType? type)
    {
        for (var index = 0; index < layout.Fields.Count; index++)
        {
            if (layout.Fields[index].Offset == offset
                && Equals(layout.Fields[index].Symbol.ResolvedType, type))
            {
                return source.FieldAt(index);
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a value of this type has bytes of its own in memory, rather than being a pointer to them.
    /// </summary>
    private static bool HasImage(VBType type) => type switch
    {
        VBFixedStringType => true,
        // the order matters: a fixed-length String is a VBStringType, and a variable-length one is a pointer.
        VBStringType or VBObjectType or VBVariantType or VBArrayType => false,
        VBUserDefinedType nested => nested.Fields().All(field => field.ResolvedType is { } fieldType && HasImage(fieldType)),
        _ => true,
    };

    // by the field's *declared* type, exactly as Decoded reads it back: a cell holds whatever was last
    // assigned to it, and a `String * 4` field assigned an ordinary String still occupies its four characters.
    // The two directions have to agree about the width, and the declared type is the only thing that knows it.
    // A fixed-length String field is the data of its characters, two bytes each: exactly the bytes, a lone surrogate included,
    // since no text encoding is involved in laying a string out in memory.
    private static byte[]? Encoded(VBType type, VBTypedValue value, int width, int pointerWidth = VBUserDefinedTypeLayout.DefaultPointerWidth) => type switch
    {
        VBFixedStringType => Fixed(VBRuntimeByteStringValue.BytesOf(value.Handle.Value), width),
        VBByteType => [Convert.ToByte(Boxed(value) ?? (byte)0)],
        VBBooleanType => BitConverter.GetBytes(Convert.ToBoolean(Boxed(value) ?? false) ? BooleanTrue : (short)0),
        VBIntegerType => BitConverter.GetBytes(Convert.ToInt16(Boxed(value) ?? (short)0)),
        // an Enum field holds a Long (MS-VBAL §5.2.3.4), and its four bytes are the Long's.
        VBLongType or VBErrorType or VBEnumType => BitConverter.GetBytes(Convert.ToInt32(Boxed(value) ?? 0)),
        VBLongLongType => BitConverter.GetBytes(Convert.ToInt64(Boxed(value) ?? 0L)),
        VBSingleType => BitConverter.GetBytes(Convert.ToSingle(Boxed(value) ?? 0f)),
        // a Currency is a 64-bit integer of ten-thousandths, which is what makes its arithmetic exact.
        VBCurrencyType => BitConverter.GetBytes(decimal.ToInt64(Convert.ToDecimal(Boxed(value) ?? 0m) * CurrencyScale)),
        VBDateType or VBDoubleType => BitConverter.GetBytes(Convert.ToDouble(Boxed(value) ?? 0d)),
        VBUserDefinedType => value is VBUserDefinedTypeValue nested ? Fixed(Of(nested, pointerWidth), width) : new byte[width],
        _ => null,
    };

    private static VBTypedValue Decoded(VBType type, ReadOnlySpan<byte> bytes) => type switch
    {
        // the width of a fixed-length String field is two bytes per character, so its data is always whole characters.
        VBFixedStringType fixedString => new VBFixedStringValue(fixedString.Length)
            .WithFixedValue(VBStringValue.FromBytes(bytes).Value),
        VBByteType => new VBByteValue(bytes[0]),
        VBBooleanType => new VBBooleanValue(BitConverter.ToInt16(bytes) != 0),
        VBIntegerType => new VBIntegerValue(BitConverter.ToInt16(bytes)),
        VBLongType or VBEnumType => new VBLongValue(BitConverter.ToInt32(bytes)),
        VBLongLongType => new VBLongLongValue(BitConverter.ToInt64(bytes)),
        VBSingleType => new VBSingleValue(BitConverter.ToSingle(bytes)),
        VBCurrencyType => new VBCurrencyValue(BitConverter.ToInt64(bytes) / CurrencyScale),
        VBDateType => new VBDateValue(BitConverter.ToDouble(bytes)),
        VBDoubleType => new VBDoubleValue(BitConverter.ToDouble(bytes)),
        // a nested record is read back through its own layout, so a copy reinterprets all the way down.
        VBUserDefinedType nested => DecodedRecord(nested, bytes),
        _ => type.DefaultValue,
    };

    private static VBTypedValue DecodedRecord(VBUserDefinedType type, ReadOnlySpan<byte> bytes)
    {
        var record = new VBUserDefinedTypeValue(type);
        var layout = record.Layout;

        for (var index = 0; index < layout.Fields.Count; index++)
        {
            var field = layout.Fields[index];
            if (field.Symbol.ResolvedType is { } fieldType && HasImage(fieldType)
                && field.Offset + field.Width <= bytes.Length)
            {
                record.TrySetFieldAt(index, Decoded(fieldType, bytes.Slice(field.Offset, field.Width)));
            }
        }

        return record;
    }

    // exactly `width` bytes: a value wider than its field is truncated and a narrower one is zero-padded,
    // which is what writing into a fixed-size slot of memory does.
    private static byte[] Fixed(byte[] bytes, int width)
    {
        if (bytes.Length == width)
        {
            return bytes;
        }

        var sized = new byte[width];
        bytes.AsSpan(0, Math.Min(bytes.Length, width)).CopyTo(sized);
        return sized;
    }

    private static object? Boxed(VBTypedValue value) => value.Handle.Value.BoxedValue;
}
