using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Intrinsic;
using static RDCore.Tests.Runtime.StdLib.StdStringsTestHarness;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.11.1.1 Asc</strong>: "an Integer data value representing the 7-bit ASCII code point of the first
/// character", a code past ASCII being implementation defined - the environment's ANSI code page here.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11.1.1 Asc")]
public sealed class AscTests
{
    [TestMethod]
    [DataRow("Apple", (short)65)]
    [DataRow("\0", (short)0)]
    [DataRow("\u007F", (short)127)]
    [DataRow("Ab", (short)65)]
    public void Asc_OfAnAsciiCharacter_IsItsCode(string text, short code)
        => Assert.AreEqual(code, IntegerOf(Module().Asc(new VBStringValue(text))));

    [TestMethod]
    [DataRow("\u00E9", (short)233)]
    [DataRow("\u20AC", (short)128)]
    [DataRow("\u0081", (short)129)]
    public void Asc_PastAscii_IsTheCodeOfTheCharacterInTheAnsiCodePage(string text, short code)
        => Assert.AreEqual(code, IntegerOf(Module().Asc(new VBStringValue(text))));

    [TestMethod]
    [DataRow("\u0100", (short)65, DisplayName = "a best fit")]
    [DataRow("\u3042", (short)63, DisplayName = "no fit")]
    public void Asc_OfACharacterTheCodePageLacks_IsWhatItsEncoderMapsItTo(string text, short code)
        => Assert.AreEqual(code, IntegerOf(Module().Asc(new VBStringValue(text))));

    [TestMethod]
    public void Asc_OfADoubleByteCharacter_IsItsTwoByteCode_AsANegativeInteger()
        // "Code point value greater than 32,767 are returned as negative Integer data values": &H82A0 in code page 932.
        => Assert.AreEqual((short)-32096, IntegerOf(Module(codePage: 932).Asc(new VBStringValue("\u3042"))));

    [TestMethod]
    public void Asc_OfAZeroLengthString_IsError5()
    {
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().Asc(new VBStringValue(string.Empty))));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().Asc(VBStringValue.VBNullString)));
    }

    [TestMethod]
    public void Asc_OfAStringOfASingleByte_IsError5()
        // a lone byte is no character: Len is 0.
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().Asc(Bytes(0x41))));

    [TestMethod]
    public void Asc_OfAStringWithATrailingByte_IsItsFirstCharacter()
        => Assert.AreEqual((short)65, IntegerOf(Module().Asc(Bytes(0x41, 0x00, 0x42))));

    [TestMethod]
    public void Asc_FromSource_TakesAnyValueAsItsString()
        => CollectionAssert.AreEqual(new[] { "49", "84" }, Run("Debug.Print Asc(123)", "Debug.Print Asc(True)"));

    [TestMethod]
    public void Asc_FromSource_OfNullOrEmpty()
    {
        // Null cannot be a String argument; Empty is a zero-length one.
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, RunToError("Debug.Print Asc(Null)"));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, RunToError("Debug.Print Asc(V)"));
    }

    [TestMethod]
    public void Asc_FromSource_OfChrB_IsError5()
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, RunToError("Debug.Print Asc(ChrB(65))"));
}
