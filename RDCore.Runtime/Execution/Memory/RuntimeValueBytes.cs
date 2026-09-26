using RDCore.SDK.Model.Values.Runtime;
using System.Text;

namespace RDCore.Runtime.Execution.Memory;

/// <summary>
/// The byte image of a runtime value, and the value back again from a mutated image.
/// </summary>
/// <remarks>
/// What makes a session's memory byte-addressable: a value bound at an address occupies bytes there,
/// and reading or writing one of them is a real thing to want to do — <c>PEEK</c> and <c>POKE</c> are
/// the whole reason this exists. The layout is the managed one (little-endian on every platform
/// RDCore targets), which is the same layout the value's own <c>Size</c> already accounts for.
/// <para>
/// A value whose managed representation is not a fixed run of bytes — an object reference, an array,
/// a user-defined type — has no image here, and reading or writing its bytes reports failure rather
/// than inventing a layout for it.
/// </para>
/// </remarks>
internal static class RuntimeValueBytes
{
    /// <summary>
    /// The byte image of <paramref name="value"/>, or <c>null</c> when it has none.
    /// </summary>
    public static byte[]? Of(IRuntimeValue value) => value switch
    {
        // a Boolean boxes as an Int32 but occupies two bytes (MS-VBAL §2.1), so it is matched on its
        // own runtime type rather than on what it boxes to.
        VBRuntimeBooleanValue boolean => BitConverter.GetBytes(boolean.StoredValue),
        _ => OfBoxed(value.BoxedValue),
    };

    private static byte[]? OfBoxed(object boxed) => boxed switch
    {
        byte single => [single],
        sbyte signed => [unchecked((byte)signed)],
        short integer => BitConverter.GetBytes(integer),
        ushort unsigned => BitConverter.GetBytes(unsigned),
        int @long => BitConverter.GetBytes(@long),
        uint unsigned => BitConverter.GetBytes(unsigned),
        long longLong => BitConverter.GetBytes(longLong),
        ulong unsigned => BitConverter.GetBytes(unsigned),
        float single => BitConverter.GetBytes(single),
        double @double => BitConverter.GetBytes(@double),
        // a Date is a Double in memory, which is exactly why a POKE into one is a rich way to break it.
        DateTime date => BitConverter.GetBytes(date.ToOADate()),
        decimal @decimal => decimal.GetBits(@decimal).SelectMany(BitConverter.GetBytes).ToArray(),
        // VBA strings are UTF-16, so two bytes per character.
        string text => Encoding.Unicode.GetBytes(text),
        _ => null,
    };

    /// <summary>
    /// Rebuilds a value of the same managed shape as <paramref name="value"/> from
    /// <paramref name="bytes"/>, or returns <c>null</c> when that shape cannot be rebuilt from bytes.
    /// </summary>
    /// <param name="value">The value whose shape the result takes.</param>
    /// <param name="bytes">The mutated byte image, the same length <see cref="Of"/> produced.</param>
    public static IRuntimeValue? From(IRuntimeValue value, byte[] bytes) => value switch
    {
        // MS-VBAL Boolean is two bytes, and anything non-zero is True.
        VBRuntimeBooleanValue => new VBRuntimeBooleanValue(BitConverter.ToInt16(bytes)),
        _ => FromBoxed(value.BoxedValue, bytes),
    };

    private static IRuntimeValue? FromBoxed(object boxed, byte[] bytes) => boxed switch
    {
        byte => new VBRuntimeValue<byte>(bytes[0]),
        sbyte => new VBRuntimeValue<sbyte>(unchecked((sbyte)bytes[0])),
        short => new VBRuntimeValue<short>(BitConverter.ToInt16(bytes)),
        ushort => new VBRuntimeValue<ushort>(BitConverter.ToUInt16(bytes)),
        int => new VBRuntimeValue<int>(BitConverter.ToInt32(bytes)),
        uint => new VBRuntimeValue<uint>(BitConverter.ToUInt32(bytes)),
        long => new VBRuntimeValue<long>(BitConverter.ToInt64(bytes)),
        ulong => new VBRuntimeValue<ulong>(BitConverter.ToUInt64(bytes)),
        float => new VBRuntimeValue<float>(BitConverter.ToSingle(bytes)),
        double => new VBRuntimeValue<double>(BitConverter.ToDouble(bytes)),
        DateTime => FromOADate(BitConverter.ToDouble(bytes)),
        decimal => FromDecimalBits(bytes),
        string => new VBRuntimeValue<string>(Encoding.Unicode.GetString(bytes)),
        _ => null,
    };

    // an arbitrary Double is not necessarily a representable date; a poke that lands outside the range
    // is the caller's business, not a reason to throw out of a byte write.
    private static IRuntimeValue? FromOADate(double serial)
    {
        try
        {
            return new VBRuntimeValue<DateTime>(DateTime.FromOADate(serial));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // likewise: not every 16-byte pattern is a valid Decimal (the flags word is constrained).
    private static IRuntimeValue? FromDecimalBits(byte[] bytes)
    {
        if (bytes.Length < 16)
        {
            return null;
        }

        try
        {
            return new VBRuntimeValue<decimal>(new decimal(
            [
                BitConverter.ToInt32(bytes, 0), BitConverter.ToInt32(bytes, 4),
                BitConverter.ToInt32(bytes, 8), BitConverter.ToInt32(bytes, 12),
            ]));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
