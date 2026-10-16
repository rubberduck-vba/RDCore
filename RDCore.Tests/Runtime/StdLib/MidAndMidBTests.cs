using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Intrinsic;
using static RDCore.Tests.Cli.ModuleWorkspace;
using static RDCore.Tests.Runtime.StdLib.StdStringsTestHarness;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.11.1.25 Mid / MidB</strong>: Length characters of a String from Start, or with <c>MidB</c> bytes; "if
/// omitted or if there are fewer than Length characters in the text (including the character at start), all characters from the
/// start position to the end of the string are returned", and a Start past the end is a zero-length string.
/// </summary>
/// <remarks>
/// Length is a <c>Variant</c>, so the member coerces it itself, after the String and before the String's Null.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11.1.25 Mid / MidB")]
[TestCategory("MS-VBAL 6.1.2.11.1.26 Mid$")]
[TestCategory("MS-VBAL 6.1.2.11.1.27 MidB$")]
public sealed class MidAndMidBTests
{
    [TestMethod]
    public void Mid_TakesCharactersFromTheStart()
    {
        Assert.AreEqual("Demo", TextOf(Module().Mid(V("Mid Function Demo"), L(14), V(4))));
        Assert.AreEqual("Function Demo", TextOf(Module().Mid(V("Mid Function Demo"), L(5))));
        Assert.AreEqual("bc", TextOf(Module().Mid(V("abc"), L(2), V(100))));
        Assert.AreEqual(string.Empty, TextOf(Module().Mid(V("abc"), L(2), V(0))));
    }

    [TestMethod]
    [DataRow(4)]
    [DataRow(5)]
    public void Mid_StartingPastTheEnd_IsAZeroLengthString(int start)
        => Assert.AreEqual(string.Empty, TextOf(Module().Mid(V("abc"), L(start))));

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void Mid_WithAStartBelowOne_IsError5(int start)
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().Mid(V("abc"), L(start))));

    [TestMethod]
    [DataRow(5, -1)]
    [DataRow(1, -1)]
    [DataRow(2, -2)]
    public void Mid_WithANegativeLength_IsError5_EvenPastTheEnd(int start, int length)
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().Mid(V("abc"), L(start), V(length))));

    [TestMethod]
    [DataRow(1)]
    [DataRow(0)]
    public void Mid_OfNull_IsNull_WhateverTheStart(int start)
    {
        Assert.IsTrue(IsNull(Module().Mid(VNull, L(start))));
        Assert.IsTrue(IsNull(Module().MidB(VNull, L(start))));
    }

    [TestMethod]
    public void Mid_TheLengthIsLetCoercedToALong()
    {
        // 1.5 rounds to the even 2; Empty is 0.
        Assert.AreEqual("bc", TextOf(Module().Mid(V("abc"), L(2), V(1.5))));
        Assert.AreEqual(string.Empty, TextOf(Module().Mid(V("abc"), L(2), VEmpty)));
        Assert.AreEqual("b", TextOf(Module().Mid(V("abc"), L(2), V("1"))));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, ErrorOf(Module().Mid(V("abc"), L(2), VNull)));
        Assert.AreEqual((int)VBRuntimeErrorId.Overflow, ErrorOf(Module().Mid(V("abc"), L(2), V(3000000000.0))));
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().Mid(V("abc"), L(2), V("x"))));
    }

    [TestMethod]
    public void Mid_TakesAnyValuesString()
    {
        Assert.AreEqual("23", TextOf(Module().Mid(V(12345), L(2), V(2))));
        Assert.AreEqual("rue", TextOf(Module().Mid(V(true), L(2))));
        Assert.AreEqual(".5", TextOf(Module().Mid(V(1.5), L(2))));
    }

    [TestMethod]
    public void Mid_OfAStringWithATrailingByte_SeesItsCharactersOnly()
        // one character and a byte: there is no second character.
        => Assert.AreEqual(string.Empty, TextOf(Module().Mid(V(Bytes(0x41, 0x42, 0x43)), L(2))));

    [TestMethod]
    public void MidB_TakesBytes()
    {
        Assert.AreEqual("bC", TextOf(Module().MidB(V("AbCdEfG"), L(3), V(4))));
        Assert.AreEqual("\u4200", TextOf(Module().MidB(V("AB"), L(2), V(2))));
        CollectionAssert.AreEqual(new byte[] { 0x00, 0x62, 0x00 }, BytesOf(Module().MidB(V("abc"), L(2), V(3))));
        Assert.AreEqual("bc", TextOf(Module().MidB(V("abc"), L(3))));
        CollectionAssert.AreEqual(new byte[] { 0x00, 0x62, 0x00, 0x63, 0x00 }, BytesOf(Module().MidB(V("abc"), L(2))));
        Assert.AreEqual(string.Empty, TextOf(Module().MidB(V("abc"), L(7))));
        CollectionAssert.AreEqual(new byte[] { 0x42 }, BytesOf(Module().MidB(V(Bytes(0x41, 0x42, 0x43)), L(2), V(1))));
    }

    [TestMethod]
    public void MidB_Errors()
    {
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().MidB(V("abc"), L(0))));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().MidB(V("abc"), L(1), V(-1))));
    }

    [TestMethod]
    public void MidTwins_OfNull_AreError94()
    {
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, ErrorOf(Module().MidString(VNull, L(1))));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, ErrorOf(Module().MidString(V("abc"), L(2), VNull)));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, ErrorOf(Module().MidBString(VNull, L(1))));
        Assert.AreEqual(string.Empty, TextOf(Module().MidString(V("abc"), L(4))));
    }

    [TestMethod]
    public void Mid_CoercesItsArgumentsInDeclarationOrder()
        // the String's coercion comes before the Length's: 13, not 94.
        => Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().Mid(V(new VBErrorValue(5)), L(1), VNull)));

    [TestMethod]
    public void Mid_FromSource_TakesItsStartLetCoerced()
    {
        Assert.AreEqual("bc", Run("Debug.Print Mid(\"abc\", 1.5)").Single());
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, RunToError("Debug.Print Mid(\"abc\", Null)"));
        // V is Empty, which is the Start 0.
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, RunToError("Debug.Print Mid(\"abc\", V)"));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, RunToError("Debug.Print Mid$(\"abc\", 0)"));
    }

    [TestMethod]
    public async Task Mid_FromSource_ACallersMissingLength_TakesTheRest()
    {
        // an omitted Optional Variant is Missing, and passed on to Mid's Length it is Length left out.
        var printed = await RunAsync([],
            "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" +
            "Private V As Variant\r\n" +
            "Private Function MidFrom(s As String, Optional n As Variant) As String\r\n    MidFrom = Mid(s, 2, n)\r\nEnd Function\r\n" +
            "Public Sub Main()\r\n    Debug.Print MidFrom(\"abc\")\r\n    Debug.Print \"[\" & MidFrom(\"abc\", V) & \"]\"\r\nEnd Sub\r\n");

        CollectionAssert.AreEqual(new[] { "bc", "[]" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public void MidDollar_FromSource()
    {
        CollectionAssert.AreEqual(new[] { "23", "0" }, Run("Debug.Print Mid$(12345, 2, 2)", "Debug.Print AscB(MidB(\"AB\", 2, 1))"));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, RunToError("Debug.Print Mid$(Null, 1)"));
    }

    [TestMethod]
    public void Mid_FromSource_OfAFixedLengthString_SeesItsPadding()
        => Assert.AreEqual("[b   ]", Run("Dim f As String * 5", "f = \"ab\"", "Debug.Print \"[\" & Mid(f, 2) & \"]\"").Single());
}
