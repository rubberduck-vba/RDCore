using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Intrinsic;
using static RDCore.Tests.Runtime.StdLib.StdStringsTestHarness;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.11.1.2 AscB</strong>: "the first eight bits (the first byte) of the implementation dependent character
/// encoding of the string" - UTF-16LE here, so the low byte of the first character.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11.1.2 AscB")]
public sealed class AscBTests
{
    [TestMethod]
    [DataRow("A", (short)65)]
    [DataRow("\u4142", (short)66)]
    [DataRow("\u20AC", (short)172)]
    public void AscB_IsTheFirstByte_LowByteFirst(string text, short value)
        => Assert.AreEqual(value, IntegerOf(Module().AscB(new VBStringValue(text))));

    [TestMethod]
    public void AscB_OfAStringOfOneByte_IsThatByte()
        => Assert.AreEqual((short)200, IntegerOf(Module().AscB(Bytes(200))));

    [TestMethod]
    public void AscB_OfAZeroLengthString_IsError5()
    {
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().AscB(new VBStringValue(string.Empty))));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().AscB(VBStringValue.VBNullString)));
    }

    [TestMethod]
    public void AscB_FromSource()
    {
        Assert.AreEqual("200", Run("Debug.Print AscB(ChrB(200))").Single());
        // a String nothing has assigned is vbNullString, and reaches the member as one.
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, RunToError("Debug.Print AscB(S)"));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, RunToError("Debug.Print AscB(Null)"));
    }
}
