using RDCore.Runtime.Execution.Memory;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.Tests.Runtime.Memory;

/// <summary>
/// The byte image of a String, and a String back from a mutated image: its data, every byte of it, read and written as it
/// is rather than through a text encoding - which is what makes <c>PEEK</c> and <c>POKE</c> of a String's bytes exact.
/// </summary>
[TestClass]
public sealed class RuntimeValueBytesTests
{
    [TestMethod]
    public void Of_AStringWithATrailingByte_ImagesEveryByte()
        => CollectionAssert.AreEqual(new byte[] { 0x41, 0x00, 0x42 },
            RuntimeValueBytes.Of(VBRuntimeByteStringValue.FromBytes([0x41, 0x00, 0x42])));

    [TestMethod]
    public void Of_ALoneSurrogate_IsItsOwnTwoBytes()
        // a text encoding would write the replacement character's FD FF instead.
        => CollectionAssert.AreEqual(new byte[] { 0x00, 0xD8 }, RuntimeValueBytes.Of(new VBRuntimeValue<string>("\uD800")));

    [TestMethod]
    public void Of_TheNullString_HasNoImage()
        // vbNullString points at no data at all, which is not the same as data of no bytes.
        => Assert.IsNull(RuntimeValueBytes.Of(new VBRuntimeValue<string>(null!)));

    [TestMethod]
    public void From_AnOddImage_KeepsItOdd()
    {
        var rebuilt = RuntimeValueBytes.From(VBRuntimeByteStringValue.FromBytes([0x41, 0x00, 0x42]), [0x00, 0xD8, 0x42]);

        Assert.AreEqual(new VBRuntimeByteStringValue("\uD800", 0x42), rebuilt);
    }

    [TestMethod]
    public void From_AStringImage_KeepsALoneSurrogate()
        => Assert.AreEqual("\uD800", RuntimeValueBytes.From(new VBRuntimeValue<string>("a"), [0x00, 0xD8])!.BoxedValue);
}
