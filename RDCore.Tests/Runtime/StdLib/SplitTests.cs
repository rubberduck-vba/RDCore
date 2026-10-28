using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.Shared;
using static RDCore.Tests.Cli.ModuleWorkspace;
using static RDCore.Tests.Runtime.StdLib.StdStringsTestHarness;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.11.1.35 Split</strong>: "a zero-based, one-dimensional array containing a specified number of
/// substrings", split on a Delimiter that is a space when it is left out.
/// </summary>
/// <remarks>
/// The array's bounds and element type are asserted in C#; from source, it is joined, indexed, assigned or walked.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11.1.35 Split")]
public sealed class SplitTests
{
    private const VBCompareMethod Binary = VBCompareMethod.VBBinaryCompare;
    private const VBCompareMethod Text = VBCompareMethod.VBTextCompare;

    private static readonly VBLongValue All = L(-1);

    [TestMethod]
    public void Split_OfAZeroLengthString_IsAnEmptyZeroBasedStringArray()
    {
        foreach (var delimiter in new[] { null, V(","), V(string.Empty) })
        {
            var result = Module().Split(new VBStringValue(string.Empty), delimiter, All, Binary);

            Assert.IsEmpty(ElementsOf(result));
            Assert.AreEqual((0, -1), (ArrayOf(result).Dimensions[0].LowerBound, ArrayOf(result).Dimensions[0].UpperBound));
        }
    }

    [TestMethod]
    public void Split_WithTheDelimiterLeftOut_SplitsOnASpace()
    {
        CollectionAssert.AreEqual(new[] { "a", "b", "", "c" }, ElementsOf(Module().Split(new VBStringValue("a b  c"), null, All, Binary)));
        CollectionAssert.AreEqual(new[] { "", "a", "" }, ElementsOf(Module().Split(new VBStringValue(" a "), null, All, Binary)));
    }

    [TestMethod]
    public void Split_EveryDelimiterMakesAnElement()
    {
        CollectionAssert.AreEqual(new[] { "a", "", "b", "" }, ElementsOf(Module().Split(new VBStringValue("a,,b,"), V(","), All, Binary)));
        CollectionAssert.AreEqual(new[] { "", "a" }, ElementsOf(Module().Split(new VBStringValue(",a"), V(","), All, Binary)));
    }

