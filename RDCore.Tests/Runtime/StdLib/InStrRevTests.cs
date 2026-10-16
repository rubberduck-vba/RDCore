using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.StdLib;
using static RDCore.Tests.Cli.ModuleWorkspace;
using static RDCore.Tests.Runtime.StdLib.StdStringsTestHarness;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.11.1.15 InStrRev</strong>: "the position of an occurrence of one string within another, from the end of
/// string", and 0 when there is none.
/// </summary>
/// <remarks>
/// Both Strings and Start are typed, so the call site coerces them; Start left out is its declared -1, and a Compare left out the
/// calling module's <c>Option Compare</c>.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11.1.15 InStrRev")]
public sealed class InStrRevTests
{
    private const VBCompareMethod Binary = VBCompareMethod.VBBinaryCompare;
    private const VBCompareMethod Text = VBCompareMethod.VBTextCompare;

    private static int InStrRev(string stringCheck, string stringMatch, int start = -1, VBCompareMethod compare = Binary)
        => LongOf(Module().InStrRev(new VBStringValue(stringCheck), new VBStringValue(stringMatch), L(start), compare));

    private static int InStrRevError(string stringCheck, string stringMatch, int start = -1, VBCompareMethod compare = Binary)
        => ErrorOf(Module().InStrRev(new VBStringValue(stringCheck), new VBStringValue(stringMatch), L(start), compare));

    [TestMethod]
    public void InStrRev_IsThePositionOfTheLastMatch()
        => Assert.AreEqual(5, InStrRev("abcabc", "bc"));

    [TestMethod]
    [DataRow("abcabc", "bc", 3, 2)]
    [DataRow("abcabc", "bc", 2, 0)]
    [DataRow("abc", "c", 2, 0)]
    [DataRow("abc", "c", 3, 3)]
    [DataRow("abc", "abc", 3, 1)]
    public void InStrRev_AMatchMustEndByTheStart(string stringCheck, string stringMatch, int start, int expected)
        => Assert.AreEqual(expected, InStrRev(stringCheck, stringMatch, start));

    [TestMethod]
    public void InStrRev_MatchesMayOverlap()
        => Assert.AreEqual(2, InStrRev("aaa", "aa"));

    [TestMethod]
    [DataRow(-1, 3)]
    [DataRow(3, 3)]
    [DataRow(1, 1)]
    public void InStrRev_OfAZeroLengthPattern_IsTheStart(int start, int expected)
        => Assert.AreEqual(expected, InStrRev("abc", string.Empty, start));

    [TestMethod]
    [DataRow("", 4)]
    [DataRow("b", 4)]
    [DataRow("b", 100)]
    public void InStrRev_StartingPastTheEnd_IsZero(string stringMatch, int start)
        => Assert.AreEqual(0, InStrRev("abc", stringMatch, start));

    [TestMethod]
    [DataRow("", -1)]
    [DataRow("a", -1)]
    [DataRow("", 1)]
    public void InStrRev_InAZeroLengthString_IsZero(string stringMatch, int start)
        => Assert.AreEqual(0, InStrRev(string.Empty, stringMatch, start));

