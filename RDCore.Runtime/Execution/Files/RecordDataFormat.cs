using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Collections.Immutable;
using System.Text;

namespace RDCore.Runtime.Execution.Files;

/// <summary>
/// <strong>MS-VBAL §5.4.5.11</strong>'s <em>Variant Data File Type Descriptors</em> and <em>Binary File Data
/// Formats</em> tables — the byte-level record format <c>Put</c> writes and <c>Get</c> reads.
/// </summary>
/// <remarks>
/// The one part of file I/O that is a wire format rather than a behaviour, so it is encoded once and both
/// statements go through it: a record written by <c>Put</c> has to be the record <c>Get</c> reads, and a file
/// written by MS-VBA has to be a file this reads, which is why every width and byte order here is the
/// specification's rather than .NET's default.
/// <para>
/// A UDT is written member by member, "in the order in which the members are declared", each by the row of its
/// declared type. The specification has no row for an array member; its format here is MS-VBA's, recorded in Excel.
/// </para>
/// </remarks>
internal static class RecordDataFormat
{
    /// <summary>
    /// The two-byte type descriptor that precedes a <c>Variant</c>'s value in a record.
    /// </summary>
    /// <remarks>
    /// "When outputting a variable whose declared type is Variant, a two byte type descriptor is output before
    /// the actual value of the variable." The second byte is always <c>00</c> in the specification's table.
    /// </remarks>
    private enum VariantDescriptor : byte
    {
        Empty = 0,
        Null = 1,
        Integer = 2,
        Long = 3,
        Single = 4,
        Double = 5,
        Currency = 6,
        Date = 7,
        String = 8,
        Error = 10,
        Boolean = 11,
        // the element types of an array a Variant holds, which the table has no row for: VarType's (MS-VBA, recorded in Excel).
        Variant = 12,
        Decimal = 14,
        Byte = 17,
        LongLong = 20,
    }

    /// <summary>
    /// The second byte of the descriptor of a Variant that holds an array: VarType's <c>vbArray</c>, <c>&amp;H2000</c>.
    /// </summary>
    private const byte ArrayDescriptor = 0x20;

    /// <summary>ANSI, "without NULL termination" — the encoding the format's String rows name.</summary>
    private static readonly Encoding Ansi = Encoding.Latin1;

    private const int DescriptorSize = 2;
    private const int StringLengthPrefixSize = 2;

    /// <summary>
    /// A <c>Boolean</c> is "FF FF" when true and "00 00" otherwise — not <c>01 00</c>, and not one byte.
    /// </summary>
    private const short BooleanTrue = -1;

    /// <summary>A <c>Currency</c> is a 64-bit integer of ten-thousandths, in a file as in memory.</summary>
    private const decimal CurrencyScale = 10000m;

