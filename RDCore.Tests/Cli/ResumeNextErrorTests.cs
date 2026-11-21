using Microsoft.Extensions.Logging.Abstractions;
using RDCore.Runtime.Support.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Platform.Components;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// An error an operation raises where <c>On Error Resume Next</c> is in effect (MS-VBAL §5.4.4.1): Err is given it, and the procedure goes on at the
/// statement after: in a loop, at each error it raises.
/// </summary>
[TestClass]
public sealed class ResumeNextErrorTests
{
    // a loop of 4 turns that raises an error at 2 of them, at line 10, and says what Err holds after each: its number, its description and Erl (the line of
    // the document by default). K is 1, 0, 1, 0.
    private static string Raising(string statement) => Program(
        "Public Sub Main()",
        "    Debug.Print Raised()",
        "End Sub",
        "Private Function Raised() As String",
        "    On Error Resume Next",
        "    Dim I As Long, K As Integer, M As Integer, N As Integer, Q As Long, D As Double, Seen As String",
        "    M = 20000",
        "    For I = 1 To 4",
        "        K = I Mod 2",
        "        " + statement,
        "        If Err.Number <> 0 Then Seen = Seen & I & \":\" & Err.Number & \" \" & Err.Description & \" \" & Erl & \";\"",
        "        Err.Clear",
        "    Next",
        "    Raised = Seen",
        "End Function");

    // MS-VBAL §5.6.9.3.5, §5.6.9.3.6: a / or a \ by 0 raises error 11; MS-VBAL §5.6.9.3.4: an Integer product an Integer does not hold, error 6.
    [TestMethod]
    [DataRow("D = 10 / K", "2:11 Division by zero 10;4:11 Division by zero 10;", DisplayName = "a / by 0")]
    [DataRow("Q = 100 \\ K", "2:11 Division by zero 10;4:11 Division by zero 10;", DisplayName = "a \\ by 0")]
    [DataRow("N = M * (K + K)", "1:6 Overflow 10;3:6 Overflow 10;", DisplayName = "an Integer product past 32767")]
    public async Task AnErrorResumeNextGoesPast_IsErr_AtEachTurnOfTheLoop(string statement, string expected)
        => await CompiledErrorHandlingTests.AssertAlikeAsync(Raising(statement), expected: $"| {expected} |");

    // Err's stack trace is where the error was raised and the calls it was raised under, as it is for an error a handler catches anywhere: the Err a loop
    // went on past last.
    [TestMethod]
    public async Task AnErrorResumeNextGoesPast_HasTheStackTraceItWasRaisedOn()
        => await CompiledErrorHandlingTests.AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Outer()",
            "End Sub",
            "Private Function Outer() As String",
            "    Outer = Inner()",
            "End Function",
            "Private Function Inner() As String",
            "    On Error Resume Next",
            "    Dim I As Long, D As Double",
            "    For I = 1 To 3",
            "        D = 10 / (I - 2)",
            "    Next",
            "    Inner = Replace(Err.StackTrace, vbCrLf, \" / \")",
            "End Function"), expected: "| at Inner (11:13) /   at Outer (5:13) /   at Main (2:17) |");

    // a cap on a hang, not a budget: each wait is on the event itself, which a loaded machine brings as late as it does.
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

    // the program run once, then again with the debugger breaking on the errors mode says: what the second run did, and what resuming it did when it waited.
    private static async Task<(ExecuteResult Run, ExecuteResult? Resumed)> BreakingOnAsync(ErrorBreakMode mode, VBExecutionEngine engine)
    {
        if (engine is VBExecutionEngine.Compiled)
        {
            CompiledExecutionTests.RequirePayloads();
        }

        (ExecuteResult, ExecuteResult?) made = default;
        await ModuleWorkspace.InspectAsync([], Program(
            "Public Sub Main()",
            "    Debug.Print Ratio(1, 0)",
            "End Sub",
            "Private Function Ratio(ByVal A As Long, ByVal B As Long) As Long",
            "    On Error Resume Next",
            "    Ratio = A \\ B",
            "    If Err.Number = 11 Then Ratio = -1",
            "End Function"), async (provider, first, again) =>
        {
            Assert.AreEqual(ExecutionOutcome.Completed, first.Outcome, first.ErrorMessage);
            _ = await new HostDebugErrorBreakHandler(provider).Handle(new HostDebugErrorBreakParams { Mode = mode }, CancellationToken.None);
            var run = await again();
            made = (run, run.Outcome is ExecutionOutcome.Suspended
                ? await new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
                    .Handle(new HostDebugResumeParams(), CancellationToken.None).WaitAsync(Patience)
                : null);
        }, debug: true, engine: engine);
        return made;
    }

    // a debugger that breaks on every error waits where the error was raised, before Resume Next goes past it, at line 6 (ErrorLine counts from 0);
    // resumed, the program goes on past it.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter)]
    [DataRow(VBExecutionEngine.Compiled)]
    public async Task AnErrorResumeNextGoesPast_IsWaitedAt_WhenEveryErrorIs(VBExecutionEngine engine)
    {
        var (run, resumed) = await BreakingOnAsync(ErrorBreakMode.All, engine);

        Assert.AreEqual((ExecutionOutcome.Suspended, 11, 5), (run.Outcome, run.ErrorNumber, run.ErrorLine), run.ErrorMessage);
        Assert.IsNotNull(resumed);
        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "-1" }, resumed.Output.Select(line => line.Trim()).ToArray());
    }

    // one that breaks on errors nothing handles does not wait at it: Resume Next handles it.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter)]
    [DataRow(VBExecutionEngine.Compiled)]
    public async Task AnErrorResumeNextGoesPast_IsNotWaitedAt_WhenOnlyUnhandledErrorsAre(VBExecutionEngine engine)
    {
        var (run, resumed) = await BreakingOnAsync(ErrorBreakMode.Unhandled, engine);

        Assert.AreEqual(ExecutionOutcome.Completed, run.Outcome, run.ErrorMessage);
        Assert.IsNull(resumed);
        CollectionAssert.AreEqual(new[] { "-1" }, run.Output.Select(line => line.Trim()).ToArray());
    }
}
