using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.StdLib;
using static RDCore.Tests.Cli.ModuleWorkspace;
using static RDCore.Tests.Runtime.StdLib.StdStringsTestHarness;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.11.1.14 InStr / InStrB</strong>: "the position of the first occurrence of one string within another", or
/// with <c>InStrB</c> "the byte position", and 0 when there is none.
/// </summary>
/// <remarks>
/// With a third argument the first is the start, and without one it is the String to search. A Compare left out is the calling
/// module's <c>Option Compare</c>, filled in at the call site, so a direct call always names one.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11.1.14 InStr / InStrB")]
public sealed class InStrAndInStrBTests
{
    private const VBCompareMethod Binary = VBCompareMethod.VBBinaryCompare;
    private const VBCompareMethod Text = VBCompareMethod.VBTextCompare;

    [TestMethod]
    public void InStr_WithTwoArguments_SearchesTheFirstForTheSecond()
        => Assert.AreEqual(9, LongOf(Module().InStr(V("XXpXXpXXPXXP"), V("P"), null, Binary)));

    [TestMethod]
    public void InStr_WithThreeArguments_StartsWhereTheFirstSays()
    {
        Assert.AreEqual(6, LongOf(Module().InStr(V(4), V("XXpXXpXXPXXP"), V("P"), Text)));
        Assert.AreEqual(9, LongOf(Module().InStr(V(1), V("XXpXXpXXPXXP"), V("P"), Binary)));
        Assert.AreEqual(0, LongOf(Module().InStr(V(1), V("XXpXXpXXPXXP"), V("W"), Binary)));
    }

    [TestMethod]
    public void InStr_InAZeroLengthString_IsZero_EvenForAZeroLengthPattern()
    {
        Assert.AreEqual(0, LongOf(Module().InStr(V(""), V(""), null, Binary)));
        Assert.AreEqual(0, LongOf(Module().InStr(V(1), V(""), V(""), Binary)));
        Assert.AreEqual(0, LongOf(Module().InStr(V(""), V("a"), null, Binary)));
    }

    [TestMethod]
    [DataRow(1, 1)]
    [DataRow(3, 3)]
    [DataRow(4, 4)]
    [DataRow(5, 0)]
    public void InStr_OfAZeroLengthPattern_IsTheStart_UpToOnePastTheEnd(int start, int expected)
    {
        Assert.AreEqual(expected, LongOf(Module().InStr(V(start), V("abc"), V(""), Binary)));
        Assert.AreEqual(1, LongOf(Module().InStr(V("abc"), V(""), null, Binary)));
    }

    [TestMethod]
    [DataRow(4)]
    [DataRow(100)]
    [DataRow(int.MaxValue)]
    public void InStr_StartingPastTheEnd_IsZero(int start)
        => Assert.AreEqual(0, LongOf(Module().InStr(V(start), V("abc"), V("b"), Binary)));

    [TestMethod]
    public void InStr_AMatchMayStartAnywhereFromTheStart()
    {
        Assert.AreEqual(1, LongOf(Module().InStr(V(1), V("aaa"), V("aa"), Binary)));
        Assert.AreEqual(2, LongOf(Module().InStr(V(2), V("aaa"), V("aa"), Binary)));
        Assert.AreEqual(0, LongOf(Module().InStr(V(1), V("abc"), V("abcd"), Binary)));
    }

