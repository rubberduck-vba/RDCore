using RDCore.SDK.Model.Errors;
using static RDCore.Tests.Runtime.StdLib.StdStringsTestHarness;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.11.1.5 ChrB</strong>: "a String data value consisting of a single byte character", and error 6 for a
/// value outside 0 to 255.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11.1.5 ChrB / ChrB$")]
public sealed class ChrBTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(65)]
    [DataRow(128)]
    [DataRow(255)]
    public void ChrB_IsASingleByte(int code)
    {
        var text = StringOf(Module().ChrB(L(code)));

        Assert.AreEqual(1, text.ByteLength);
        Assert.AreEqual(0, text.Length, "a byte is not a character");
        CollectionAssert.AreEqual(new[] { (byte)code }, text.GetBytes());
    }

    [TestMethod]
    [DataRow(256)]
    [DataRow(-1)]
    [DataRow(65535)]
    [DataRow(65536)]
    [DataRow(-32768)]
    [DataRow(-32769)]
    public void ChrB_OutsideZeroTo255_IsError6(int code)
    {
        Assert.AreEqual((int)VBRuntimeErrorId.Overflow, ErrorOf(Module().ChrB(L(code))));
        Assert.AreEqual((int)VBRuntimeErrorId.Overflow, ErrorOf(Module().ChrBString(L(code))));
    }

    [TestMethod]
    public void ChrB_FromSource_IsOneByteAndNoCharacter()
        => CollectionAssert.AreEqual(new[] { "0", "1" }, Run("Debug.Print Len(ChrB(65))", "Debug.Print LenB(ChrB(65))"));

    [TestMethod]
    public void ChrB_FromSource_TwoOfThem_AreOneCharacter()
        => CollectionAssert.AreEqual(new[] { "True", "3", "False", "True" }, Run(
            "Debug.Print ChrB(65) & ChrB(66) = ChrW(&H4241)",
            "Debug.Print LenB(ChrB(65) & \"C\")",
            "Debug.Print ChrB(65) = \"A\"",
            "Debug.Print (ChrB(65) & ChrB(0)) = \"A\""));
}
