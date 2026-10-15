using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// What <c>Debug.Print</c> prints is its output list, and a comparison is an expression of the list like any other: <c>Debug.Print x = 0</c> prints whether
/// <c>x</c> is 0. It is no Let of 0 to <c>Debug.Print x</c>, the target of a Let being an l-expression (<strong>MS-VBAL §5.4.3.8</strong>).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.5.8 Print Statement")]
public sealed class DebugPrintComparisonTests
{
    private static string Program(string declarations, params string[] lines)
        => $"Attribute VB_Name = \"Program\"\r\n{declarations}\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n";

    [TestMethod]
    public async Task AnEquality_PrintsTheComparison()
        => CollectionAssert.AreEqual(new[] { "True", "True", "False" }, await RunAsync([], Program(
            string.Empty,
            "Debug.Print 1 = 1",
            "Dim r As Long",
            "Debug.Print r = 0",
            "Debug.Print r = 1")));

    [TestMethod]
    public async Task AnEqualityAfterADeclarationOnTheSameLine_PrintsTheComparison()
        => CollectionAssert.AreEqual(new[] { "True" }, await RunAsync([], Program(string.Empty, "Dim r As Long: Debug.Print r = 0")));

    [TestMethod]
    public async Task AnEqualityOfTheResultOfAFunction_PrintsTheComparison()
        => CollectionAssert.AreEqual(new[] { "True", "False" }, await RunAsync([], Program(
            "Private Function G(ByVal n As Long) As Long\r\n    G = n * 2\r\nEnd Function",
            "Debug.Print G(0) = 0",
            "Debug.Print G(1) = 1")));

    // a record passed to a function that fills it in, and a second argument: the shape of `Debug.Print gettimeofday(now, 0) = 0`, with a function of the module in
    // place of the C library's.
    [TestMethod]
    public async Task AnEqualityOfTheResultOfAFunctionThatFillsInARecord_PrintsTheComparison()
        => CollectionAssert.AreEqual(new[] { "True", "True" }, await RunAsync([], Program(
            "Private Type TimeValue\r\n    Seconds As Long\r\n    Microseconds As Long\r\nEnd Type\r\n"
                + "Private Function Fill(value As TimeValue, ByVal zone As Long) As Long\r\n    value.Seconds = 1700000001\r\nEnd Function",
            "Dim now As TimeValue",
            "Debug.Print Fill(now, 0) = 0",
            "Debug.Print now.Seconds > 1700000000")));

    // the comparison is one item of the list, and the separators around it are what they are around any other.
    [TestMethod]
    public async Task AnEqualityAfterOtherItems_IsPrintedAsOneItemOfTheList()
        => CollectionAssert.AreEqual(new[] { "aTrue" }, await RunAsync([], Program(string.Empty, "Debug.Print \"a\"; 1 = 1")));

    // with more of the list after it, an equality cannot be read as a Let at all, the value of a Let ending before the separator.
    [TestMethod]
    public async Task AnEqualityBeforeOtherItems_IsPrintedAsOneItemOfTheList()
        => CollectionAssert.AreEqual(new[] { "False" + new string(' ', 9) + "x" }, await RunAsync([], Program(string.Empty, "Debug.Print 1 = 2, \"x\"")));
}
