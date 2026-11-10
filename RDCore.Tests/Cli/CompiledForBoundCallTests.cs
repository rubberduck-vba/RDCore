using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A call in the start, the end or the step of a <c>For</c> statement: each is evaluated once, in that order, before the counter is given the start
/// (MS-VBAL §5.4.2.3). A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledForBoundCallTests
{
    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    // the start a variable a call in the end changes, by reference: the start is the value the variable had when it was evaluated, before the call.
    [TestMethod]
    public async Task AStartTheEndsCallChanges_IsReadBeforeTheCall()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Total()",
            "End Sub",
            "Private Function Total() As Long",
            "    Dim A As Long",
            "    Dim I As Long",
            "    A = 1",
            "    For I = A To Bump(A)",
            "        Total = Total + I",
            "    Next",
            "    Total = Total * 100 + A",
            "End Function",
            "Private Function Bump(ByRef A As Long) As Long",
            "    A = A + 10",
            "    Bump = A",
            "End Function"), expected: "6611");
}
