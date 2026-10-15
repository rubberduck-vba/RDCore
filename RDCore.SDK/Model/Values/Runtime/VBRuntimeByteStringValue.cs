using System.Runtime.InteropServices;
namespace RDCore.SDK.Model.Values.Runtime;

/// <summary>
/// The runtime value of a <c>String</c> whose data is an odd number of bytes: the whole characters it holds, and the byte
/// left over after them.
/// </summary>
/// <remarks>
/// The binary format a <c>String</c> is stored in is "the implementation-defined binary format used to store <c>String</c>
/// data" (<strong>MS-VBAL §5.5.1.2.6</strong>), and a <c>Byte()</c> of any length is "interpreted as if it represents" it.
/// RDCore's format is UTF-16LE code units - which a managed <see cref="string"/> already is - and, when the data is an odd
/// number of bytes, the one byte left over after the last whole code unit. A <see cref="string"/> cannot hold half a code
/// unit, so that byte is held beside it.
/// <para>
/// 👉 Only an odd string is one of these: an even one is the <see cref="VBRuntimeValue{T}"/> of a <see cref="string"/> it
/// always was. <see cref="BoxedValue"/> is the whole characters, so a reader that only knows the managed value sees the
/// characters of an odd string - never nothing, and never half a character.
/// </para>
/// <para>
/// The byte image assumes a little-endian platform that allows misaligned access, as the session's memory already does.
/// </para>
/// </remarks>
/// <param name="Text">The whole characters: every code unit the data makes up.</param>
/// <param name="TrailingByte">The byte left over after them.</param>
public readonly record struct VBRuntimeByteStringValue(string Text, byte TrailingByte) : IRuntimeValue<string>
{
    /// <inheritdoc/>
    public object BoxedValue => Text;
    /// <inheritdoc/>
    public string StoredValue => Text;

    /// <summary>
    /// The runtime value of the <c>String</c> whose data is <paramref name="bytes"/>: an ordinary one for an even number of
    /// bytes, and one of these for an odd number.
    /// </summary>
    /// <remarks>
    /// Every byte is kept, and every pair of bytes is a code unit - a lone surrogate included. <c>Encoding.Unicode</c> is not
    /// this format: it reads <c>00 D8</c> as <c>U+FFFD</c> and drops an odd byte.
    /// </remarks>
    /// <param name="bytes">The data of the string, two bytes per code unit, little-endian.</param>
    public static IRuntimeValue<string> FromBytes(ReadOnlySpan<byte> bytes)
    {
        var text = new string(MemoryMarshal.Cast<byte, char>(bytes[..(bytes.Length & ~1)]));
        return bytes.Length % 2 == 0
            ? new VBRuntimeValue<string>(text)
            : new VBRuntimeByteStringValue(text, bytes[^1]);
    }

    /// <summary>
    /// The data of the <c>String</c> <paramref name="value"/> holds: two bytes per code unit, little-endian, then the trailing
    /// byte when there is one.
    /// </summary>
    /// <remarks>
    /// <c>vbNullString</c> has no data at all, and neither has a value that holds no <c>String</c>.
    /// </remarks>
    /// <param name="value">The runtime value of a <c>String</c>.</param>
    public static byte[] BytesOf(IRuntimeValue value) => value switch
    {
        VBRuntimeByteStringValue odd => [.. MemoryMarshal.AsBytes(odd.Text.AsSpan()), odd.TrailingByte],
        { BoxedValue: string text } => MemoryMarshal.AsBytes(text.AsSpan()).ToArray(),
        _ => [],
    };

    /// <inheritdoc/>
    public override string ToString() => Text;
}
