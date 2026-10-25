using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.Execution.Memory;

/// <summary>
/// The <c>BSTR</c> a variable-length <c>String</c> is in the session's memory, as <strong>MS-VBA</strong> lays one out (recorded in Excel, oracle\s17 S05,
/// S13): its length in bytes as a <c>Long</c>, then its data - two bytes per character, and the odd byte of a <c>String</c> that has one - then a null
/// character. <c>StrPtr</c> is the address of the first character, four bytes into the block.
/// </summary>
/// <remarks>
/// A block is made for a <c>String</c> where it is held - a variable, an element of an array, a field of a record, a <c>Variant</c> (its <see cref="Holder"/>) -
/// the first time its address is asked for, and is the same for as long as the holder holds that very <c>String</c>: <c>t = s</c> gives <c>t</c> a block of its
/// own (S03), as MS-VBA copies the characters, and a <c>String</c> assigned to the holder is in another block (S02).
/// </remarks>
/// <param name="start">Where the block begins: its length prefix.</param>
/// <param name="holder">Where the <c>String</c> is held: the address whose bytes are the pointer to the characters.</param>
/// <param name="value">The <c>String</c> the block holds the data of.</param>
internal sealed class StringBlock(MemoryAddress start, MemoryAddress holder, IRuntimeValue value)
{
    /// <summary>How many bytes the length prefix occupies, which <c>StrPtr</c> is past.</summary>
    public const int PrefixSize = sizeof(int);

    /// <summary>How many bytes the null character after the data occupies.</summary>
    public const int TerminatorSize = sizeof(char);

    /// <summary>Where the block begins: its length prefix.</summary>
    public MemoryAddress Start { get; } = start;

    /// <summary>Where the <c>String</c> is held.</summary>
    public MemoryAddress Holder { get; } = holder;

    /// <summary>The address of the first character: what <c>StrPtr</c> is.</summary>
    public MemoryAddress Characters => new(Start.Value + PrefixSize);

    /// <summary>The <c>String</c> the block holds, which a <c>POKE</c> into its characters replaces with what the bytes then spell.</summary>
    public IRuntimeValue Value { get; set; } = value;

    /// <summary>How many bytes a block for data of <paramref name="length"/> bytes occupies.</summary>
    public static int SizeFor(int length) => PrefixSize + length + TerminatorSize;

    /// <summary>
    /// The block's bytes: the length prefix, the data, and the null character.
    /// </summary>
    public byte[] Image()
    {
        var data = VBRuntimeByteStringValue.BytesOf(Value);
        var image = new byte[SizeFor(data.Length)];
        BitConverter.TryWriteBytes(image, data.Length);
        data.CopyTo(image, PrefixSize);
        return image;
    }

    /// <summary>
    /// The <c>String</c> a mutated image spells, when the change is in its data: the length prefix and the null character are where the block's size is,
    /// which a <c>POKE</c> does not change.
    /// </summary>
    /// <param name="image">The block's bytes, one of them changed.</param>
    /// <param name="offset">Where the change is.</param>
    /// <returns><see langword="null"/> when the change is not in the data.</returns>
    public static IRuntimeValue? Spelled(byte[] image, int offset)
        => offset >= PrefixSize && offset < image.Length - TerminatorSize
            ? VBRuntimeByteStringValue.FromBytes(image.AsSpan(PrefixSize, image.Length - PrefixSize - TerminatorSize))
            : null;
}