    /// <summary>
    /// Writes <paramref name="value"/> at the stream's current position.
    /// </summary>
    /// <param name="stream">The channel's stream, already positioned.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="mode">The channel's mode, which decides how a <c>String</c> is framed.</param>
    /// <param name="isVariantTarget">Whether the <c>data</c> expression's declared type is <c>Variant</c>, in
    /// which case a type descriptor precedes the value.</param>
    /// <param name="written">How many bytes went to the file, which <c>Put</c> checks against a record length.</param>
    /// <returns><c>false</c> for a value this format has no row for — an object.</returns>
    public static bool TryWrite(
        Stream stream, VBTypedValue value, VBFileMode mode, bool isVariantTarget, out int written)
    {
        written = 0;
        var unwrapped = Unwrapped(value);

        // the descriptor table's Object row is ERROR, and so is its User Defined Type row - but the latter is
        // about a UDT inside a *Variant*, not about a UDT as the data, which the statement itself covers.
        if (unwrapped is VBObjectValue)
        {
            return false;
        }

        // "If <data> is a UDT, then the value of each member of the UDT is written to the file at the current
        // file-pointer-position... in the order in which the members are declared in the UDT" - each by its own
        // row of the format, and with no padding between them, which is what makes Len of a UDT "the size as it
        // will be written to the file" rather than its in-memory size.
        if (unwrapped is VBUserDefinedTypeValue udt)
        {
            return TryWriteFields(stream, udt, mode, out written);
        }

        // an array variable is its elements, each as an element of an array field is; a resizable one of a Random channel has its bounds first, as an
        // array field does, and a fixed-size one, whose bounds are its declaration's, none (MS-VBA, recorded in Excel).
        if (!isVariantTarget && unwrapped is VBArrayValue elements)
        {
            var withBounds = mode is VBFileMode.Random && elements is not VBFixedSizeArrayValue;
            if (!TryWriteArray(stream, elements, elements.ItemType, withBounds, mode, out written))
            {
                return false;
            }

            stream.Flush();
            return true;
        }

        if (!isVariantTarget)
        {
            written += WriteValue(stream, unwrapped, mode);
            stream.Flush();
            return true;
        }

        // a Variant that holds an array is described as its VarType is, vbArray with the element type in the low byte, then is the array with its bounds
        // (MS-VBA, recorded in Excel).
        if (unwrapped is VBArrayValue array)
        {
            if (DescriptorOf(array.ItemType) is not { } elementDescriptor)
            {
                return false;
            }

            stream.WriteByte((byte)elementDescriptor);
            stream.WriteByte(ArrayDescriptor);
            if (!TryWriteArray(stream, array, array.ItemType, withBounds: true, mode, out var arrayBytes))
            {
                return false;
            }

            written = DescriptorSize + arrayBytes;
            stream.Flush();
            return true;
        }

        if (DescriptorOf(unwrapped) is not { } descriptor)
        {
            return false;
        }

        stream.WriteByte((byte)descriptor);
        stream.WriteByte(0);

        // a String a Variant holds has its two-byte length in Binary mode as in Random: the descriptor says it is a String, not how long it is (MS-VBA,
        // recorded in Excel).
        written = DescriptorSize + WriteValue(stream, unwrapped, VBFileMode.Random);
        stream.Flush();
        return true;
    }

    /// <summary>
    /// Reads a value of <paramref name="declaredType"/> from the stream's current position.
    /// </summary>
    /// <param name="stream">The channel's stream, already positioned.</param>
    /// <param name="declaredType">The declared type of the variable being read into, which decides how many
    /// bytes to read — except for a <c>Variant</c>, where the record's own type descriptor decides.</param>
    /// <param name="mode">The channel's mode, which decides how a <c>String</c> is framed.</param>
    /// <param name="currentLength">The length of the variable's current value, which is how many bytes a
    /// <c>String</c> read in <see cref="VBFileMode.Binary"/> takes: "the number of bytes to read is the number
    /// of characters in <c>variable</c>".</param>
    /// <param name="value">The value read.</param>
    /// <returns><c>false</c> for a declared type this format has no row for.</returns>
    public static bool TryRead(
        Stream stream, VBType declaredType, VBFileMode mode, int currentLength, out VBTypedValue? value)
    {
        value = null;
        if (declaredType.Equals(VBVariantType.TypeInfo))
        {
            // "Two bytes are read from the file. These two bytes are the type descriptor for the data value
            // that follows."
            var descriptorBytes = ReadBytes(stream, DescriptorSize);
            if (descriptorBytes[1] == ArrayDescriptor)
            {
                return DescribedType((VariantDescriptor)descriptorBytes[0]) is { } itemType
                    && TryReadResizable(stream, itemType, mode, out value);
            }

            return TryReadDescribed(stream, (VariantDescriptor)descriptorBytes[0], VBFileMode.Random, currentLength, out value);
        }

        // a resizable array of a Random channel is sized to the bounds its record gives, whatever it was sized to before (MS-VBA, recorded in Excel); any
        // other array is read into (TryReadInto).
        if (declaredType is VBArrayType arrayType)
        {
            return mode is VBFileMode.Random && declaredType is not VBFixedSizeArrayType && TryReadResizable(stream, arrayType.ItemType, mode, out value);
        }

        return TryReadTyped(stream, declaredType, mode, currentLength, out value);
    }

