using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Intrinsic;
using static RDCore.Tests.Runtime.StdLib.StdStringsTestHarness;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.11.1.3 AscW</strong>: "the 16-bit Unicode code point of the first character", a code point above 32,767
/// returned as a negative <c>Integer</c>.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11.1.3 AscW")]
public sealed class AscWTests
{
    [TestMethod]
    [DataRow("A", (short)65)]
    [DataRow("\u20AC", (short)8364)]
    [DataRow("\u7FFF", (short)32767)]
    public void AscW_IsTheFirstCodeUnit(string text, short code)
        => Assert.AreEqual(code, IntegerOf(Module().AscW(new VBStringValue(text))));

    [TestMethod]
    [DataRow("\u8000", (short)-32768)]
    [DataRow("\uFFFF", (short)-1)]
    public void AscW_PastInt16_IsNegative(string text, short code)
        => Assert.AreEqual(code, IntegerOf(Module().AscW(new VBStringValue(text))));

    [TestMethod]
    public void AscW_OfASurrogatePair_IsItsHighHalf()
        => Assert.AreEqual((short)-10179, IntegerOf(Module().AscW(new VBStringValue("\uD83D\uDE00"))));

    [TestMethod]
    public void AscW_OfAZeroLengthString_OrASingleByte_IsError5()
    {
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().AscW(new VBStringValue(string.Empty))));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().AscW(Bytes(0x41))));
    }

    [TestMethod]
    public void AscW_FromSource()
        // two single bytes are one character, &H4241.
        => CollectionAssert.AreEqual(new[] { "16961", "-1" }, Run("Debug.Print AscW(ChrB(65) & ChrB(66))", "Debug.Print AscW(ChrW(-1))"));
}
