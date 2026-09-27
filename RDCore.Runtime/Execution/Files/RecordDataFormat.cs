using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
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
/// 🚧 A UDT is not handled yet. The specification says "the value of each member of the UDT is written to the
/// file... in the order in which the members are declared", which needs member-by-member access to the
/// session's memory that nothing exposes yet — see the TODO on <see cref="TryWrite"/>.
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
        Decimal = 14,
        LongLong = 20,
    }

    /// <summary>ANSI, "without NULL termination" — the encoding the format's String rows name.</summary>
    private static readonly Encoding Ansi = Encoding.Latin1;

    private const int DescriptorSize = 2;
    private const int StringLengthPrefixSize = 2;

    /// <summary>
    /// A <c>Boolean</c> is "FF FF" when true and "00 00" otherwise — not <c>01 00</c>, and not one byte.
    /// </summary>
    private const short BooleanTrue = -1;

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

        if (isVariantTarget)
        {
            if (DescriptorOf(unwrapped) is not { } descriptor)
            {
                return false;
            }

            stream.WriteByte((byte)descriptor);
            stream.WriteByte(0);
            written += DescriptorSize;
        }

        written += WriteValue(stream, unwrapped, mode);
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
    /// <returns><c>false</c> for a declared type this format has no row for, or at end of file.</returns>
    public static bool TryRead(
        Stream stream, VBType declaredType, VBFileMode mode, int currentLength, out VBTypedValue? value)
    {
        value = null;
        if (declaredType.Equals(VBVariantType.TypeInfo))
        {
            // "Two bytes are read from the file. These two bytes are the type descriptor for the data value
            // that follows."
            if (!TryReadBytes(stream, DescriptorSize, out var descriptorBytes))
            {
                return false;
            }

            return TryReadDescribed(stream, (VariantDescriptor)descriptorBytes[0], mode, currentLength, out value);
        }

        return TryReadTyped(stream, declaredType, mode, currentLength, out value);
    }

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
    /// <returns><c>false</c> at end of file, or for a field whose type the format has no row for.</returns>
    public static bool TryReadInto(Stream stream, VBUserDefinedTypeValue udt, VBFileMode mode)
    {
        for (var index = 0; index < udt.Fields.Length; index++)
        {
            var field = udt.Fields[index];

            // a nested UDT is read into in place too, so its own fields keep their identity the way the
            // outer ones do - and so a String field nested two deep still measures its own current length.
            if (udt.FieldAt(index) is VBUserDefinedTypeValue nested)
            {
                if (!TryReadInto(stream, nested, mode))
                {
                    return false;
                }

                continue;
            }

            var currentLength = udt.FieldAt(index) is VBStringValue text ? text.Length : 0;
            if (!TryRead(stream, field.ResolvedType ?? VBVariantType.TypeInfo, mode, currentLength, out var value)
                || !udt.TrySetFieldAt(index, value!))
            {
                return false;
            }
        }

        return true;
    }

    // the write side of the same walk. A field is written by its own row of the format, so a String field
    // carries a length prefix in Random mode and none in Binary, exactly as a String variable would.
    private static bool TryWriteFields(Stream stream, VBUserDefinedTypeValue udt, VBFileMode mode, out int written)
    {
        written = 0;
        for (var index = 0; index < udt.Fields.Length; index++)
        {
            if (udt.FieldAt(index) is not { } field
                || !TryWrite(stream, field, mode, isVariantTarget: field is VBVariantValue, out var fieldBytes))
            {
                return false;
            }

            written += fieldBytes;
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
        // Currency and Date are eight bytes each, the one scaled and the other an OLE Automation serial - both
        // of which the managed representation already is.
        VBCurrencyValue currency => WriteBytes(stream, BitConverter.GetBytes(Convert.ToDouble(Boxed(currency)))),
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
                return TryReadBytes(stream, fixedString.Length, out var fixedBytes)
                    && Assigned(new VBFixedStringValue(fixedString.Length).WithFixedValue(Ansi.GetString(fixedBytes)), out value);

            case VBStringType:
                return TryReadString(stream, mode, currentLength, out value);
        }

        if (SizeOf(declaredType) is not { } size || !TryReadBytes(stream, size, out var bytes))
        {
            return false;
        }

        value = declaredType switch
        {
            VBBooleanType => new VBBooleanValue(BitConverter.ToInt16(bytes) != 0),
            VBByteType => new VBByteValue(bytes[0]),
            VBIntegerType => new VBIntegerValue(BitConverter.ToInt16(bytes)),
            VBLongType => new VBLongValue(BitConverter.ToInt32(bytes)),
            VBErrorType => new VBErrorValue(BitConverter.ToInt32(bytes)),
            VBLongLongType => new VBLongLongValue(BitConverter.ToInt64(bytes)),
            VBSingleType => new VBSingleValue(BitConverter.ToSingle(bytes)),
            VBCurrencyType => new VBCurrencyValue(Convert.ToDecimal(BitConverter.ToDouble(bytes))),
            VBDateType => new VBDateValue(BitConverter.ToDouble(bytes)),
            VBDecimalType => new VBDecimalValue(new decimal(
                [BitConverter.ToInt32(bytes, 0), BitConverter.ToInt32(bytes, 4), BitConverter.ToInt32(bytes, 8), BitConverter.ToInt32(bytes, 12)])),
            VBDoubleType => new VBDoubleValue(BitConverter.ToDouble(bytes)),
            _ => null,
        };

        return value is not null;
    }

    private static bool TryReadString(Stream stream, VBFileMode mode, int currentLength, out VBTypedValue? value)
    {
        value = null;
        if (mode is VBFileMode.Binary)
        {
            // "If the value type of <variable> is String, then the number of bytes to read is the number of
            // characters in <variable>" - a Binary String has no length in the record, so the variable's own
            // current length is what says how much of the file belongs to it.
            if (!TryReadBytes(stream, currentLength, out var raw))
            {
                return false;
            }

            value = new VBStringValue(Ansi.GetString(raw));
            return true;
        }

        // "Two bytes are read from the file. The data value of these two bytes is the number of bytes to read
        // from the file."
        if (!TryReadBytes(stream, StringLengthPrefixSize, out var prefix)
            || !TryReadBytes(stream, BitConverter.ToUInt16(prefix), out var bytes))
        {
            return false;
        }

        value = new VBStringValue(Ansi.GetString(bytes));
        return true;
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
        VBLongType or VBSingleType or VBErrorType => 4,
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
        // a Byte has no row in the descriptor table at all, so a Variant holding one cannot be written as a
        // Variant - MS-VBA widens it to Integer, and so does this.
        VBByteValue => VariantDescriptor.Integer,
        _ => null,
    };

    private static int WriteBytes(Stream stream, byte[] bytes)
    {
        stream.Write(bytes, 0, bytes.Length);
        return bytes.Length;
    }

    private static bool TryReadBytes(Stream stream, int count, out byte[] bytes)
    {
        bytes = new byte[count];
        var read = 0;
        while (read < count)
        {
            var got = stream.Read(bytes, read, count - read);
            if (got == 0)
            {
                return false;
            }

            read += got;
        }

        return true;
    }

    private static bool Assigned(VBTypedValue read, out VBTypedValue? value)
    {
        value = read;
        return true;
    }

    // a Variant's own TypeInfo mirrors whatever it wraps, so what is written is the wrapped value's format -
    // after the Variant's own type descriptor, which is why the two are decided separately. A Variant holds
    // either a typed value or the managed value directly, so both shapes unwrap here: the second needs its
    // type to build a value of, which is the TypeInfo the Variant is already reporting.
    private static VBTypedValue Unwrapped(VBTypedValue value) => value switch
    {
        VBVariantValue when value.Handle.Value.BoxedValue is VBTypedValue wrapped => wrapped,
        VBVariantValue variant when variant.TypeInfo is not VBVariantType => variant.TypeInfo.CreateValue(variant.Handle),
        _ => value,
    };

    private static object? Boxed(VBTypedValue value) => value.Handle.Value.BoxedValue;

    private static string Text(VBStringValue value) => value.Handle.Value.BoxedValue as string ?? string.Empty;
}