    [TestMethod]
    [DataRow(0)]
    [DataRow(-2)]
    public void InStrRev_WithAStartOfZeroOrBelowMinusOne_IsError5(int start)
    {
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, InStrRevError("abc", "b", start));
        // -1 is the end.
        Assert.AreEqual(2, InStrRev("abc", "b", -1));
    }

    [TestMethod]
    public void InStrRev_InTextMode_IgnoresCase()
    {
        Assert.AreEqual(5, InStrRev("abcABC", "b", compare: Text));
        Assert.AreEqual(2, InStrRev("aaa", "AA", compare: Text));
        Assert.AreEqual(2, InStrRev("abcABC", "b", compare: Binary));
    }

    [TestMethod]
    [DataRow(2)]
    [DataRow(-1)]
    [DataRow(7)]
    [DataRow(1033)]
    public void InStrRev_WithACompareOtherThanZeroOrOne_IsError5(int compare)
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, InStrRevError("abc", "b", compare: (VBCompareMethod)compare));

    [TestMethod]
    public void InStrRev_OfAStringWithATrailingByte_SearchesItsCharacters()
        => Assert.AreEqual(0, LongOf(Module().InStrRev(Bytes(0x41, 0x00, 0x42), new VBStringValue("B"), L(-1), Binary)));

    [TestMethod]
    public void InStrRev_CalledDirectlyWithoutItsStart_IsTheCallersMistake()
        // a typed optional is filled from its declaration before a member is reached, so null is a C# caller's mistake.
        => Assert.ThrowsExactly<ArgumentNullException>(() => Module().InStrRev(new VBStringValue("abc"), new VBStringValue("b"), null, Binary));

    [TestMethod]
    public void InStrRev_FromSource_WithTheStartLeftOut_SearchesFromTheEnd()
        => Assert.AreEqual("4", Run("Debug.Print InStrRev(\"abca\", \"a\")").Single());

    [TestMethod]
    [DataRow(null, "1", DisplayName = "no Option Compare")]
    [DataRow(OptionCompare.Binary, "1", DisplayName = "Option Compare Binary")]
    [DataRow(OptionCompare.Text, "2", DisplayName = "Option Compare Text")]
    [DataRow(OptionCompare.Database, "2", DisplayName = "Option Compare Database")]
    public void InStrRev_FromSource_WithCompareLeftOut_FollowsTheModulesOptionCompare(OptionCompare? mode, string expected)
        // the runtime semantics' "the <option-compare-directive> of the calling module", where the table says binary.
        => CollectionAssert.AreEqual(new[] { expected, expected }, RunUnder(mode, "Debug.Print InStrRev(\"Aa\", \"A\")", "Debug.Print InStrRev(\"Aa\", \"A\", -1)"));

    [TestMethod]
    public void InStrRev_FromSource_AnExplicitCompare_OverridesTheModules()
    {
        Assert.AreEqual("1", RunUnder(OptionCompare.Text, "Debug.Print InStrRev(\"Aa\", \"A\", -1, 0)").Single());
        Assert.AreEqual("2", RunUnder(OptionCompare.Binary, "Debug.Print InStrRev(\"Aa\", \"A\", -1, vbTextCompare)").Single());
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, RunToErrorUnder(OptionCompare.Text, "Debug.Print InStrRev(\"Aa\", \"A\", -1, 2)"));
    }

    [TestMethod]
    public void InStrRev_FromSource_TakesItsArgumentsLetCoerced()
    {
        Assert.AreEqual("2", Run("Debug.Print InStrRev(\"abc\", \"b\", 1.5)").Single());
        Assert.AreEqual("4", Run("Debug.Print InStrRev(12321, \"2\")").Single());
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, RunToError("Debug.Print InStrRev(Null, \"a\")"));
        // V is Empty, which is the Start 0.
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, RunToError("Debug.Print InStrRev(\"abc\", \"b\", V)"));
    }

    [TestMethod]
    public async Task InStrRev_FromSource_InAClassModule_FollowsThatModulesOptionCompare()
    {
        var finder = ClassModule("Finder", "Option Explicit", "Option Compare Text",
            "Public Function FindLast(ByVal s As String, ByVal p As String) As Long", "    FindLast = InStrRev(s, p)", "End Function");
        var printed = await RunAsync([("Finder", finder)],
            "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nOption Compare Binary\r\n" +
            "Public Sub Main()\r\n    Dim f As Finder\r\n    Set f = New Finder\r\n" +
            "    Debug.Print f.FindLast(\"Aa\", \"A\")\r\n    Debug.Print InStrRev(\"Aa\", \"A\")\r\nEnd Sub\r\n");

        CollectionAssert.AreEqual(new[] { "2", "1" }, printed, string.Join(" | ", printed));
    }
}