    /// <summary>
    /// Reads one record into <paramref name="array"/>'s elements, the first subscript varying fastest, replacing each (<strong>MS-VBAL §5.4.5.12</strong>).
    /// </summary>
    /// <remarks>
    /// The array's own bounds say how many elements the record holds, which is how MS-VBA reads a fixed-size array, and a resizable one of a Binary
    /// channel. An array with no elements, such as a resizable one that was never sized, reads nothing (recorded in Excel).
    /// </remarks>
    /// <param name="stream">The channel's stream, already positioned.</param>
    /// <param name="array">The array whose elements are replaced.</param>
    /// <param name="mode">The channel's mode.</param>
    /// <returns><c>false</c> for an element type the format has no row for.</returns>
    public static bool TryReadInto(Stream stream, VBArrayValue array, VBFileMode mode)
        => TryReadElements(stream, array.ItemType, array, mode);

    /// <summary>
    /// Reads one record into <paramref name="udt"/>'s fields, in declaration order, replacing each
    /// (<strong>MS-VBAL §5.4.5.12</strong>).
    /// </summary>
    /// <remarks>
    /// Reads <em>into</em> the value rather than returning a new one, which is what a <c>Get</c> of a UDT
    /// does: the variable keeps its identity, and its fields take the record's values. A field this cannot
    /// read leaves the whole call <c>false</c> — with the fields before it already replaced, because the
    /// bytes before it have already been consumed and there is no meaningful way to put either back.
    /// </remarks>
    /// <param name="stream">The channel's stream, already positioned.</param>
    /// <param name="udt">The UDT value whose fields are replaced.</param>
    /// <param name="mode">The channel's mode, which decides how a <c>String</c> field is framed.</param>
    /// <returns><c>false</c> for a field whose type the format has no row for.</returns>
    public static bool TryReadInto(Stream stream, VBUserDefinedTypeValue udt, VBFileMode mode)
    {
        for (var index = 0; index < udt.Fields.Length; index++)
        {
            // a nested record and a fixed-size array are read into in place, so they keep their identity the way the outer record does; any other field
            // is let what was read in place (TryLetFieldAt): a ByRef parameter given it is bound to its cell (MS-VBAL §5.3.1.11).
            var current = udt.FieldAt(index);
            if (!TryReadMember(stream, udt.Fields[index].ResolvedType ?? VBVariantType.TypeInfo, current, mode, out var value)
                || (!ReferenceEquals(value, current) && !udt.TryLetFieldAt(index, value!)))
            {
                return false;
            }
        }

        return true;
    }

    // the write side of the same walk. Each field is written by the row of its declared type, not of the value it holds: a Variant field has its type
    // descriptor whatever it was last given, a String * n field is its characters and nothing else, and an array field is its elements.
    private static bool TryWriteFields(Stream stream, VBUserDefinedTypeValue udt, VBFileMode mode, out int written)
    {
        written = 0;
        for (var index = 0; index < udt.Fields.Length; index++)
        {
            if (udt.FieldAt(index) is not { } field
                || !TryWriteMember(stream, udt.Fields[index].ResolvedType ?? VBVariantType.TypeInfo, field, mode, out var fieldBytes))
            {
                return false;
            }

            written += fieldBytes;
        }

        return true;
    }

    // a member of a record (a field, or an element of an array field) as MS-VBA writes it (recorded in Excel): a String has its two-byte length in
    // Binary mode as in Random, since nothing else in the record says where it ends; a String * n is padded with spaces to its length, or cut to it; a
    // fixed-size array is its elements, the first subscript varying fastest; a resizable array is its number of dimensions in two bytes, then the element
    // count and lower bound of each in four bytes each, then its elements, and only the two bytes when it has none.
    private static bool TryWriteMember(Stream stream, VBType declared, VBTypedValue value, VBFileMode mode, out int written)
    {
        written = 0;
        switch (declared)
        {
            case VBVariantType:
                return TryWrite(stream, value, mode, isVariantTarget: true, out written);

            case VBFixedStringType fixedString when Unwrapped(value) is VBStringValue text:
                written = WriteBytes(stream, Ansi.GetBytes(Text(text).PadRight(fixedString.Length)[..fixedString.Length]));
                return true;

            case VBStringType when Unwrapped(value) is VBStringValue text:
                written = WriteString(stream, Text(text), VBFileMode.Random);
                return true;

            case VBArrayType arrayType when Unwrapped(value) is VBArrayValue array:
                return TryWriteArray(stream, array, arrayType.ItemType, withBounds: declared is not VBFixedSizeArrayType, mode, out written);

            default:
                return TryWrite(stream, value, mode, isVariantTarget: false, out written);
        }
    }

