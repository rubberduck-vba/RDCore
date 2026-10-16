using RDCore.SDK.Model.Errors;
using static RDCore.Tests.Runtime.StdLib.StdStringsTestHarness;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.11.1.6 ChrW</strong>: the character a 16-bit code point names, a negative argument "treated as if it
/// was the value: CharCode + 65,536".
/// </summary>
/// <remarks>
/// 👉 The range's "-32,767" is read as -32,768, the bottom of an <c>Integer</c>, so that <c>ChrW(AscW(c))</c> is <c>c</c> for every
/// code unit: <c>AscW</c> of U+8000 is -32768.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11.1.6 ChrW / ChrW$")]
public sealed class ChrWTests
{
    [TestMethod]
    [DataRow(0, "\0")]
    [DataRow(128, "\u0080")]
    [DataRow(256, "\u0100")]
    [DataRow(65535, "\uFFFF")]
    public void ChrW_IsTheCharacterOfThatCodePoint(int code, string expected)
        => Assert.AreEqual(expected, TextOf(Module().ChrW(L(code))));

    [TestMethod]
    public void ChrW_OfASurrogate_IsThatLoneSurrogate()
        // an attribute cannot carry a lone surrogate, so this row is written out.
        => Assert.AreEqual(((char)0xD83D).ToString(), TextOf(Module().ChrW(L(55357))));

    [TestMethod]
    [DataRow(-1, "\uFFFF")]
    [DataRow(-32767, "\u8001")]
    [DataRow(-32768, "\u8000")]
    public void ChrW_OfANegativeCode_Adds65536(int code, string expected)
        => Assert.AreEqual(expected, TextOf(Module().ChrW(L(code))));

    [TestMethod]
    [DataRow(65536)]
    [DataRow(-32769)]
    [DataRow(2147483647)]
    public void ChrW_OutsideItsRange_IsError5(int code)
    {
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().ChrW(L(code))));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().ChrWString(L(code))));
    }

    [TestMethod]
    public void ChrW_OfMinus32768_IsTheCharacterAscWGivesMinus32768For()
        => Assert.AreEqual(TextOf(Module().ChrW(L(32768))), TextOf(Module().ChrW(L(IntegerOf(Module().AscW(new("\u8000")))))));

    [TestMethod]
    public void ChrWDollar_FromSource()
    {
        Assert.AreEqual("12354", Run("Debug.Print AscW(ChrW$(&H3042))").Single());
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, RunToError("Debug.Print ChrW$(-32769)"));
    }
}
