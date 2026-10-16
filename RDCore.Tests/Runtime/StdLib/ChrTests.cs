using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Intrinsic;
using static RDCore.Tests.Runtime.StdLib.StdStringsTestHarness;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.11.1.4 Chr</strong>: 0 to 127 is ASCII, 128 to 255 is implementation defined - the environment's ANSI
/// code page - and anything else is error 5 "unless the implementation supports a character set with a larger code point range",
/// as a double-byte code page does.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11.1.4 Chr / Chr$")]
public sealed class ChrTests
{
    [TestMethod]
    [DataRow(0, "\0")]
    [DataRow(10, "\n")]
    [DataRow(65, "A")]
    [DataRow(127, "\u007F")]
    public void Chr_OfAnAsciiCode_IsThatCharacter(int code, string expected)
        => Assert.AreEqual(expected, TextOf(Module().Chr(L(code))));

    [TestMethod]
    [DataRow(128, "\u20AC")]
    [DataRow(159, "\u0178")]
    [DataRow(255, "\u00FF")]
    [DataRow(129, "\u0081")]
    public void Chr_From128To255_IsTheCharacterOfTheAnsiCodePage(int code, string expected)
        => Assert.AreEqual(expected, TextOf(Module().Chr(L(code))));

    [TestMethod]
    [DataRow(256)]
    [DataRow(-1)]
    [DataRow(65535)]
    [DataRow(-32768)]
    [DataRow(65536)]
    public void Chr_OutsideZeroTo255_OnASingleByteCodePage_IsError5(int code)
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().Chr(L(code))));

    [TestMethod]
    public void Chr_OnADoubleByteCodePage_TakesADoubleByteCode()
    {
        var module = Module(codePage: 932);

        Assert.AreEqual("\u3042", TextOf(module.Chr(L(-32096))), "a negative code is the code plus 65,536");
        Assert.AreEqual("\u3042", TextOf(module.Chr(L(33440))));
        Assert.AreEqual("\u30fb", TextOf(module.Chr(L(0x8145))), "a character the code page defines, whatever its decoder puts in for others");
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(module.Chr(L(65536))));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(module.Chr(L(-32769))));

        // the result is "a single character": two single-byte characters are not one, and nor is what the decoder puts in for a
        // lead byte with a trail byte the code page does not define.
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(module.Chr(L(0x4142))), "\"AB\"");
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(module.Chr(L(0x100))), "Chr(1) & Chr(0)");
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(module.Chr(L(0x8100))));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(module.Chr(L(0x8100 - 0x10000))), "the same code, negative");
    }

    [TestMethod]
    public void Chr_From128To255_OnADoubleByteCodePage_IsError5_UnlessItIsOneCharacter()
    {
        var module = Module(codePage: 932);

        // a lone lead byte is no character; a single-byte code page reads &H81 as its character
        // (Chr_From128To255_IsTheCharacterOfTheAnsiCodePage).
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(module.Chr(L(0x81))));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(module.ChrString(L(0x81))));
        Assert.AreEqual("ｱ", TextOf(module.Chr(L(0xB1))), "a single-byte character of the double-byte code page");
    }

    [TestMethod]
    public void ChrString_IsChr_AsAString()
    {
        var result = Module().ChrString(L(65));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Verbose);
        Assert.IsInstanceOfType<VBStringValue>(result.Result);
        Assert.AreEqual("A", result.Result.Value);
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().ChrString(L(256))));
    }

    [TestMethod]
    public void Chr_FromSource_TakesItsCodeLetCoerced()
    {
        // 65.5 rounds to the even 66; True is -1.
        CollectionAssert.AreEqual(new[] { "B", "A" }, Run("Debug.Print Chr(65.5)", "Debug.Print Chr(\"65\")"));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, RunToError("Debug.Print Chr(Null)"));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, RunToError("Debug.Print Chr(True)"));
    }

    [TestMethod]
    public void ChrDollar_FromSource_CallsTheStringTwin()
    {
        var (lines, members) = RunSpied("Debug.Print Chr$(65)");

        Assert.AreEqual("A", lines.Single());
        Assert.AreEqual("Chr$", members.Single());
    }
}