    // the bounds come last dimension first, as a SAFEARRAY lists them; the elements first subscript fastest, the order the array holds them in.
    private static bool TryWriteArray(Stream stream, VBArrayValue array, VBType itemType, bool withBounds, VBFileMode mode, out int written)
    {
        written = 0;
        if (withBounds)
        {
            var dimensions = array.IsInitialized ? array.Dimensions : ImmutableArray<VBArrayValue.VBArrayDimension>.Empty;
            written += WriteBytes(stream, BitConverter.GetBytes((short)dimensions.Length));
            for (var dimension = dimensions.Length - 1; dimension >= 0; dimension--)
            {
                written += WriteBytes(stream, BitConverter.GetBytes(dimensions[dimension].Length));
                written += WriteBytes(stream, BitConverter.GetBytes(dimensions[dimension].LowerBound));
            }
        }

        for (var index = 0; index < (array.IsInitialized ? array.Length : 0); index++)
        {
            if (!TryWriteMember(stream, itemType, array.ElementAt(index) ?? itemType.DefaultValue, mode, out var elementBytes))
            {
                return false;
            }

            written += elementBytes;
        }

        return true;
    }

    // the read side of TryWriteMember. A record, and a fixed-size array, is read into the value the member holds, which is what value is then; a resizable
    // array is a new one of the bounds the record gives, or one with none.
    private static bool TryReadMember(Stream stream, VBType declared, VBTypedValue? current, VBFileMode mode, out VBTypedValue? value)
    {
        value = null;
        switch (declared)
        {
            case VBUserDefinedType when current is VBUserDefinedTypeValue record:
                value = record;
                return TryReadInto(stream, record, mode);

            // a Variant member is a Variant still, whatever it was read as: the next Put writes its descriptor.
            case VBVariantType:
                if (!TryRead(stream, declared, mode, 0, out var held))
                {
                    return false;
                }

                value = held is VBVariantValue ? held : new VBVariantValue(held!);
                return true;

            case VBFixedStringType:
                return TryReadTyped(stream, declared, mode, 0, out value);

            case VBStringType:
                value = ReadString(stream, VBFileMode.Random, 0);
                return true;

            case VBFixedSizeArrayType fixedArray when current is VBArrayValue elements:
                value = elements;
                return TryReadElements(stream, fixedArray.ItemType, elements, mode);

            case VBArrayType arrayType:
                return TryReadResizable(stream, arrayType.ItemType, mode, out value);

            default:
                return TryReadTyped(stream, declared, mode, 0, out value);
        }
    }

    private static bool TryReadResizable(Stream stream, VBType itemType, VBFileMode mode, out VBTypedValue? value)
    {
        value = null;
        var rank = BitConverter.ToInt16(ReadBytes(stream, sizeof(short)));
        if (rank < 0)
        {
            return false;
        }

        var dimensions = new (int, int)[rank];
        for (var dimension = dimensions.Length - 1; dimension >= 0; dimension--)
        {
            var bounds = ReadBytes(stream, 2 * sizeof(int));
            var lowerBound = BitConverter.ToInt32(bounds, sizeof(int));
            dimensions[dimension] = (lowerBound, lowerBound + BitConverter.ToInt32(bounds, 0) - 1);
        }

        var cells = ElementCells.Of(itemType);
        VBArrayValue array = itemType is VBByteType
            ? new VBResizableByteArrayValue(dimensions, cells)
            : new VBResizableArrayValue(dimensions, itemType, elementCell: cells);
        value = array;
        return TryReadElements(stream, itemType, array, mode);
    }

