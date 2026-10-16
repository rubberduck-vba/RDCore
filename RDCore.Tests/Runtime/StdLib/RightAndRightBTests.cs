using RDCore.SDK.Model.Errors;
using static RDCore.Tests.Runtime.StdLib.StdStringsTestHarness;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.11.1.30 Right / RightB</strong>: the last Length characters of a String, or with <c>RightB</c> its last
/// Length bytes; "if string contains Null, Null is returned".
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11.1.30 Right / RightB")]
[TestCategory("MS-VBAL 6.1.2.11.1.31 Right$")]
[TestCategory("MS-VBAL 6.1.2.11.1.32 RightB$")]
public sealed class RightAndRightBTests
{
    [TestMethod]
    [DataRow("Hello World", 6, " World")]
    [DataRow("Hello World", 20, "Hello World")]
    [DataRow("abc", 0, "")]
    public void Right_TakesTheLastCharacters(string text, int length, string expected)
        => Assert.AreEqual(expected, TextOf(Module().Right(V(text), L(length))));

    [TestMethod]
    public void Right_OfANegativeLength_IsError5()
    {
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().Right(V("abc"), L(-1))));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().RightB(V("abc"), L(-1))));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(-1)]
    public void Right_OfNull_IsNull_WhateverTheLength(int length)
    {
        Assert.IsTrue(IsNull(Module().Right(VNull, L(length))));
        Assert.IsTrue(IsNull(Module().RightB(VNull, L(length))));
    }

    [TestMethod]
    public void Right_TakesAnyValuesString()
    {
        var pair = char.ConvertFromUtf32(0x1F600);

        Assert.AreEqual(string.Empty, TextOf(Module().Right(VEmpty, L(1))));
        Assert.AreEqual("45", TextOf(Module().Right(V(12345), L(2))));
        Assert.AreEqual("ue", TextOf(Module().Right(V(true), L(2))));
        Assert.AreEqual(".5", TextOf(Module().Right(V(1.5), L(2))));
        Assert.AreEqual(pair[1].ToString(), TextOf(Module().Right(V(pair), L(1))), "a pair may be split");
    }

    [TestMethod]
    public void RightB_TakesTheLastBytes()
    {
        Assert.AreEqual("bc", TextOf(Module().RightB(V("abc"), L(4))));
        CollectionAssert.AreEqual(new byte[] { 0x00, 0x63, 0x00 }, BytesOf(Module().RightB(V("abc"), L(3))));
        CollectionAssert.AreEqual(new byte[] { 0x00 }, BytesOf(Module().RightB(V("abc"), L(1))));
        Assert.AreEqual("abc", TextOf(Module().RightB(V("abc"), L(7))));
    }

    [TestMethod]
    public void RightTwins_OfNull_AreError94()
    {
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, ErrorOf(Module().RightString(VNull, L(1))));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, ErrorOf(Module().RightBString(VNull, L(1))));
    }

    [TestMethod]
    public void Right_FromSource()
    {
        CollectionAssert.AreEqual(new[] { "bc", "abc", ".5" }, Run(
            "Debug.Print Right(\"abc\", 1.5)", "Debug.Print Right$(\"abc\", 5)", "Debug.Print Right$(1.5, 2)"));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, RunToError("Debug.Print Right(\"abc\", Null)"));
    }

    [TestMethod]
    public void Right_FromSource_OfAFixedLengthString_SeesItsPadding()
        => Assert.AreEqual("[  ]", Run("Dim f As String * 5", "f = \"ab\"", "Debug.Print \"[\" & Right(f, 2) & \"]\"").Single());
}
