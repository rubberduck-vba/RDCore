using Microsoft.Extensions.Logging.Abstractions;
using RDCore.Runtime.Support.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.Runtime.Compiled;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using RDCore.SDK.Platform.Components;

namespace RDCore.Tests.Cli;

/// <summary>
/// MS-VBAL §5.4.2.3: an error a <c>Next</c> raises - stepping the counter, assigning it, testing it with the end - is located at the <c>Next</c>, as MS-VBA
/// locates it (recorded in Excel): the line <c>Erl</c> reports when it counts document lines, the line a run that ends with the error reports, and the line a
/// program that waits at the error waits at. So is the error 92 of a <c>Next</c> whose loop was never opened. An error of the <c>For</c> statement itself is
/// at the <c>For</c>. Each program runs in the interpreter and in compiled code alike.
/// </summary>
[TestClass]
public sealed class NextErrorLocationTests
{
    // a cap on a hang, not a budget: each wait is on the event itself, which a loaded machine brings as late as it does.
    private static readonly TimeSpan SafetyCap = TimeSpan.FromMinutes(5);

    // Main calls Probe, which runs the loop under a handler that prints the error and Erl, which counts document lines.
    private static string[] Probe(string declarations, string[] loop, params string[] handler) =>
    [
        "Public Sub Main()",
        "    Debug.Print Probe()",
        "End Sub",
        "Private Function Probe() As String",
        $"    {declarations}",
        "    On Error GoTo H",
        .. loop,
        "    Exit Function",
        "H:",
        .. handler.Length == 0 ? ["    Probe = \"E\" & Err.Number & \"@\" & Erl"] : handler,
        "End Function",
    ];

    // the line, counted from 1, that the program has text on.
    private static int LineOf(string[] program, string text) => Array.FindIndex(program, line => line.Trim() == text) + 1;

    private static readonly string[] NullCounter = ["    For k = 1 To 3", "        If k = 2 Then k = Null", "    Next k"];
    private static readonly string[] TextCounter = ["    For k = 1 To 3", "        If k = 2 Then k = \"x\"", "    Next k"];
    private static readonly string[] Overflowing = ["    For i = 32766 To 32767", "    Next i"];
    private static readonly string[] OverflowingBare = ["    For i = 32766 To 32767", "    Next"];
    private static readonly string[] OverflowingOuter = ["    For i = 32766 To 32767", "    For k = 1 To 1", "    Next k, i"];
    private static readonly string[] NeverOpened = ["    GoTo L", "    For i = 1 To 3", "L:", "    Next i"];

    [TestMethod]
    [DataRow("Null", 94, DisplayName = "94, a counter that holds Null")]
    [DataRow("Text", 13, DisplayName = "13, a counter that holds a String that is no number")]
    [DataRow("Overflowing", 6, DisplayName = "6, a counter stepped past its type")]
    [DataRow("OverflowingBare", 6, DisplayName = "6, a counter stepped past its type, at a Next that names none")]
    [DataRow("OverflowingOuter", 6, DisplayName = "6, the outer counter stepped past its type, at the Next it shares")]
    [DataRow("NeverOpened", 92, DisplayName = "92, a Next whose loop was never opened")]
    public async Task AnErrorTheNextRaises_IsAtTheNext(string loop, int number)
    {
        var (lines, next) = loop switch
        {
            "Null" => (NullCounter, "Next k"),
            "Text" => (TextCounter, "Next k"),
            "Overflowing" => (Overflowing, "Next i"),
            "OverflowingBare" => (OverflowingBare, "Next"),
            "OverflowingOuter" => (OverflowingOuter, "Next k, i"),
            _ => (NeverOpened, "Next i"),
        };
        var program = Probe("Dim k, i As Integer", lines);

        await AssertRunsAsync(program, $"| E{number}@{LineOf(program, next)} |");
    }

    [TestMethod]
    public async Task AnOverflowAtTheNext_LeavesTheCounterAsItWas()
    {
        var program = Probe("Dim i As Integer", Overflowing, "    Probe = \"E\" & Err.Number & \"@\" & Erl & \" i=\" & i");

        await AssertRunsAsync(program, $"| E6@{LineOf(program, "Next i")} i=32767 |");
    }

    [TestMethod]
    public async Task AnErrorOfTheForStatement_IsAtTheFor()
    {
        // a Null end raises 94 at the For, which evaluates it.
        var program = Probe("Dim k, e: e = Null", ["    For k = 1 To e", "    Next k"]);

        await AssertRunsAsync(program, $"| E94@{LineOf(program, "For k = 1 To e")} |");
    }

    [TestMethod]
    public async Task AnErrorTheNextRaises_ThatNothingCatches_EndsTheRunAtTheNext()
    {
        string[] program = ["Public Sub Main()", "    Dim i As Integer", "    For i = 32766 To 32767", "    Next i", "End Sub"];

        // the line a run reports is counted from 0.
        await AssertRunsAsync(program, $"| 6 VBR00006 Overflow @{LineOf(program, "Next i") - 1}:");
    }

    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter)]
    [DataRow(VBExecutionEngine.Compiled)]
    public async Task AProgramThatWaitsAtAnErrorTheNextRaises_WaitsAtTheNext(VBExecutionEngine engine)
    {
        if (engine is VBExecutionEngine.Compiled)
        {
            CompiledExecutionTests.RequirePayloads();
        }

        string[] program = ["Public Sub Main()", "    Dim i As Integer", "    Stop", "    For i = 32766 To 32767", "    Next i", "End Sub"];
        ExecuteResult? atTheError = null;
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync([], Program(program), async (provider, stopped, _) =>
        {
            provider.Execution.SetErrorBreak(ErrorBreakMode.Unhandled);
            atTheError = await new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
                .Handle(new HostDebugResumeParams(), CancellationToken.None).WaitAsync(SafetyCap);
        }, debug: true, engine: engine);

        Assert.AreEqual((ExecutionOutcome.Suspended, 6, LineOf(program, "Next i") - 1), (atTheError!.Outcome, atTheError.ErrorNumber, atTheError.ErrorLine),
            atTheError.ErrorMessage);
    }

    // the program run in the interpreter and in compiled code, alike.
    private static async Task AssertRunsAsync(string[] program, string expected)
    {
        var interpreted = await RunAsync(Program(program), VBExecutionEngine.Interpreter);
        StringAssert.Contains(interpreted, expected, interpreted);

        CompiledExecutionTests.RequirePayloads();
        Assert.AreEqual(interpreted, await RunAsync(Program(program), VBExecutionEngine.Compiled));
    }

    private static async Task<string> RunAsync(string program, VBExecutionEngine engine)
    {
        var observed = string.Empty;
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync([], program, (_, run, _) =>
        {
            observed = Observed(run);
            return Task.CompletedTask;
        }, engine: engine);
        return observed;
    }
}