    [TestMethod]
    [DataRow(0, "b")]
    [DataRow(-1, "b")]
    [DataRow(0, "")]
    public void InStr_WithAStartBelowOne_IsError5(int start, string pattern)
    {
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().InStr(V(start), V("abc"), V(pattern), Binary)));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().InStrB(V(start), V("abc"), V(pattern), Binary)));
    }

    [TestMethod]
    public void InStr_OfNull_IsNull()
    {
        Assert.IsTrue(IsNull(Module().InStr(V(1), VNull, V("a"), Binary)));
        Assert.IsTrue(IsNull(Module().InStr(V(1), V("a"), VNull, Binary)));
        Assert.IsTrue(IsNull(Module().InStr(VNull, V("a"), null, Binary)));
        Assert.IsTrue(IsNull(Module().InStr(V("a"), VNull, null, Binary)));
    }

    [TestMethod]
    public void InStr_OfNull_IsNull_BeforeItsStartOrItsCompareIsChecked()
    {
        Assert.IsTrue(IsNull(Module().InStr(V(0), VNull, V("a"), Binary)));
        Assert.IsTrue(IsNull(Module().InStr(V(1), VNull, V("a"), (VBCompareMethod)2)));
    }

    [TestMethod]
    public void InStr_WithANullStart_IsError94()
        // "If start contains the data value Null, an error occurs."
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, ErrorOf(Module().InStr(VNull, V("abc"), V("b"), Binary)));

    [TestMethod]
    public void InStr_CoercesItsArgumentsInDeclarationOrder()
        // the start's coercion comes before the String's: 94, not 13.
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, ErrorOf(Module().InStr(VNull, V(new VBErrorValue(5)), V("b"), Binary)));

    [TestMethod]
    public void InStr_TheStartIsLetCoercedToALong()
    {
        // 1.5 and 2.5 round to the even 2, 3.5 to 4; True is -1 and Empty 0, both below 1.
        Assert.AreEqual(2, LongOf(Module().InStr(V(1.5), V("abcb"), V("b"), Binary)));
        Assert.AreEqual(2, LongOf(Module().InStr(V(2.5), V("bbbb"), V("b"), Binary)));
        Assert.AreEqual(4, LongOf(Module().InStr(V(3.5), V("bbbb"), V("b"), Binary)));
        Assert.AreEqual(4, LongOf(Module().InStr(V("3"), V("abcb"), V("b"), Binary)));
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().InStr(V("x"), V("abcb"), V("b"), Binary)));
        Assert.AreEqual((int)VBRuntimeErrorId.Overflow, ErrorOf(Module().InStr(V(2147483648.0), V("abcb"), V("b"), Binary)));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().InStr(V(true), V("abcb"), V("b"), Binary)));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().InStr(VEmpty, V("abcb"), V("b"), Binary)));
    }

    [TestMethod]
    public void InStr_TheStringsAreLetCoercedToString()
    {
        // without a third argument 1 is the String to search, and an explicit Empty pattern is a zero-length one.
        Assert.AreEqual(2, LongOf(Module().InStr(V(21), V("1"), null, Binary)));
        Assert.AreEqual(0, LongOf(Module().InStr(V(1), V("abc"), null, Binary)));
        Assert.AreEqual(2, LongOf(Module().InStr(V(1), V("a-1"), V(-1), Binary)));
        Assert.AreEqual(2, LongOf(Module().InStr(V(1), V("a1.5b"), V(1.5), Binary)));
        Assert.AreEqual(0, LongOf(Module().InStr(V(1), VEmpty, V("a"), Binary)));
        Assert.AreEqual(1, LongOf(Module().InStr(V(1), V("abc"), VEmpty, Binary)));
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().InStr(V(1), V("abc"), VMissing, Binary)));
    }

    [TestMethod]
    public void InStr_WithoutAStringToSearchOrAPattern_IsError449()
    {
        Assert.AreEqual((int)VBRuntimeErrorId.ArgumentNotOptional, ErrorOf(Module().InStr(null, null, null, Binary)));
        Assert.AreEqual((int)VBRuntimeErrorId.ArgumentNotOptional, ErrorOf(Module().InStr(V("abc"), null, null, Binary)));
        Assert.AreEqual((int)VBRuntimeErrorId.ArgumentNotOptional, ErrorOf(Module().InStr(V(1), null, V("b"), Binary)));
        Assert.AreEqual((int)VBRuntimeErrorId.ArgumentNotOptional, ErrorOf(Module().InStrB(V("abc"), null, null, Binary)));
    }

    [TestMethod]
    public void InStr_WithTheStartLeftOut_StartsAtOne()
        => Assert.AreEqual(2, LongOf(Module().InStr(null, V("abc"), V("b"), Binary)));

    [TestMethod]
    [DataRow(-1)]
    [DataRow(240)]
    [DataRow(5000)]
    public void InStr_WithACompareThatIsNoModeNorLocale_IsError5(int compare)
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().InStr(V(1), V("abc"), V("b"), (VBCompareMethod)compare)));

    [TestMethod]
    public void InStr_WithVbDatabaseCompare_IsError5()
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().InStr(V(1), V("abc"), V("b"), (VBCompareMethod)2)));

    [TestMethod]
    [DataRow(7)]
    [DataRow(1033)]
    public void InStr_WithACompareThatIsALocale_SearchesTheText(int compare)
        => Assert.AreEqual(2, LongOf(Module().InStr(V(1), V("abc"), V("B"), (VBCompareMethod)compare)));

    [TestMethod]
    public void InStr_ComparesAsItsCompareSays()
    {
        Assert.AreEqual(2, LongOf(Module().InStr(V(1), V("abc"), V("B"), Text)));
        Assert.AreEqual(0, LongOf(Module().InStr(V(1), V("abc"), V("B"), Binary)));
        Assert.AreEqual(1, LongOf(Module().InStr(V(1), V("aaa"), V("AA"), Text)));
    }

    [TestMethod]
    public void InStr_InTextMode_FollowsTheEnvironmentsCulture()
    {
        // Turkish cases i to a dotted capital I; the invariant culture does not.
        Assert.AreEqual(2, LongOf(Module(lcid: 1055).InStr(V(1), V("xİx"), V("i"), Text)));
        Assert.AreEqual(0, LongOf(Module().InStr(V(1), V("xİx"), V("i"), Text)));
    }

    [TestMethod]
    public void InStr_CountsCodeUnits()
        => Assert.AreEqual(3, LongOf(Module().InStr(V(1), V("a😀"), V("\uDE00"), Binary)));

    [TestMethod]
    public void InStr_OfAStringWithATrailingByte_SearchesItsCharacters()
        // one character, U+4241, and a byte: the byte is not searched.
        => Assert.AreEqual(1, LongOf(Module().InStr(V(Bytes(0x41, 0x42, 0x43)), V("䉁"), null, Binary)));

    [TestMethod]
    public void InStrB_IsABytePosition()
    {
        Assert.AreEqual(3, LongOf(Module().InStrB(V("abc"), V("b"), null, Binary)));
        Assert.AreEqual(3, LongOf(Module().InStrB(V(2), V("abc"), V("b"), Binary)));
        Assert.AreEqual(0, LongOf(Module().InStrB(V(4), V("abc"), V("b"), Binary)));
    }

    [TestMethod]
    public void InStrB_MayMatchBetweenCharacters()
        // "AB" is 41 00 42 00, and U+4200 is 00 42.
        => Assert.AreEqual(2, LongOf(Module().InStrB(V("AB"), V("䈀"), null, Binary)));

    [TestMethod]
    public void InStrB_FindsASingleByte()
    {
        Assert.AreEqual(3, LongOf(Module().InStrB(V("abc"), V(Bytes(0x62)), null, Binary)));
        Assert.AreEqual(2, LongOf(Module().InStrB(V("abc"), V(Bytes(0x00)), null, Binary)));
        Assert.AreEqual(3, LongOf(Module().InStrB(V(Bytes(0x41, 0x00, 0x42)), V(Bytes(0x42)), null, Binary)));
    }

    [TestMethod]
    [DataRow(1, 1)]
    [DataRow(7, 7)]
    [DataRow(8, 0)]
    public void InStrB_OfAZeroLengthPattern_IsTheStart_UpToOnePastTheLastByte(int start, int expected)
        => Assert.AreEqual(expected, LongOf(Module().InStrB(V(start), V("abc"), V(""), Binary)));

    [TestMethod]
    public void InStrB_EdgeCases()
    {
        Assert.AreEqual(0, LongOf(Module().InStrB(V(""), V(""), null, Binary)));
        Assert.IsTrue(IsNull(Module().InStrB(VNull, V("a"), null, Binary)));
        Assert.IsTrue(IsNull(Module().InStrB(V(1), V("abc"), VNull, Binary)));
    }

    [TestMethod]
    [DataRow(1, DisplayName = "vbTextCompare")]
    [DataRow(2, DisplayName = "vbDatabaseCompare")]
    [DataRow(7, DisplayName = "a neutral language")]
    [DataRow(1033, DisplayName = "a locale")]
    [DataRow(240, DisplayName = "no locale")]
    [DataRow(-1, DisplayName = "a negative number")]
    public void InStrB_SearchesTheBytes_WhateverItsCompareIs(int compare)
    {
        Assert.AreEqual(0, LongOf(Module().InStrB(V(1), V("xAx"), V("a"), (VBCompareMethod)compare)));
        Assert.AreEqual(3, LongOf(Module().InStrB(V(1), V("xAx"), V("A"), (VBCompareMethod)compare)));
    }

    [TestMethod]
    public void InStr_FromSource_TheOfficeExamples()
        => CollectionAssert.AreEqual(new[] { "6", "9" }, Run("Debug.Print InStr(4, \"XXpXXpXXPXXP\", \"P\", 1)", "Debug.Print InStr(\"XXpXXpXXPXXP\", \"P\")"));

    [TestMethod]
    [DataRow(null, "2", "2", "3", DisplayName = "no Option Compare")]
    [DataRow(OptionCompare.Binary, "2", "2", "3", DisplayName = "Option Compare Binary")]
    [DataRow(OptionCompare.Text, "1", "1", "2", DisplayName = "Option Compare Text")]
    // the environment's DatabaseCompare, which is Text unless it says otherwise.
    [DataRow(OptionCompare.Database, "1", "1", "2", DisplayName = "Option Compare Database")]
    public void InStr_FromSource_WithCompareLeftOut_FollowsTheModulesOptionCompare(OptionCompare? mode, string two, string three, string fromTwo)
        => CollectionAssert.AreEqual(new[] { two, three, fromTwo },
            RunUnder(mode, "Debug.Print InStr(\"aA\", \"A\")", "Debug.Print InStr(1, \"aA\", \"A\")", "Debug.Print InStr(2, \"aaA\", \"A\")"));

    [TestMethod]
    public void InStr_FromSource_AnExplicitCompare_OverridesTheModules()
    {
        Assert.AreEqual("2", RunUnder(OptionCompare.Text, "Debug.Print InStr(1, \"aA\", \"A\", 0)").Single());
        Assert.AreEqual("1", RunUnder(OptionCompare.Binary, "Debug.Print InStr(1, \"aA\", \"A\", 1)").Single());
        Assert.AreEqual("1", RunUnder(null, "Debug.Print InStr(1, \"aA\", \"A\", vbTextCompare)").Single());
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, RunToErrorUnder(OptionCompare.Binary, "Debug.Print InStr(1, \"aA\", \"A\", 2)"));
    }

    [TestMethod]
    public void InStr_FromSource_WithACompareButNoStart_IsNotRefused()
        => Assert.AreEqual("1", RunUnder(OptionCompare.Binary, "Debug.Print InStr(\"aA\", \"A\", , 1)").Single());

    [TestMethod]
    public void InStr_FromSource_AMatchUnderOptionCompareText_IsEqualToThePattern()
        => CollectionAssert.AreEqual(new[] { "3", "True" },
            RunUnder(OptionCompare.Text, "Dim k As Long", "k = InStr(1, \"abCDef\", \"cd\", 1)", "Debug.Print k", "Debug.Print Mid(\"abCDef\", k, 2) = \"cd\""));

    [TestMethod]
    public void InStr_FromSource_ANullCharPattern_IsFoundWhereItIs()
        => CollectionAssert.AreEqual(new[] { "3", "0" },
            RunUnder(OptionCompare.Text, "Debug.Print InStr(1, \"ab\" & Chr(0) & \"c\", Chr(0), 1)", "Debug.Print InStr(1, \"abc\", Chr(0), 1)"));

    [TestMethod]
    public void InStr_FromSource_ASoftHyphen_IsNotLookedThrough()
        => Assert.AreEqual("0", RunUnder(OptionCompare.Text, "Debug.Print InStr(1, \"a\" & ChrW(173) & \"bc\", \"ab\", 1)").Single());

    [TestMethod]
    public void InStr_FromSource_TakesItsStartLetCoerced()
    {
        Assert.AreEqual("2", Run("Debug.Print InStr(1.5, \"abcb\", \"b\")").Single());
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, RunToError("Debug.Print InStr(Null, \"abc\", \"b\")"));
        // V is Empty, which is the start 0.
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, RunToError("Debug.Print InStr(V, \"abc\", \"b\")"));
        Assert.AreEqual("True", Run("Debug.Print IsNull(InStr(1, Null, \"b\"))").Single());
    }

    [TestMethod]
    public async Task InStr_FromSource_ACallersMissingThirdArgument_MeansTheTwoArgumentForm()
    {
        // an omitted Optional Variant is Missing, and passed on to InStr's third argument it is that argument left out.
        var printed = await RunAsync([],
            "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" +
            "Private Function Find(Optional v As Variant) As Variant\r\n    Find = InStr(\"abc\", \"b\", v)\r\nEnd Function\r\n" +
            "Public Sub Main()\r\n    Debug.Print Find()\r\nEnd Sub\r\n");

        CollectionAssert.AreEqual(new[] { "2" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task InStr_FromSource_InAClassModule_FollowsThatModulesOptionCompare()
    {
        // Finder states Option Compare Text and Program none: each call of InStr takes the Option Compare of the module whose code makes it, and InStrB
        // searches the bytes in either.
        var finder = ClassModule("Finder", "Option Explicit", "Option Compare Text",
            "Public Function Find(ByVal s As String, ByVal p As String) As Variant", "    Find = InStr(s, p)", "End Function",
            "Public Function FindB(ByVal s As String, ByVal p As String) As Variant", "    FindB = InStrB(s, p)", "End Function");
        var printed = await RunAsync([("Finder", finder)],
            "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" +
            "Public Sub Main()\r\n    Dim f As Finder\r\n    Set f = New Finder\r\n" +
            "    Debug.Print f.Find(\"aA\", \"A\")\r\n    Debug.Print InStr(\"aA\", \"A\")\r\n" +
            "    Debug.Print f.FindB(\"aA\", \"A\")\r\n    Debug.Print InStrB(\"aA\", \"A\")\r\nEnd Sub\r\n");

        CollectionAssert.AreEqual(new[] { "1", "2", "3", "3" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    [DataRow(null, "3")]
    [DataRow(OptionCompare.Binary, "3")]
    [DataRow(OptionCompare.Text, "3")]
    public void InStrB_FromSource_WithCompareLeftOut_SearchesTheBytes_WhateverTheModulesOptionCompare(OptionCompare? mode, string expected)
        => Assert.AreEqual(expected, RunUnder(mode, "Debug.Print InStrB(\"aA\", \"A\")").Single());
}
