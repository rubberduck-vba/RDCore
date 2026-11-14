using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution.Memory;

/// <summary>
/// The bytes a <see cref="VBUserDefinedTypeValue"/> occupies in memory, laid out by
/// <see cref="VBUserDefinedTypeLayout"/>: what an <c>LSet</c> between two UDT variables copies
/// (<strong>MS-VBAL §5.4.3.6</strong>).
/// </summary>
/// <remarks>
/// The specification says twice that this is implementation-defined: "the data in <c>expression</c> (as
/// stored in memory <em>in an implementation-defined manner</em>) is copied into <c>bound-variable-expression</c>
/// <em>in an implementation-defined manner</em>". What it does not say, and what every VBA program using
/// <c>LSet</c> on a UDT relies on, is that the copy <em>reinterprets</em>: the source's bytes are laid out by
/// the source's own type and read back through the destination's, which is how VBA fakes a union. So this is
/// a byte image and not a field-by-field assignment: between two UDTs of the same shape the two would agree,
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
    /// A field with no byte representation of its own (a variable-length <c>String</c>, an object, an array,
    /// a <c>Variant</c>) is a <em>pointer</em> in memory, and a pointer's bytes mean nothing to a process
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
    public static byte[] Of(VBUserDefinedTypeValue value, int pointerWidth) => Of(value, pointerWidth, hostBytes: null);

    /// <summary>
    /// The memory image of <paramref name="value"/>, laid out for a host whose pointers are <paramref name="pointerWidth"/> bytes wide, with the bytes the host
    /// lays out for each field that is no bytes of its own (<see cref="IsLaidOutByTheHost"/>): a variable-length <c>String</c> the address of its characters
    /// (<c>StrPtr</c>), an object the address of its interface (<c>ObjPtr</c>), a <c>Variant</c> its <c>VARIANT</c>.
    /// </summary>
    /// <param name="value">The value to lay out.</param>
    /// <param name="pointerWidth">The host's pointer width in bytes.</param>
    /// <param name="hostBytes">
    /// The bytes of the field the host lays out that begins at an offset of the image - in a nested record, and an element of a fixed-size array field, the
    /// offset is from the start of the outermost record. <see langword="null"/>, or a field it gives no bytes for, leaves those bytes zero.
    /// </param>
    public static byte[] Of(VBUserDefinedTypeValue value, int pointerWidth, Func<int, byte[]?>? hostBytes)
        => LaidOut(value, pointerWidth, hostBytes is null ? null : (at, _, _) => hostBytes(at));

    // the image, with the bytes the host lays out for a field given by what is at the offset: its declared type and its value.
    private static byte[] LaidOut(VBUserDefinedTypeValue value, int pointerWidth, Func<int, VBType, VBTypedValue, byte[]?>? hostBytes)
    {
        var layout = VBUserDefinedTypeLayout.Of(value, pointerWidth);
        var image = new byte[layout.Size];

        for (var index = 0; index < layout.Fields.Count; index++)
        {
            var field = layout.Fields[index];
            if (field.Symbol.ResolvedType is { } type
                && ImageOf(type, value.FieldAt(index), field.Width, pointerWidth, hostBytes, field.Offset) is { } bytes)
            {
                bytes.AsSpan(0, Math.Min(bytes.Length, field.Width)).CopyTo(image.AsSpan(field.Offset));
            }
        }

        return image;
    }

    /// <summary>
    /// Whether a value of <paramref name="type"/> is in memory what the host lays out, rather than bytes of its own: a variable-length <c>String</c>, an
    /// object and a dynamic array are a pointer, a <c>Variant</c> a <c>VARIANT</c>. A fixed-size array is its elements, side by side.
    /// </summary>
    /// <param name="type">The declared type.</param>
    public static bool IsLaidOutByTheHost(VBType type)
        => type is VBStringType and not VBFixedStringType or VBObjectType or VBClassType or VBVariantType or VBArrayType and not VBFixedSizeArrayType;

    // what a field, or an element of a fixed-size array field, is in memory at an offset of the outermost record: what the host lays out for a pointer or a
    // VARIANT, a record its own fields, a fixed-size array its elements side by side - in the record (oracle\s24 P04) - and anything else as Encoded lays it out.
    private static byte[]? ImageOf(
        VBType type, VBTypedValue? value, int width, int pointerWidth, Func<int, VBType, VBTypedValue, byte[]?>? hostBytes, int at) => type switch
    {
        _ when value is null => null,
        _ when IsLaidOutByTheHost(type) => hostBytes?.Invoke(at, type, value),
        VBUserDefinedType when value is VBUserDefinedTypeValue nested
            => LaidOut(nested, pointerWidth, hostBytes is null ? null : (offset, held, of) => hostBytes(at + offset, held, of)),
        _ when value is VBFixedSizeArrayValue inline => InlineImage(inline, width, pointerWidth, hostBytes, at),
        _ => Encoded(type, value, width, pointerWidth),
    };

    private static byte[] InlineImage(VBFixedSizeArrayValue array, int width, int pointerWidth, Func<int, VBType, VBTypedValue, byte[]?>? hostBytes, int at)
    {
        var image = new byte[width];
        var elementWidth = Math.Max(1, VBUserDefinedTypeLayout.WidthOf(array.ItemType, pointerWidth));
        for (var element = 0; element < array.Length && (element + 1) * elementWidth <= width; element++)
        {
            if (ImageOf(array.ItemType, array.ElementAt(element), elementWidth, pointerWidth, hostBytes, at + element * elementWidth) is { } bytes)
            {
                bytes.AsSpan(0, Math.Min(bytes.Length, elementWidth)).CopyTo(image.AsSpan(element * elementWidth));
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
            if (field.Symbol.ResolvedType is not { } type || field.Offset + field.Width > image.Length)
            {
                continue;
            }

            // a nested record, and a fixed-size array field, take the bytes of what in them has bytes of its own: they are in the record.
            var bytes = image.Slice(field.Offset, field.Width);
            switch (destination.FieldAt(index))
            {
                case VBUserDefinedTypeValue nested when type is VBUserDefinedType:
                    WriteInto(nested, bytes, pointerWidth);
                    continue;
                case VBFixedSizeArrayValue inline:
                    WriteInline(inline, bytes, pointerWidth);
                    continue;
            }

            if (HasImage(type))
            {
                destination.TrySetFieldAt(index, Decoded(type, bytes, pointerWidth));
            }
        }
    }

    private static void WriteInline(VBFixedSizeArrayValue array, ReadOnlySpan<byte> image, int pointerWidth)
    {
        var elementWidth = Math.Max(1, VBUserDefinedTypeLayout.WidthOf(array.ItemType, pointerWidth));
        for (var element = 0; element < array.Length && (element + 1) * elementWidth <= image.Length; element++)
        {
            var bytes = image.Slice(element * elementWidth, elementWidth);
            if (array.ItemType is VBUserDefinedType && array.ElementAt(element) is VBUserDefinedTypeValue record)
            {
                WriteInto(record, bytes, pointerWidth);
            }
            else if (HasImage(array.ItemType))
            {
                array.TrySetElementAt(element, new ValueBindingHandle(Decoded(array.ItemType, bytes, pointerWidth).RuntimeValue));
            }
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
    /// <param name="pointerWidth">The host's pointer width in bytes, which a record's layout depends on.</param>
    public static VBTypedValue Decode(VBType type, ReadOnlySpan<byte> bytes, int pointerWidth) => Decoded(type, bytes, pointerWidth);

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
    /// <para>
    /// The copy is made place by place, at every depth: a field of the destination that is a record is written field by field, and a fixed-size array field
    /// element by element, each at its offset from the start of the record. Every place is written where it is, so a <c>ByRef</c> parameter given one reads
    /// what the copy wrote (<see cref="VBUserDefinedTypeValue.TryLetFieldAt"/>, <see cref="VBArrayValue.TryLetElementAt"/>).
    /// </para>
    /// <para>
    /// Only the bytes both types have are copied, as MS-VBA copies them (observed in Excel): the source's bytes go over the start of the destination, and the
    /// rest of the destination keeps what it held. A place the source's image reaches only part of is its own bytes with the source's laid over their start,
    /// so a <c>Long</c> given the two bytes of an <c>Integer</c> keeps its high two. A place past the end of the source's image is left as it was. An object field the source's image
    /// reaches only part of is set to <c>Nothing</c> (RD-VBAL §5.4.3.6).
    /// </para>
    /// <para>
    /// The specification leaves how the bytes are copied to the implementation. RD-VBA copies the data a place points at: a <c>String</c>, a resizable
    /// array or a <c>Variant</c> of the source at the same offset and of the same type is copied into cells of the destination's own
    /// (<see cref="DataValues.Copy"/>), and an object there gives the destination's object place the object, as <c>Set</c> would. Counting that reference
    /// is the caller's. An object read as the bytes of another type is the address of its interface, what <c>ObjPtr</c> returns, and bytes read as an
    /// object are the object at that address: <c>Nothing</c> for 0. An object place of a class type is given only an object of that class, or of a class
    /// that implements it.
    /// </para>
    /// </remarks>
    /// <param name="source">The value read.</param>
    /// <param name="destination">The value written, place by place, in place.</param>
    /// <param name="pointers">The session's object addresses, and its pointer width, which both records are laid out for.</param>
    /// <param name="isOfClass">
    /// Whether an object is of a class, or of a class that implements it: what <c>Set</c> of the object to a variable of the class requires.
    /// </param>
    /// <returns>
    /// <see cref="VBRecordCopyOutcome.Copied"/>, or why nothing was written: no object has the address the bytes of an object place say, or an object
    /// place of a class type would be given an object of another class.
    /// </returns>
    public static VBRecordCopyOutcome Copy(
        VBUserDefinedTypeValue source, VBUserDefinedTypeValue destination, IObjectPointers pointers, Func<VBRuntimeObjectId, VBClassType, bool> isOfClass)
    {
        var pointerWidth = pointers.PointerWidth;
        var sourceSize = VBUserDefinedTypeLayout.Of(source, pointerWidth).Size;
        var from = PlacesOf(source, pointerWidth);
        var to = PlacesOf(destination, pointerWidth);

        // the destination places the source's bytes give a value, which are all the image is laid out for: an object of the source is given the address of
        // its interface (which ObjPtr would make) only where one of them reads it.
        var read = to.Where(IsReadFromBytes).Select(place => (place.Offset, place.Width)).ToList();
        var image = read.Count == 0 ? [] : LaidOut(source, pointerWidth, AddressBytes);
        byte[]? overlaid = null;

        // each place's value first, then the writes: an object the destination cannot be given leaves it as it was.
        var writes = new List<Action>(to.Count);
        foreach (var place in to)
        {
            var (outcome, write) = WriteOf(place);
            if (outcome is not VBRecordCopyOutcome.Copied)
            {
                return outcome;
            }

            writes.Add(write!);
        }

        foreach (var write in writes)
        {
            write();
        }

        return VBRecordCopyOutcome.Copied;

        bool Fits(Place place) => place.Offset + place.Width <= sourceSize;

        // whether the source's image reaches the place at all: a place past its end is not written.
        bool Reaches(Place place) => place.Offset < sourceSize;

        // an object place reads the bytes when the source has no object at its offset; a place that has bytes of its own always does.
        bool IsReadFromBytes(Place place)
            => Reaches(place) && (IsObject(place.Type) ? ObjectAt(place.Offset) is null : HasImage(place.Type));

        // the destination's own image with the source's laid over its start: what a place the source reaches only part of is read from. It is made before
        // anything is written.
        byte[] Overlaid()
        {
            if (overlaid is null)
            {
                overlaid = LaidOut(destination, pointerWidth, null);
                image.AsSpan(0, Math.Min(image.Length, overlaid.Length)).CopyTo(overlaid);
            }

            return overlaid;
        }

        byte[]? AddressBytes(int at, VBType type, VBTypedValue value)
            => IsObject(type) && value is VBObjectValue held
                && read.Any(range => at < range.Offset + range.Width && range.Offset < at + pointerWidth)
                && pointers.TryGetPointer(held.Value, type, out var pointer)
                ? PointerBytes(pointer, pointerWidth)
                : null;

        // the object a place of an object type of the source holds at the offset, whatever its class.
        VBObjectValue? ObjectAt(int offset)
            => from.FirstOrDefault(place => place.Offset == offset && IsObject(place.Type)) is { Type: not null } held ? held.Value as VBObjectValue : null;

        // the source place at the same offset, when it is of the same declared type. Anything else would be reinterpreting a reference as a different kind
        // of reference, which is the one thing a byte copy cannot be allowed to do.
        VBTypedValue? ReferenceAt(int offset, VBType type)
            => from.FirstOrDefault(place => place.Offset == offset && Equals(place.Type, type)) is { Type: not null } held ? held.Value : null;

        bool IsOfType(VBRuntimeObjectId instance, VBType type) => type is not VBClassType classType || isOfClass(instance, classType);

        (VBRecordCopyOutcome, Action?) WriteOf(Place place)
        {
            var copied = VBRecordCopyOutcome.Copied;
            if (!Reaches(place))
            {
                return (copied, static () => { });
            }

            if (IsObject(place.Type))
            {
                if (ObjectAt(place.Offset) is { } held)
                {
                    return held.IsNothing() || IsOfType(held.Value, place.Type)
                        ? (copied, () => place.Let(DataValues.Copy(held)))
                        : (VBRecordCopyOutcome.ObjectOfAnotherClass, null);
                }

                // part of an address is no object's: the place is Nothing.
                if (!Fits(place))
                {
                    return (copied, place.Reset);
                }

                var address = pointerWidth == sizeof(long)
                    ? BitConverter.ToInt64(image, place.Offset)
                    : BitConverter.ToInt32(image, place.Offset);
                if (address == 0)
                {
                    return (copied, place.Reset);
                }

                if (!pointers.TryGetObject(address, out var instance))
                {
                    return (VBRecordCopyOutcome.NoObjectAtAddress, null);
                }

                return IsOfType(instance, place.Type)
                    ? (copied, () => place.Let(new VBObjectValue(instance)))
                    : (VBRecordCopyOutcome.ObjectOfAnotherClass, null);
            }

            // a place with no bytes of its own is let a copy of the source's place of the same type at the same offset, in cells of its own.
            if (!HasImage(place.Type))
            {
                return ReferenceAt(place.Offset, place.Type) is { } data
                    ? (copied, () => place.Let(DataValues.Copy(data)))
                    : (copied, place.Reset);
            }

            var bytes = Fits(place) ? image : Overlaid();
            var value = Decoded(place.Type, bytes.AsSpan(place.Offset, place.Width), pointerWidth);
            return (copied, () => place.Let(value));
        }
    }

    // a place of a record that holds a value of its own, at its offset from the start of the outermost record: a field that is no record and no fixed-size
    // array, or an element of a fixed-size array field. A field that is a record, and a fixed-size array field, are the places they are made of.
    private readonly record struct Place(int Offset, int Width, VBType Type, VBTypedValue? Value, Action<VBTypedValue> Let, Action Reset);

    private static List<Place> PlacesOf(VBUserDefinedTypeValue record, int pointerWidth)
    {
        var places = new List<Place>();
        AddPlaces(record, pointerWidth, 0, places);
        return places;
    }

    private static void AddPlaces(VBUserDefinedTypeValue record, int pointerWidth, int at, List<Place> places)
    {
        var layout = VBUserDefinedTypeLayout.Of(record, pointerWidth);
        for (var index = 0; index < layout.Fields.Count; index++)
        {
            var field = layout.Fields[index];
            if (field.Symbol.ResolvedType is not { } type)
            {
                continue;
            }

            switch (record.StoredFieldAt(index))
            {
                case VBUserDefinedTypeValue nested when type is VBUserDefinedType:
                    AddPlaces(nested, pointerWidth, at + field.Offset, places);
                    continue;
                case VBFixedSizeArrayValue elements:
                    AddElements(elements, pointerWidth, at + field.Offset, field.Width, places);
                    continue;
            }

            var fieldIndex = index;
            places.Add(new Place(at + field.Offset, field.Width, type, record.FieldAt(index),
                value => record.TryLetFieldAt(fieldIndex, value), () => record.TryResetFieldAt(fieldIndex)));
        }
    }

    // an element that holds a record is the places of that record; any other element is a place, written in its cell.
    private static void AddElements(VBFixedSizeArrayValue array, int pointerWidth, int at, int width, List<Place> places)
    {
        var itemType = array.ItemType;
        var elementWidth = Math.Max(1, VBUserDefinedTypeLayout.WidthOf(itemType, pointerWidth));
        for (var element = 0; element < array.Length && (element + 1) * elementWidth <= width; element++)
        {
            var offset = at + element * elementWidth;
            if (itemType is VBUserDefinedType
                && array.CellAt(element) is { } cell && cell.BindingCapabilities.HasFlag(BindingCapabilities.GetValue)
                && cell.Value is VBRuntimeValue<VBRuntimeUserDefinedTypeValue> { StoredValue.UserDefinedType: { } record })
            {
                AddPlaces(record, pointerWidth, offset, places);
                continue;
            }

            var elementIndex = element;
            places.Add(new Place(offset, elementWidth, itemType, array.ElementAt(element),
                value => array.TryLetElementAt(elementIndex, value), () => array.TryLetElementAt(elementIndex, DataValues.Copy(itemType.DefaultValue))));
        }
    }

    private static bool IsObject(VBType? type) => type is VBObjectType or VBClassType;

    private static byte[] PointerBytes(long pointer, int pointerWidth)
        => pointerWidth == sizeof(long) ? BitConverter.GetBytes(pointer) : BitConverter.GetBytes(unchecked((int)pointer));

    /// <summary>
    /// Whether a value of this type has bytes of its own in memory, rather than being a pointer to them.
    /// </summary>
    private static bool HasImage(VBType type) => type switch
    {
        VBFixedStringType => true,
        // the order matters: a fixed-length String is a VBStringType, and a variable-length one is a pointer.
        VBStringType or VBObjectType or VBClassType or VBVariantType or VBArrayType => false,
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
        VBLongLongType or VBLongPtrType_x64 => BitConverter.GetBytes(Convert.ToInt64(Boxed(value) ?? 0L)),
        VBLongPtrType_x86 => BitConverter.GetBytes(Convert.ToInt32(Boxed(value) ?? 0)),
        VBSingleType => BitConverter.GetBytes(Convert.ToSingle(Boxed(value) ?? 0f)),
        // a Currency is a 64-bit integer of ten-thousandths, which is what makes its arithmetic exact.
        VBCurrencyType => BitConverter.GetBytes(decimal.ToInt64(Convert.ToDecimal(Boxed(value) ?? 0m) * CurrencyScale)),
        VBDateType or VBDoubleType => BitConverter.GetBytes(Convert.ToDouble(Boxed(value) ?? 0d)),
        VBUserDefinedType => value is VBUserDefinedTypeValue nested ? Fixed(Of(nested, pointerWidth), width) : new byte[width],
        _ => null,
    };

    private static VBTypedValue Decoded(VBType type, ReadOnlySpan<byte> bytes, int pointerWidth) => type switch
    {
        // the width of a fixed-length String field is two bytes per character, so its data is always whole characters.
        VBFixedStringType fixedString => new VBFixedStringValue(fixedString.Length)
            .WithFixedValue(VBStringValue.FromBytes(bytes).Value),
        VBByteType => new VBByteValue(bytes[0]),
        VBBooleanType => new VBBooleanValue(BitConverter.ToInt16(bytes) != 0),
        VBIntegerType => new VBIntegerValue(BitConverter.ToInt16(bytes)),
        VBLongType or VBEnumType => new VBLongValue(BitConverter.ToInt32(bytes)),
        VBLongLongType => new VBLongLongValue(BitConverter.ToInt64(bytes)),
        // a LongPtr is the integer a pointer of the host is: eight bytes in a 64-bit host, four in a 32-bit one.
        VBLongPtrType_x64 => new VBLongPtrValue(BitConverter.ToInt64(bytes)),
        VBLongPtrType_x86 => new VBLongPtrValue(BitConverter.ToInt32(bytes)),
        VBSingleType => new VBSingleValue(BitConverter.ToSingle(bytes)),
        VBCurrencyType => new VBCurrencyValue(BitConverter.ToInt64(bytes) / CurrencyScale),
        VBDateType => new VBDateValue(BitConverter.ToDouble(bytes)),
        VBDoubleType => new VBDoubleValue(BitConverter.ToDouble(bytes)),
        // a nested record is read back through its own layout, for the same pointers, so a copy reinterprets all the way down.
        VBUserDefinedType nested => DecodedRecord(nested, bytes, pointerWidth),
        // a cell of its own: the default value of a type is one value every caller shares.
        _ => DataValues.Copy(type.DefaultValue),
    };

    private static VBTypedValue DecodedRecord(VBUserDefinedType type, ReadOnlySpan<byte> bytes, int pointerWidth)
    {
        var record = new VBUserDefinedTypeValue(type);
        var layout = VBUserDefinedTypeLayout.Of(record, pointerWidth);

        for (var index = 0; index < layout.Fields.Count; index++)
        {
            var field = layout.Fields[index];
            if (field.Symbol.ResolvedType is { } fieldType && HasImage(fieldType)
                && field.Offset + field.Width <= bytes.Length)
            {
                record.TrySetFieldAt(index, Decoded(fieldType, bytes.Slice(field.Offset, field.Width), pointerWidth));
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
