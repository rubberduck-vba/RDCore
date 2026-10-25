using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.Execution.Memory;

/// <summary>
/// The <c>BSTR</c> a variable-length <c>String</c> is in the session's memory, as <strong>MS-VBA</strong> lays one out (recorded in Excel, oracle\s17 S05,
/// S13): its length in bytes as a <c>Long</c>, then its data - two bytes per character, and the odd byte of a <c>String</c> that has one - then a null
/// character. <c>StrPtr</c> is the address of the first character, four bytes into the block.
/// </summary>
/// <remarks>
/// <para>
/// A block is made for a <c>String</c> where it is held - a variable, an element of an array, a field of a record, a <c>Variant</c> - the first time its
/// address is asked for, and is the same for as long as the holder holds that very <c>String</c>: <c>t = s</c> gives <c>t</c> a block of its own (S03), as
/// MS-VBA copies the characters, and a <c>String</c> assigned to the holder is in another block (S02).
/// </para>
/// <para>
/// The block's bytes are its own: the length is what its first four say, which a <c>POKE</c> can make shorter than the data, and the characters past it stay
/// where they are (oracle\s17 grid-p3 L01). Another holder can be given the block's address as its pointer, and the two then hold the one <c>String</c>: a
/// change to the characters through either is the other's too (Q01).
/// </para>
/// </remarks>
internal sealed class StringBlock
{
    /// <summary>How many bytes the length prefix occupies, which <c>StrPtr</c> is past.</summary>
    public const int PrefixSize = sizeof(int);

    /// <summary>How many bytes the null character after the data occupies.</summary>
    public const int TerminatorSize = sizeof(char);

    /// <param name="start">Where the block begins: its length prefix.</param>
    /// <param name="holder">Where the <c>String</c> is held: the address whose bytes are the pointer to the characters.</param>
    /// <param name="value">The <c>String</c> the block holds the data of.</param>
    public StringBlock(MemoryAddress start, MemoryAddress holder, IRuntimeValue value)
    {
        Start = start;
        Value = value;
        Holders.Add(holder);
        var data = VBRuntimeByteStringValue.BytesOf(value);
        Bytes = new byte[SizeFor(data.Length)];
        BitConverter.TryWriteBytes(Bytes, data.Length);
        data.CopyTo(Bytes, PrefixSize);
    }

    /// <summary>Where the block begins: its length prefix.</summary>
    public MemoryAddress Start { get; }

    /// <summary>
    /// Where the <c>String</c> is held: each address whose bytes are the pointer to the characters - the one it was made for, and any other given it.
    /// </summary>
    public HashSet<MemoryAddress> Holders { get; } = [];

    /// <summary>The address of the first character: what <c>StrPtr</c> is.</summary>
    public MemoryAddress Characters => new(Start.Value + PrefixSize);

    /// <summary>The <c>String</c> the block holds: what its length says of its data.</summary>
    public IRuntimeValue Value { get; private set; }

    /// <summary>The block's bytes: the length prefix, the data, and the null character.</summary>
    public byte[] Bytes { get; private set; }

    /// <summary>How many bytes a block for data of <paramref name="length"/> bytes occupies.</summary>
    public static int SizeFor(int length) => PrefixSize + length + TerminatorSize;

    /// <summary>
    /// Writes <paramref name="text"/>, as long as the <c>String</c> the block holds, into its data: what the <c>Mid</c> statement does in place.
    /// </summary>
    /// <returns><see langword="false"/> when it is not as long.</returns>
    public bool TryRewrite(IRuntimeValue text)
    {
        var data = VBRuntimeByteStringValue.BytesOf(text);
        if (data.Length != BitConverter.ToInt32(Bytes, 0))
        {
            return false;
        }

        data.CopyTo(Bytes, PrefixSize);
        Value = text;
        return true;
    }

    /// <summary>
    /// Takes <paramref name="image"/> as the block's bytes, one of them changed by a <c>POKE</c>: the <c>String</c> it holds is then what its length says of
    /// its data.
    /// </summary>
    /// <param name="image">The block's bytes.</param>
    /// <param name="changed">The <c>String</c> it holds now, when it is another.</param>
    /// <returns>
    /// <see langword="false"/> when the length says the data goes on past the block, which would be reading memory the block is not.
    /// </returns>
    public bool TryWrite(byte[] image, out IRuntimeValue? changed)
    {
        changed = null;
        var length = BitConverter.ToInt32(image, 0);
        if (length < 0 || PrefixSize + length > image.Length)
        {
            return false;
        }

        Bytes = image;
        var text = VBRuntimeByteStringValue.FromBytes(image.AsSpan(PrefixSize, length));
        if (!VBRuntimeByteStringValue.BytesOf(text).AsSpan().SequenceEqual(VBRuntimeByteStringValue.BytesOf(Value)))
        {
            changed = Value = text;
        }

        return true;
    }
}