    private static bool TryReadElements(Stream stream, VBType itemType, VBArrayValue array, VBFileMode mode)
    {
        for (var index = 0; index < (array.IsInitialized ? array.Length : 0); index++)
        {
            var current = array.ElementAt(index);
            if (!TryReadMember(stream, itemType, current, mode, out var element)
                || (!ReferenceEquals(element, current) && !array.TryLetElementAt(index, element!)))
            {
                return false;
            }
        }

        return true;
    }

    private static int WriteValue(Stream stream, VBTypedValue value, VBFileMode mode) => value switch
    {
        VBFixedStringValue text => WriteBytes(stream, Ansi.GetBytes(Text(text))),
        VBStringValue text => WriteString(stream, Text(text), mode),
        VBBooleanValue boolean => WriteBytes(stream, BitConverter.GetBytes(
            Convert.ToBoolean(boolean.Handle.Value.BoxedValue) ? BooleanTrue : (short)0)),
        VBByteValue @byte => WriteBytes(stream, [Convert.ToByte(@byte.Handle.Value.BoxedValue)]),
        VBIntegerValue integer => WriteBytes(stream, BitConverter.GetBytes(Convert.ToInt16(Boxed(integer)))),
        VBLongValue @long => WriteBytes(stream, BitConverter.GetBytes(Convert.ToInt32(Boxed(@long)))),
        // "The value of the error code. See HRESULT in [MS-DTYP]" - four bytes, like a Long.
        VBErrorValue error => WriteBytes(stream, BitConverter.GetBytes(Convert.ToInt32(Boxed(error)))),
        VBLongLongValue longLong => WriteBytes(stream, BitConverter.GetBytes(Convert.ToInt64(Boxed(longLong)))),
        VBSingleValue single => WriteBytes(stream, BitConverter.GetBytes(Convert.ToSingle(Boxed(single)))),
        // Currency and Date are eight bytes each: the one a 64-bit integer of ten-thousandths (1.5 is 98 3A 00 00 00 00 00 00), the other an OLE
        // Automation serial, a Double.
        VBCurrencyValue currency => WriteBytes(stream, BitConverter.GetBytes(decimal.ToInt64(Convert.ToDecimal(Boxed(currency)) * CurrencyScale))),
        VBDateValue date => WriteBytes(stream, BitConverter.GetBytes(Convert.ToDouble(Boxed(date)))),
        VBDecimalValue @decimal => WriteBytes(stream, Decimal.GetBits(Convert.ToDecimal(Boxed(@decimal)))
            .SelectMany(BitConverter.GetBytes).ToArray()),
        VBDoubleValue @double => WriteBytes(stream, BitConverter.GetBytes(Convert.ToDouble(Boxed(@double)))),
        // Empty and Null are the descriptor and nothing else: the table gives them no bytes to write.
        _ => 0,
    };

    // "In random mode, the first two bytes are the length of the String... In binary mode there is no two-byte
    // prefix, and the String is stored in ANSI form, without NULL termination."
    private static int WriteString(Stream stream, string text, VBFileMode mode)
    {
        var bytes = Ansi.GetBytes(text);
        return mode is VBFileMode.Binary
            ? WriteBytes(stream, bytes)
            : WriteBytes(stream, BitConverter.GetBytes((ushort)Math.Min(bytes.Length, ushort.MaxValue)))
                + WriteBytes(stream, bytes);
    }

    private static bool TryReadDescribed(
        Stream stream, VariantDescriptor descriptor, VBFileMode mode, int currentLength, out VBTypedValue? value)
    {
        value = descriptor switch
        {
            VariantDescriptor.Empty => VBEmptyValue.Empty,
            VariantDescriptor.Null => VBNullValue.Null,
            _ => null,
        };

        if (value is not null)
        {
            return true;
        }

        return TryReadTyped(stream, DescribedType(descriptor), mode, currentLength, out value);
    }

