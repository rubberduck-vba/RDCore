using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using static RDCore.Tests.Runtime.StdLib.StdStringsTestHarness;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.11.1.19 Left / LeftB</strong>: the first Length characters of a String, or with <c>LeftB</c> its first
/// Length bytes; "if string contains Null, Null is returned".
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11.1.19 Left / LeftB")]
[TestCategory("MS-VBAL 6.1.2.11.1.20 Left$")]
[TestCategory("MS-VBAL 6.1.2.11.1.21 LeftB$")]
public sealed class LeftAndLeftBTests
{
    [TestMethod]
    [DataRow("Hello World", 7, "Hello W")]
    [DataRow("Hello World", 20, "Hello World")]
    [DataRow("abc", int.MaxValue, "abc")]
    [DataRow("abc", 0, "")]
    public void Left_TakesTheFirstCharacters(string text, int length, string expected)
        => Assert.AreEqual(expected, TextOf(Module().Left(V(text), L(length))));

    [TestMethod]
    public void Left_OfANegativeLength_IsError5()
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().Left(V("abc"), L(-1))));

    [TestMethod]
    [DataRow(1)]
    [DataRow(-1)]
    public void Left_OfNull_IsNull_WhateverTheLength(int length)
    {
        Assert.IsTrue(IsNull(Module().Left(VNull, L(length))));
        Assert.IsTrue(IsNull(Module().LeftB(VNull, L(length))));
    }

    [TestMethod]
    public void Left_TakesAnyValuesString()
    {
        Assert.AreEqual(string.Empty, TextOf(Module().Left(VEmpty, L(1))));
        Assert.AreEqual("12", TextOf(Module().Left(V(12345), L(2))));
        Assert.AreEqual("Tr", TextOf(Module().Left(V(true), L(2))));
        Assert.AreEqual("1.", TextOf(Module().Left(V(1.5), L(2))));
    }

    [TestMethod]
    public void Left_MaySplitASurrogatePair()
    {
        var pair = char.ConvertFromUtf32(0x1F600);

        Assert.AreEqual(pair[0].ToString(), TextOf(Module().Left(V(pair), L(1))));
    }

    [TestMethod]
    public void Left_OfAStringWithATrailingByte_TakesCharacters()
    {
        var left = StringOf(Module().Left(V(Bytes(0x41, 0x42, 0x43)), L(1)));

        Assert.AreEqual("\u4241", left.Value);
        Assert.AreEqual(2, left.ByteLength);
    }

    [TestMethod]
    public void LeftB_TakesTheFirstBytes()
    {
        Assert.AreEqual("a", TextOf(Module().LeftB(V("abc"), L(2))));
        CollectionAssert.AreEqual(new byte[] { 0x61, 0x00, 0x62 }, BytesOf(Module().LeftB(V("abc"), L(3))));
        CollectionAssert.AreEqual(new byte[] { 0x61 }, BytesOf(Module().LeftB(V("abc"), L(1))));
        Assert.AreEqual(string.Empty, TextOf(Module().LeftB(V("abc"), L(0))));
        Assert.AreEqual("abc", TextOf(Module().LeftB(V("abc"), L(7))));
        CollectionAssert.AreEqual(new byte[] { 0x41 }, BytesOf(Module().LeftB(V(Bytes(0x41)), L(2))));
    }

    [TestMethod]
    public void LeftB_OfANegativeLength_IsError5()
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().LeftB(V("abc"), L(-1))));

    [TestMethod]
    public void LeftTwins_OfNull_AreError94()
    {
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, ErrorOf(Module().LeftString(VNull, L(1))));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, ErrorOf(Module().LeftBString(VNull, L(1))));

        var twin = Module().LeftString(V("abc"), L(2));
        Assert.IsInstanceOfType<VBStringValue>(twin.Result);
        Assert.AreEqual("ab", twin.Result.Value);
    }

    [TestMethod]
    public void Left_OfMissing_IsError13()
        // a required Variant parameter receives a caller's Missing whole, and Missing has no String form.
        => Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().Left(VMissing, L(1))));

    [TestMethod]
    public void Left_OfNothing_IsError91()
    {
        // Nothing, Let-coerced to a String, is error 91 (MS-VBAL 5.5.1.2.13), as it is for StrReverse's typed parameter.
        Assert.AreEqual((int)VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet, ErrorOf(Module().Left(V(VBObjectValue.Nothing), L(1))));
        Assert.AreEqual((int)VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet, RunToError("Debug.Print Left(Nothing, 1)"));
        Assert.AreEqual((int)VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet, RunToError("Debug.Print StrReverse(Nothing)"));
    }

    [TestMethod]
    public void Left_OfAnObject_IsError13()
        // 🚧 an object that is not Nothing: its default member is not read.
        => Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().Left(V(new VBObjectValue(new VBRuntimeObjectId())), L(1))));

    [TestMethod]
    public void Left_FromSource_TakesItsLengthLetCoerced()
    {
        // 1.5 and 2.5 both round to the even 2.
        CollectionAssert.AreEqual(new[] { "ab", "ab", "ab" }, Run(
            "Debug.Print Left(\"abc\", 1.5)", "Debug.Print Left(\"abc\", 2.5)", "Debug.Print Left(\"abc\", \"2\")"));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, RunToError("Debug.Print Left(\"abc\", Null)"));
        Assert.AreEqual((int)VBRuntimeErrorId.Overflow, RunToError("Debug.Print Left(\"abc\", 2147483648#)"));
    }

    [TestMethod]
    public void LeftDollar_FromSource_IsTheStringTwin()
    {
        Assert.AreEqual("Null", Run("Debug.Print Left(Null, 1)").Single());
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, RunToError("Debug.Print Left$(Null, 1)"));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, RunToError("Debug.Print Left$(\"abc\", -1)"));

        var (lines, members) = RunSpied("Debug.Print Left$(\"abc\", 2)");
        Assert.AreEqual("ab", lines.Single());
        Assert.AreEqual("Left$", members.Single());
    }

    [TestMethod]
    public void LeftB_FromSource_OfAnOddLength_HasThatManyBytes()
        => CollectionAssert.AreEqual(new[] { "3", "1" }, Run("Debug.Print LenB(LeftB(\"abc\", 3))", "Debug.Print LeftB(12345, 2)"));

    [TestMethod]
    public void Left_FromSource_Qualified()
        => CollectionAssert.AreEqual(new[] { "ab", "a" }, Run("Debug.Print VBA.Left$(\"abc\", 2)", "Debug.Print VBA.Strings.Left(\"abc\", 1)"));

    [TestMethod]
    public void Left_FromSource_OfAFixedLengthString_SeesItsPadding()
        => Assert.AreEqual("[ab ]", Run("Dim f As String * 5", "f = \"ab\"", "Debug.Print \"[\" & Left(f, 3) & \"]\"").Single());
}
