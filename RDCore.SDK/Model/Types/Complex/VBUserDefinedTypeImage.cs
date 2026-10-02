using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Text;

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

    /// <summary>VBA characters are Unicode, so a fixed-length <c>String</c> field is two bytes each.</summary>
    private static readonly Encoding Characters = Encoding.Unicode;

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
    public static byte[] Of(VBUserDefinedTypeValue value)
    {
        var layout = value.Layout;
        var image = new byte[layout.Size];

        for (var index = 0; index < layout.Fields.Count; index++)
        {
            var field = layout.Fields[index];
            if (value.FieldAt(index) is { } held && field.Symbol.ResolvedType is { } type
                && Encoded(type, held, field.Width) is { } bytes)
            {
                bytes.CopyTo(image.AsSpan(field.Offset, Math.Min(bytes.Length, field.Width)));
            }
        }

        return image;
    }

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
                destination.TrySetFieldAt(index, ReferenceAt(source, sourceLayout, field.Offset, type)
                    ?? type?.DefaultValue ?? VBEmptyValue.Empty);
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
    private static byte[]? Encoded(VBType type, VBTypedValue value, int width) => type switch
    {
        VBFixedStringType => Fixed(Characters.GetBytes(Text(value)), width),
        VBByteType => [Convert.ToByte(Boxed(value) ?? (byte)0)],
        VBBooleanType => BitConverter.GetBytes(Convert.ToBoolean(Boxed(value) ?? false) ? BooleanTrue : (short)0),
        VBIntegerType => BitConverter.GetBytes(Convert.ToInt16(Boxed(value) ?? (short)0)),
        VBLongType or VBErrorType => BitConverter.GetBytes(Convert.ToInt32(Boxed(value) ?? 0)),
        VBLongLongType => BitConverter.GetBytes(Convert.ToInt64(Boxed(value) ?? 0L)),
        VBSingleType => BitConverter.GetBytes(Convert.ToSingle(Boxed(value) ?? 0f)),
        // a Currency is a 64-bit integer of ten-thousandths, which is what makes its arithmetic exact.
        VBCurrencyType => BitConverter.GetBytes(decimal.ToInt64(Convert.ToDecimal(Boxed(value) ?? 0m) * CurrencyScale)),
        VBDateType or VBDoubleType => BitConverter.GetBytes(Convert.ToDouble(Boxed(value) ?? 0d)),
        VBUserDefinedType => value is VBUserDefinedTypeValue nested ? Fixed(Of(nested), width) : new byte[width],
        _ => null,
    };

    private static string Text(VBTypedValue value) => Boxed(value) as string ?? string.Empty;

    private static VBTypedValue Decoded(VBType type, ReadOnlySpan<byte> bytes) => type switch
    {
        VBFixedStringType fixedString => new VBFixedStringValue(fixedString.Length)
            .WithFixedValue(Characters.GetString(bytes)),
        VBByteType => new VBByteValue(bytes[0]),
        VBBooleanType => new VBBooleanValue(BitConverter.ToInt16(bytes) != 0),
        VBIntegerType => new VBIntegerValue(BitConverter.ToInt16(bytes)),
        VBLongType => new VBLongValue(BitConverter.ToInt32(bytes)),
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
