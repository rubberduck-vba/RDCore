using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.Shared;
using static RDCore.Tests.Cli.ModuleWorkspace;
using static RDCore.Tests.Runtime.StdLib.StdStringsTestHarness;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.11.1.29 Replace</strong>: "a String, with substitutions made, that begins at the position specified by
/// Start and concludes at the end of the Expression string. It is not a copy of the original string from start to finish."
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11.1.29 Replace")]
public sealed class ReplaceTests
{
    private const VBCompareMethod Binary = VBCompareMethod.VBBinaryCompare;
    private const VBCompareMethod Text = VBCompareMethod.VBTextCompare;

    private static readonly VBLongValue All = L(-1);

    private static RuntimeSemanticsEvaluationResult<VBStringValue> Replace(string expression, string find, string replace, int start = 1, int count = -1, VBCompareMethod compare = Binary)
        => Module().Replace(new VBStringValue(expression), new VBStringValue(find), new VBStringValue(replace), L(start), L(count), compare);

    [TestMethod]
    public void Replace_ReplacesEveryMatch()
        => Assert.AreEqual("aXcX", TextOf(Replace("abcb", "b", "X")));

    [TestMethod]
    [DataRow("abcabc", 3, "caXc")]
    [DataRow("abc", 3, "c")]
    [DataRow("abc", 2, "Xc")]
    public void Replace_ReturnsTheStringFromTheStartOn(string expression, int start, string expected)
        => Assert.AreEqual(expected, TextOf(Replace(expression, "b", "X", start)));

    [TestMethod]
    [DataRow("abc", 4)]
    [DataRow("", 1)]
    [DataRow("", 2)]
    public void Replace_StartingPastTheEnd_OrOfAZeroLengthString_IsAZeroLengthString(string expression, int start)
        => Assert.AreEqual(string.Empty, TextOf(Replace(expression, "b", "X", start)));

    [TestMethod]
    [DataRow("abc", "", 1, -1, "abc")]
    [DataRow("abcabc", "", 3, -1, "cabc")]
    [DataRow("abcabc", "b", 3, 0, "cabc")]
    [DataRow("abc", "b", 1, 0, "abc")]
    [DataRow("abc", "b", 2, 0, "bc")]
    public void Replace_WithAZeroLengthFindOrACountOfZero_IsTheStringFromTheStart(string expression, string find, int start, int count, string expected)
        => Assert.AreEqual(expected, TextOf(Replace(expression, find, "X", start, count)));

    [TestMethod]
    public void Replace_ReplacesAtMostCountMatches()
    {
        Assert.AreEqual("aXcb", TextOf(Replace("abcb", "b", "X", 1, 1)));
        Assert.AreEqual("aXcX", TextOf(Replace("abcb", "b", "X", 1, 5)));
    }

    [TestMethod]
    public void Replace_MatchesDoNotOverlap_AndAreNotSearchedAgain()
    {
        Assert.AreEqual("ba", TextOf(Replace("aaa", "aa", "b")));
        Assert.AreEqual("a..b..c", TextOf(Replace("a.b.c", ".", "..")));
    }

    [TestMethod]
    public void Replace_WithAZeroLengthReplacement_RemovesTheMatches()
    {
        Assert.AreEqual("ac", TextOf(Replace("abc", "b", "")));
        Assert.AreEqual(string.Empty, TextOf(Replace("abc", "abc", "")));
    }

    [TestMethod]
    [DataRow("abc", 0)]
    [DataRow("abc", -1)]
    [DataRow("abc", -2)]
    [DataRow("", 0)]
    public void Replace_WithAStartBelowOne_IsError5_EvenOfAZeroLengthString(string expression, int start)
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Replace(expression, "b", "X", start)));

    [TestMethod]
    [DataRow(-2)]
    [DataRow(int.MinValue)]
    public void Replace_WithACountBelowMinusOne_IsError5(int count)
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Replace("abc", "b", "X", 1, count)));

    [TestMethod]
    public void Replace_ComparesAsItsCompareSays()
    {
        Assert.AreEqual("xBxb", TextOf(Replace("ABab", "a", "x", compare: Text)));
        Assert.AreEqual("ba", TextOf(Replace("aaa", "AA", "b", compare: Text)));
        Assert.AreEqual("abc", TextOf(Replace("abc", "B", "X", compare: Binary)));
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(240)]
    [DataRow(5000)]
    public void Replace_WithACompareThatIsNoModeNorLocale_IsError5(int compare)
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Replace("abc", "b", "X", compare: (VBCompareMethod)compare)));

    [TestMethod]
    [DataRow(2, DisplayName = "vbDatabaseCompare, as Option Compare Database compares")]
    [DataRow(7)]
    [DataRow(1033)]
    public void Replace_WithACompareThatIsDatabaseOrALocale_ReplacesTheText(int compare)
        => Assert.AreEqual("aXc", ((VBStringValue)Replace("abc", "B", "X", compare: (VBCompareMethod)compare).Result!).Value);

    [TestMethod]
    public void Replace_DropsATrailingByte()
        // the Expression is read as characters: one, and a byte that is not one.
        => CollectionAssert.AreEqual(new byte[] { 0x58, 0x00 },
            BytesOf(Module().Replace(Bytes(0x41, 0x00, 0x42), new VBStringValue("A"), new VBStringValue("X"), L(1), All, Binary)));

    [TestMethod]
    public void Replace_FromSource_WithStartAndCountLeftOut_ReplacesEveryMatch()
        => Assert.AreEqual("bbb", Run("Debug.Print Replace(\"aaa\", \"a\", \"b\")").Single());

    [TestMethod]
    public void Replace_FromSource_WithCompareLeftOut_FollowsTheModulesOptionCompare()
    {
        string[] body = ["Debug.Print Replace(\"aA\", \"a\", \"x\")"];

        Assert.AreEqual("xx", RunUnder(OptionCompare.Text, body).Single());
        Assert.AreEqual("xA", RunUnder(OptionCompare.Binary, body).Single());
    }

    [TestMethod]
    public void Replace_FromSource_TakesItsArgumentsLetCoerced()
    {
        CollectionAssert.AreEqual(new[] { "Xc", "1x3x1" }, Run("Debug.Print Replace(\"abc\", \"b\", \"X\", 1.5)", "Debug.Print Replace(12321, \"2\", \"x\")"));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, RunToError("Debug.Print Replace(Null, \"a\", \"b\")"));
    }

    [TestMethod]
    public void Replace_FromSource_ANullCharFind_UnderText_IsFoundLiterally()
        => Assert.AreEqual("a#b", RunUnder(OptionCompare.Text,
            "Debug.Print Replace(\"a\" & Chr(0) & \"b\", Chr(0), \"#\", 1, -1, 1)").Single());

    [TestMethod]
    public async Task Replace_FromSource_ACallersMissingCount_IsATypeMismatch()
    {
        // an omitted Optional Variant is Missing, and passed on to Replace's Long Count it is an explicit argument whose value is an Error value.
        var printed = await RunAsync([],
            "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" +
            "Private Function ReplaceUpTo(Optional n As Variant) As String\r\n    ReplaceUpTo = Replace(\"aaa\", \"a\", \"b\", 1, n)\r\nEnd Function\r\n" +
            "Public Sub Main()\r\n    Debug.Print ReplaceUpTo(2)\r\n    On Error Resume Next\r\n    Debug.Print ReplaceUpTo()\r\n    Debug.Print Err.Number\r\nEnd Sub\r\n");

        CollectionAssert.AreEqual(new[] { "bba", "13" }, printed, string.Join(" | ", printed));
    }
}