    private static bool TryReadTyped(
        Stream stream, VBType? declaredType, VBFileMode mode, int currentLength, out VBTypedValue? value)
    {
        value = null;
        switch (declaredType)
        {
            case null:
                return false;

            case VBFixedStringType fixedString:
                value = new VBFixedStringValue(fixedString.Length).WithFixedValue(Ansi.GetString(ReadBytes(stream, fixedString.Length)));
                return true;

            case VBStringType:
                value = ReadString(stream, mode, currentLength);
                return true;
        }

        if (SizeOf(declaredType) is not { } size)
        {
            return false;
        }

        var bytes = ReadBytes(stream, size);
        value = declaredType switch
        {
            VBBooleanType => new VBBooleanValue(BitConverter.ToInt16(bytes) != 0),
            VBByteType => new VBByteValue(bytes[0]),
            VBIntegerType => new VBIntegerValue(BitConverter.ToInt16(bytes)),
            VBLongType or VBEnumType => new VBLongValue(BitConverter.ToInt32(bytes)),
            VBErrorType => new VBErrorValue(BitConverter.ToInt32(bytes)),
            VBLongLongType => new VBLongLongValue(BitConverter.ToInt64(bytes)),
            VBSingleType => new VBSingleValue(BitConverter.ToSingle(bytes)),
            VBCurrencyType => new VBCurrencyValue(BitConverter.ToInt64(bytes) / CurrencyScale),
            VBDateType => new VBDateValue(BitConverter.ToDouble(bytes)),
            VBDecimalType => new VBDecimalValue(new decimal(
                [BitConverter.ToInt32(bytes, 0), BitConverter.ToInt32(bytes, 4), BitConverter.ToInt32(bytes, 8), BitConverter.ToInt32(bytes, 12)])),
            VBDoubleType => new VBDoubleValue(BitConverter.ToDouble(bytes)),
            _ => null,
        };

        return value is not null;
    }

    private static VBStringValue ReadString(Stream stream, VBFileMode mode, int currentLength)
    {
        // "If the value type of <variable> is String, then the number of bytes to read is the number of
        // characters in <variable>" - a Binary String has no length in the record, so the variable's own
        // current length is what says how much of the file belongs to it.
        // In Random mode, "Two bytes are read from the file. The data value of these two bytes is the number of bytes to read from the file."
        var length = mode is VBFileMode.Binary ? currentLength : BitConverter.ToUInt16(ReadBytes(stream, StringLengthPrefixSize));
        return new VBStringValue(Ansi.GetString(ReadBytes(stream, length)));
    }

    /// <summary>
    /// How many bytes a value of <paramref name="declaredType"/> occupies in a record, or <c>null</c> when the
    /// format has no fixed width for it (a <c>String</c>) or no row at all (an object, a UDT).
    /// </summary>
    private static int? SizeOf(VBType declaredType) => declaredType switch
    {
        VBByteType => 1,
        // "If the data value of the Boolean is True, then the two bytes are FF FF" - two, not one.
        VBBooleanType or VBIntegerType => 2,
        // an Enum holds a Long (MS-VBAL §5.2.3.4), which is what Put writes of it.
        VBLongType or VBSingleType or VBErrorType or VBEnumType => 4,
        VBCurrencyType or VBDateType or VBDoubleType or VBLongLongType => 8,
        VBDecimalType => 16,
        _ => null,
    };

    private static VBType? DescribedType(VariantDescriptor descriptor) => descriptor switch
    {
        VariantDescriptor.Integer => VBIntegerType.TypeInfo,
        VariantDescriptor.Long => VBLongType.TypeInfo,
        VariantDescriptor.Single => VBSingleType.TypeInfo,
        VariantDescriptor.Double => VBDoubleType.TypeInfo,
        VariantDescriptor.Currency => VBCurrencyType.TypeInfo,
        VariantDescriptor.Date => VBDateType.TypeInfo,
        VariantDescriptor.String => VBStringType.TypeInfo,
        VariantDescriptor.Error => VBErrorType.TypeInfo,
        VariantDescriptor.Boolean => VBBooleanType.TypeInfo,
        VariantDescriptor.Decimal => VBDecimalType.TypeInfo,
        VariantDescriptor.LongLong => VBLongLongType.TypeInfo,
        VariantDescriptor.Variant => VBVariantType.TypeInfo,
        VariantDescriptor.Byte => VBByteType.TypeInfo,
        // the table's "Unknown", "User Defined Type" and "Object" rows are all ERROR - there is no descriptor
        // byte that means them, so one that means nothing else is a record this cannot read.
        _ => null,
    };