    [TestMethod]
    public void Split_WithALimit_LeavesTheRestInTheLastElement()
    {
        CollectionAssert.AreEqual(new[] { "a", "b,c" }, ElementsOf(Module().Split(new VBStringValue("a,b,c"), V(","), L(2), Binary)));
        CollectionAssert.AreEqual(new[] { "a,b,c" }, ElementsOf(Module().Split(new VBStringValue("a,b,c"), V(","), L(1), Binary)));
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, ElementsOf(Module().Split(new VBStringValue("a,b,c"), V(","), L(10), Binary)));
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, ElementsOf(Module().Split(new VBStringValue("a,b,c"), V(","), All, Binary)));
    }

    [TestMethod]
    public void Split_WithALimitOfZero_IsAnEmptyArray()
        => Assert.IsEmpty(ElementsOf(Module().Split(new VBStringValue("a,b"), V(","), L(0), Binary)));

    [TestMethod]
    [DataRow(-2)]
    [DataRow(int.MinValue)]
    public void Split_WithALimitBelowMinusOne_IsError5(int limit)
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().Split(new VBStringValue("a,b"), V(","), L(limit), Binary)));

    [TestMethod]
    public void Split_MatchesDoNotOverlap()
    {
        CollectionAssert.AreEqual(new[] { "a", "b", "-c" }, ElementsOf(Module().Split(new VBStringValue("a--b---c"), V("--"), All, Binary)));
        CollectionAssert.AreEqual(new[] { "", "a" }, ElementsOf(Module().Split(new VBStringValue("aaa"), V("aa"), All, Binary)));
        CollectionAssert.AreEqual(new[] { "", "" }, ElementsOf(Module().Split(new VBStringValue("abc"), V("abc"), All, Binary)));
    }

    [TestMethod]
    public void Split_OnAZeroLengthOrEmptyDelimiter_OrOneNotFound_IsTheWholeString()
    {
        CollectionAssert.AreEqual(new[] { "abc" }, ElementsOf(Module().Split(new VBStringValue("abc"), V(string.Empty), All, Binary)));
        CollectionAssert.AreEqual(new[] { "a b" }, ElementsOf(Module().Split(new VBStringValue("a b"), VEmpty, All, Binary)));
        CollectionAssert.AreEqual(new[] { "abc" }, ElementsOf(Module().Split(new VBStringValue("abc"), V("x"), All, Binary)));
    }

    [TestMethod]
    public void Split_OnCrLf()
    {
        CollectionAssert.AreEqual(new[] { "a", "b" }, ElementsOf(Module().Split(new VBStringValue("a\r\nb"), V("\r\n"), All, Binary)));
        CollectionAssert.AreEqual(new[] { "a\r", "b" }, ElementsOf(Module().Split(new VBStringValue("a\r\nb"), V("\n"), All, Binary)));
    }

    [TestMethod]
    public void Split_TheDelimiterIsItsString_AndNullIsError94()
    {
        CollectionAssert.AreEqual(new[] { "a", "b" }, ElementsOf(Module().Split(new VBStringValue("a1b"), V(1), All, Binary)));
        // a number is written in the regional settings: 1.5 is "1,5" in German.
        CollectionAssert.AreEqual(new[] { "", "x2" }, ElementsOf(Module(lcid: 1031, regional: German).Split(new VBStringValue("1,5x2"), V(1.5), All, Binary)));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, ErrorOf(Module().Split(new VBStringValue("a b"), VNull, All, Binary)));
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().Split(new VBStringValue("a b"), V(new VBErrorValue(5)), All, Binary)));
    }

    [TestMethod]
    public void Split_CoercesTheDelimiterBeforeItChecksTheLimit()
        // the Delimiter's Null comes before the Limit's value: 94, not 5.
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, ErrorOf(Module().Split(new VBStringValue("a b"), VNull, L(-2), Binary)));

    [TestMethod]
    public void Split_ComparesAsItsCompareSays()
    {
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, ElementsOf(Module().Split(new VBStringValue("aXbxc"), V("x"), All, Text)));
        CollectionAssert.AreEqual(new[] { "aXb", "c" }, ElementsOf(Module().Split(new VBStringValue("aXbxc"), V("x"), All, Binary)));
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(240)]
    [DataRow(5000)]
    public void Split_WithACompareThatIsNoModeNorLocale_IsError5(int compare)
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().Split(new VBStringValue("a b"), null, All, (VBCompareMethod)compare)));

    [TestMethod]
    [DataRow(2, DisplayName = "vbDatabaseCompare, as Option Compare Database compares")]
    [DataRow(7)]
    [DataRow(1033)]
    public void Split_WithACompareThatIsDatabaseOrALocale_SplitsAtTheText(int compare)
        => Assert.AreEqual(1, ArrayOf(Module().Split(new VBStringValue("aXb"), V("x"), All, (VBCompareMethod)compare)).Dimensions[0].UpperBound);

    [TestMethod]
    public void Split_DropsATrailingByte()
        // one character and a byte: the byte is not a character, so it is in no element.
        => CollectionAssert.AreEqual(new[] { "A" }, ElementsOf(Module().Split(Bytes(0x41, 0x00, 0x42), null, All, Binary)));

    [TestMethod]
    public void Split_FromSource_WithEverythingLeftOut_SplitsOnASpace()
        => Assert.AreEqual("a|b", Run("Debug.Print Join(Split(\"a b\"), \"|\")").Single());

    [TestMethod]
    public void Split_FromSource_IsZeroBased_WhateverTheOptionBase()
    {
        var lines = RunIn(new RuntimeSourceHarness.Surroundings { Directives = new ModuleDirectives(Base: 1) }, [],
            "Debug.Print LBound(Split(\"a b\"))", "Debug.Print UBound(Split(\"a b\"))", "Debug.Print UBound(Split(\"\"))", "Debug.Print LBound(Array(1))");

        CollectionAssert.AreEqual(new[] { "0", "1", "-1", "1" }, lines);
    }

    [TestMethod]
    public void Split_FromSource_IsAStringArray()
        => CollectionAssert.AreEqual(new[] { "String()", "8200" }, Run("Debug.Print TypeName(Split(\"a\"))", "Debug.Print VarType(Split(\"a\"))"));

    [TestMethod]
    public void Split_FromSource_CanBeIndexed()
        => Assert.AreEqual("b", Run("Debug.Print Split(\"a b\")(1)").Single());

    [TestMethod]
    public void Split_FromSource_IsAssignedToAStringArray()
    {
        var parts = Field("Parts", new VBResizableArrayType(VBStringType.TypeInfo));

        CollectionAssert.AreEqual(new[] { "b", "1" }, RunWith([parts], "Parts = Split(\"a,b\", \",\")", "Debug.Print Parts(1)", "Debug.Print UBound(Parts)"));
    }

    [TestMethod]
    public void Split_FromSource_IsATypeMismatch_ForAnArrayOfAnotherElementType()
    {
        // a String() is not a Long(), whatever its elements say (MS-VBAL 5.5.1.2.7).
        var numbers = Field("Numbers", new VBResizableArrayType(VBLongType.TypeInfo));

        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, RunToErrorWith([numbers], "Numbers = Split(\"1 2\")"));
    }

    [TestMethod]
    public void Split_FromSource_OfAZeroLengthString_IteratesNoTimes()
    {
        var element = Field("E", VBVariantType.TypeInfo);

        CollectionAssert.AreEqual(new[] { "done" }, RunWith([element], "For Each E In Split(\"\")", "Debug.Print E", "Next", "Debug.Print \"done\""));
    }

    [TestMethod]
    public void Split_FromSource_WithCompareLeftOut_FollowsTheModulesOptionCompare()
    {
        string[] body = ["Debug.Print Join(Split(\"aXbxc\", \"x\"), \"|\")"];

        Assert.AreEqual("a|b|c", RunUnder(OptionCompare.Text, body).Single());
        Assert.AreEqual("aXb|c", RunUnder(OptionCompare.Binary, body).Single());
    }

    [TestMethod]
    public void Split_FromSource_ANullCharDelimiter_UnderText_IsFoundLiterally()
        => Assert.AreEqual("a|b", RunUnder(OptionCompare.Text,
            "Debug.Print Join(Split(\"a\" & Chr(0) & \"b\", Chr(0)), \"|\")").Single());

    [TestMethod]
    public void Split_FromSource_TakesItsArgumentsLetCoerced()
    {
        CollectionAssert.AreEqual(new[] { "a|b,c", "1|3|1" }, Run("Debug.Print Join(Split(\"a,b,c\", \",\", 1.5), \"|\")", "Debug.Print Join(Split(12321, 2), \"|\")"));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, RunToError("Debug.Print Join(Split(Null))"));
    }

    [TestMethod]
    public async Task Split_FromSource_IntoALocalStringArray_LoadsAndRuns()
    {
        // the module passes static semantics, or it would not load: a String() variable takes the Variant holding a String().
        var printed = await RunAsync([],
            "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" +
            "Public Sub Main()\r\n    Dim parts() As String\r\n    parts = Split(\"a b c\")\r\n    Debug.Print parts(2)\r\n    Debug.Print UBound(parts)\r\nEnd Sub\r\n");

        CollectionAssert.AreEqual(new[] { "c", "2" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task Split_FromSource_ACallersMissingDelimiter_IsASpace_ButAMissingLimitIsAMismatch()
    {
        // an omitted Optional Variant is Missing: passed on to Split's Variant Delimiter it is the Delimiter left out, and passed on to its
        // Long Limit it is an explicit argument whose value is an Error value.
        var printed = await RunAsync([],
            "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" +
            "Private Function SplitAt(Optional d As Variant) As String\r\n    SplitAt = Join(Split(\"a b\", d), \"|\")\r\nEnd Function\r\n" +
            "Private Function LimitOf(Optional n As Variant) As String\r\n    LimitOf = Join(Split(\"a b c\", \" \", n), \"|\")\r\nEnd Function\r\n" +
            "Public Sub Main()\r\n    Debug.Print SplitAt()\r\n    On Error Resume Next\r\n    Debug.Print LimitOf()\r\n    Debug.Print Err.Number\r\nEnd Sub\r\n");

        CollectionAssert.AreEqual(new[] { "a|b", "13" }, printed, string.Join(" | ", printed));
    }
}
