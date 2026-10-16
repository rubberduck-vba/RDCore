using RDCore.SDK.Model.Errors;
using static RDCore.Tests.Runtime.StdLib.StdStringsTestHarness;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.11.1.23 LTrim / RTrim / Trim</strong>: "a copy of a specified string without leading spaces (LTrim),
/// trailing spaces (RTrim), or both leading and trailing spaces (Trim)" - spaces, U+0020, and no other whitespace.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11.1.23 LTrim / RTrim / Trim")]
[TestCategory("MS-VBAL 6.1.2.11.1.24 LTrim$ / RTrim$ / Trim$")]
public sealed class TrimTests
{
    [TestMethod]
    public void Trim_RemovesTheSpaces()
    {
        Assert.AreEqual("<-Trim-> ", TextOf(Module().LTrim(V(" <-Trim-> "))));
        Assert.AreEqual(" <-Trim->", TextOf(Module().RTrim(V(" <-Trim-> "))));
        Assert.AreEqual("<-Trim->", TextOf(Module().Trim(V(" <-Trim-> "))));
        Assert.AreEqual("a b", TextOf(Module().Trim(V(" a b "))));
        Assert.AreEqual(string.Empty, TextOf(Module().Trim(V("   "))));
        Assert.AreEqual(string.Empty, TextOf(Module().Trim(V(string.Empty))));
    }

    [TestMethod]
    [DataRow("\tx\t", DisplayName = "tabs")]
    [DataRow("\r\nx\r\n", DisplayName = "line breaks")]
    [DataRow("\0x\0", DisplayName = "null characters")]
    [DataRow("\u00A0x\u00A0", DisplayName = "no-break spaces")]
    [DataRow("\u2003x\u2003", DisplayName = "em spaces")]
    [DataRow("\u3000x\u3000", DisplayName = "ideographic spaces")]
    public void Trim_RemovesNothingButU0020(string text)
    {
        Assert.AreEqual(text, TextOf(Module().Trim(V(text))));
        Assert.AreEqual(text, TextOf(Module().LTrim(V(text))));
        Assert.AreEqual(text, TextOf(Module().RTrim(V(text))));
    }

    [TestMethod]
    public void Trim_OfNullEmptyOrANumber()
    {
        Assert.IsTrue(IsNull(Module().Trim(VNull)));
        Assert.IsTrue(IsNull(Module().LTrim(VNull)));
        Assert.IsTrue(IsNull(Module().RTrim(VNull)));
        Assert.AreEqual(string.Empty, TextOf(Module().Trim(VEmpty)));
        Assert.AreEqual("12", TextOf(Module().Trim(V(12))));
        Assert.AreEqual("-1.5", TextOf(Module().Trim(V(-1.5))));
    }

    [TestMethod]
    public void TrimTwins_OfNull_AreError94()
    {
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, ErrorOf(Module().TrimString(VNull)));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, ErrorOf(Module().LTrimString(VNull)));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, ErrorOf(Module().RTrimString(VNull)));
    }

    [TestMethod]
    public void Trim_OfMissing_IsError13()
        => Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().Trim(VMissing)));

    [TestMethod]
    public void TrimDollar_FromSource()
    {
        Assert.AreEqual("12", Run("Debug.Print Trim$(12)").Single());
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, RunToError("Debug.Print Trim$(Null)"));
    }

    [TestMethod]
    public void Trim_FromSource_OfAFixedLengthString_RemovesItsPadding()
        => CollectionAssert.AreEqual(new[] { "[ab]", "[ab   ]", "[ab]" }, Run(
            "Dim f As String * 5", "f = \"ab\"",
            "Debug.Print \"[\" & RTrim(f) & \"]\"", "Debug.Print \"[\" & LTrim(f) & \"]\"", "Debug.Print \"[\" & Trim$(f) & \"]\""));
}
