using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>For</c> loop whose counter is passed by reference, to a callee or by the caller (MS-VBAL §5.4.2.3): each pass adds the step to the value the
/// counter holds then, what a callee assigned it included, and the loop ends on that value. The procedure is compiled, and a program runs the same
/// either way.
/// </summary>
[TestClass]
public sealed class CompiledForCounterByReferenceTests
{
    // the program run both ways, printing what it is expected to, with the procedure compiled.
    private static async Task AssertCountsAsync(string procedure, string expected, params string[] lines)
    {
        var compiled = await AssertAlikeAsync(Program(lines), expected: expected);
        Assert.IsTrue(compiled.TryGetValue(procedure, out var result), $"{procedure} was not called");
        Assert.IsNotNull(result.Body, $"{procedure}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
    }

    // a Long counter given by reference to a Sub that adds 1 to it: the loop counts 1, 3, 5, 7, 9 and adds what the Sub left, 2 + 4 + 6 + 8 + 10.
    [TestMethod]
    public async Task ALongCounterACalleeAssigns_IsCountedOnFromWhatTheCalleeLeft()
        => await AssertCountsAsync("Bumped", " 30 ",
            "Public Sub Main()",
            "    Debug.Print Bumped()",
            "End Sub",
            "Private Function Bumped() As Long",
            "    Dim I As Long",
            "    For I = 1 To 10",
            "        Bump I",
            "        Bumped = Bumped + I",
            "    Next",
            "End Function",
            "Private Sub Bump(N As Long)",
            "    N = N + 1",
            "End Sub");

    // a Long counter given by reference to a function inside an expression: 1 is made 2 and doubled, then 3 made 4, 5 made 6.
    [TestMethod]
    public async Task ALongCounterGivenToAFunctionInAnExpression_IsCountedOnFromWhatTheFunctionLeft()
        => await AssertCountsAsync("Summed", " 24 ",
            "Public Sub Main()",
            "    Debug.Print Summed()",
            "End Sub",
            "Private Function Summed() As Long",
            "    Dim I As Long",
            "    For I = 1 To 5",
            "        Summed = Summed + Twice(I)",
            "    Next",
            "End Function",
            "Private Function Twice(N As Long) As Long",
            "    N = N + 1",
            "    Twice = N * 2",
            "End Function");

    // a parameter passed by reference as the counter: the caller's variable holds the counter's last value, one step past the end.
    [TestMethod]
    public async Task AParameterPassedByReferenceAsTheCounter_LeavesTheCallersVariablePastTheEnd()
        => await AssertCountsAsync("Counted", " 6  4 ",
            "Public Sub Main()",
            "    Dim K As Long",
            "    Debug.Print Counted(K); K",
            "End Sub",
            "Private Function Counted(N As Long) As Long",
            "    For N = 1 To 3",
            "        Counted = Counted + N",
            "    Next",
            "End Function");

    // a Double counter a callee adds 1 to: 0.5 made 1.5, then 2 made 3; 3.5 is past the end.
    [TestMethod]
    public async Task ADoubleCounterACalleeAssigns_IsCountedOnFromWhatTheCalleeLeft()
        => await AssertCountsAsync("Halves", " 4.5 ",
            "Public Sub Main()",
            "    Debug.Print Halves()",
            "End Sub",
            "Private Function Halves() As Double",
            "    Dim D As Double",
            "    For D = 0.5 To 3 Step 0.5",
            "        Raise D",
            "        Halves = Halves + D",
            "    Next",
            "End Function",
            "Private Sub Raise(N As Double)",
            "    N = N + 1",
            "End Sub");

    // a Single counter a callee adds 0.5 to: 1 made 1.5, then 3 made 3.5; 5 is past the end.
    [TestMethod]
    public async Task ASingleCounterACalleeAssigns_IsCountedOnFromWhatTheCalleeLeft()
        => await AssertCountsAsync("Stepped", " 5 ",
            "Public Sub Main()",
            "    Debug.Print Stepped()",
            "End Sub",
            "Private Function Stepped() As Single",
            "    Dim S As Single",
            "    For S = 1 To 4 Step 1.5",
            "        Nudge S",
            "        Stepped = Stepped + S",
            "    Next",
            "End Function",
            "Private Sub Nudge(N As Single)",
            "    N = N + 0.5",
            "End Sub");
}