    private static VariantDescriptor? DescriptorOf(VBTypedValue value) => value switch
    {
        VBEmptyValue => VariantDescriptor.Empty,
        VBNullValue => VariantDescriptor.Null,
        VBIntegerValue => VariantDescriptor.Integer,
        VBLongValue => VariantDescriptor.Long,
        VBSingleValue => VariantDescriptor.Single,
        VBDoubleValue => VariantDescriptor.Double,
        VBCurrencyValue => VariantDescriptor.Currency,
        VBDateValue => VariantDescriptor.Date,
        VBStringValue => VariantDescriptor.String,
        VBErrorValue => VariantDescriptor.Error,
        VBBooleanValue => VariantDescriptor.Boolean,
        VBDecimalValue => VariantDescriptor.Decimal,
        VBLongLongValue => VariantDescriptor.LongLong,
        // a Byte has no row in the descriptor table: MS-VBA writes VarType's, vbByte, then the one byte (recorded in Excel).
        VBByteValue => VariantDescriptor.Byte,
        _ => null,
    };

    // the element type of an array a Variant holds, as VarType describes it; an array of records or of objects has no descriptor this format writes.
    private static VariantDescriptor? DescriptorOf(VBType itemType) => itemType switch
    {
        VBVariantType => VariantDescriptor.Variant,
        VBByteType => VariantDescriptor.Byte,
        VBIntegerType => VariantDescriptor.Integer,
        VBLongType or VBEnumType => VariantDescriptor.Long,
        VBSingleType => VariantDescriptor.Single,
        VBDoubleType => VariantDescriptor.Double,
        VBCurrencyType => VariantDescriptor.Currency,
        VBDateType => VariantDescriptor.Date,
        VBStringType => VariantDescriptor.String,
        VBErrorType => VariantDescriptor.Error,
        VBBooleanType => VariantDescriptor.Boolean,
        VBDecimalType => VariantDescriptor.Decimal,
        VBLongLongType => VariantDescriptor.LongLong,
        _ => null,
    };

    private static int WriteBytes(Stream stream, byte[] bytes)
    {
        stream.Write(bytes, 0, bytes.Length);
        return bytes.Length;
    }

    // a read past the end of the file is no error: each byte the file does not have reads a zero, and the file-pointer-position moves past them all,
    // which is what EOF then says (MS-VBA, recorded in Excel).
    private static byte[] ReadBytes(Stream stream, int count)
    {
        var bytes = new byte[count];
        var read = 0;
        while (read < count)
        {
            var got = stream.Read(bytes, read, count - read);
            if (got == 0)
            {
                stream.Seek(count - read, SeekOrigin.Current);
                break;
            }

            read += got;
        }

        return bytes;
    }

    // a Variant's own TypeInfo mirrors whatever it wraps, so what is written is the wrapped value's format -
    // after the Variant's own type descriptor, which is why the two are decided separately. A Variant holds
    // either a typed value or the managed value directly, so both shapes unwrap here: the second needs its
    // type to build a value of, which is the TypeInfo the Variant is already reporting. An array has no binding of its own to read
    // (VBResizableArrayValue's handle is inert): the one a Variant holds is its typed value.
    private static VBTypedValue Unwrapped(VBTypedValue value) => value switch
    {
        VBVariantValue { TypedValue: VBArrayValue array } => array,
        VBVariantValue when value.Handle.Value.BoxedValue is VBTypedValue wrapped => wrapped,
        VBVariantValue variant when variant.TypeInfo is not VBVariantType => variant.TypeInfo.CreateValue(variant.Handle),
        _ => value,
    };

    private static object? Boxed(VBTypedValue value) => value.Handle.Value.BoxedValue;

    private static string Text(VBStringValue value) => value.Handle.Value.BoxedValue as string ?? string.Empty;
}
