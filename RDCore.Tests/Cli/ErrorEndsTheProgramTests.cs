using RDCore.CLI.App.Repl;
using RDCore.CLI.Host.Handlers;
using RDCore.Runtime.Compiled;
using RDCore.Runtime.Support.Host;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// A run-time error that nothing handles ends the program as <c>End</c> ends it: nothing is unwound, no <c>Class_Terminate</c> runs, and the session is
/// reset once the error is reported: its variables, its <c>Static</c> locals, its objects and its files. An error a handler catches unwinds the
/// activations it leaves, and their objects terminate as they go. A program that waits at an error under a debugger is not over, and keeps what it made.
/// </summary>
[TestClass]
public sealed class ErrorEndsTheProgramTests
{
    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    // an object that says when it is destroyed.
    private static readonly (string Name, string Source) Noisy = ("Noisy", ModuleWorkspace.ClassModule("Noisy", "Option Explicit",
        "Public Name As String",
        "Private Sub Class_Terminate()",
        "    Debug.Print \"bye \"; Name",
        "End Sub"));

    // a class whose member raises a division by zero, with an object in a local.
    private static readonly (string Name, string Source) Worker = ("Worker", ModuleWorkspace.ClassModule("Worker", "Option Explicit",
        "Public Sub Fail()",
        "    Dim Held As Noisy, Z As Long",
        "    Set Held = New Noisy",
        "    Held.Name = \"member\"",
        "    Debug.Print 1 \\ Z",
        "End Sub"));

    private static long ValueOf(EnvironmentSessionProvider provider, string module, string name)
    {
        var session = provider.Session;
        Assert.IsTrue(session.Symbols.TryResolveValue(module, GlobalSymbols.UnresolvedSymbol, out var container));
        Assert.IsTrue(session.Symbols.TryResolveValue(name, container!, out var variable));
        return Convert.ToInt64(session.Symbols.Resolver.GetValue(variable!).Value.BoxedValue);
    }

    // the program run twice in engine: each run's result, and the value of Program.Total after each.
    private static async Task<(ExecuteResult First, long Total, ExecuteResult Second, long TotalAgain)> RunTwiceAsync(VBExecutionEngine engine, string program,
        params (string Name, string Source)[] classes)
    {
        if (engine is VBExecutionEngine.Compiled)
        {
            CompiledExecutionTests.RequirePayloads();
        }

        ExecuteResult? first = null, second = null;
        long total = 0, totalAgain = 0;
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync(classes, program, async (provider, result, again) =>
        {
            (first, total) = (result, ValueOf(provider, "Program", "Total"));
            second = await again();
            totalAgain = ValueOf(provider, "Program", "Total");
        }, engine: engine);
        return (first!, total, second!, totalAgain);
    }

    private static string[] Printed(ExecuteResult result) => [.. result.Output.Select(line => line.Trim())];

    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "interpreted")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "compiled")]
    public async Task AnUnhandledError_EndsTheProgram_AndTheNextRunStartsFromTheStart(VBExecutionEngine engine)
    {
        var run = await RunTwiceAsync(engine, Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Debug.Print Total; Counted()",
            "    Total = 7",
            "    Fail",
            "End Sub",
            "Private Function Counted() As Long",
            "    Static Seen As Long",
            "    Seen = Seen + 1",
            "    Counted = Seen",
            "End Function",
            "Private Sub Fail()",
            "    Dim Z As Long",
            "    Debug.Print 1 \\ Z",
            "End Sub"));

        Assert.AreEqual(ExecutionOutcome.RuntimeError, run.First.Outcome, run.First.ErrorMessage);
        Assert.AreEqual(11, run.First.ErrorNumber);
        CollectionAssert.AreEqual(new[] { "Fail", "Main" }, run.First.StackTrace.Select(frame => frame.Procedure).ToArray(), "the error is said with where it was raised");
        CollectionAssert.AreEqual(new[] { "0  1" }, Printed(run.First));
        Assert.AreEqual(0, run.Total, "the variable is what it was defined as");
        CollectionAssert.AreEqual(new[] { "0  1" }, Printed(run.Second), "the variable and the Static start from nothing again");
        Assert.AreEqual(0, run.TotalAgain);
    }

    // MS-VBA runs no Class_Terminate when the error dialog is answered End, nor when it is answered Debug and the project is then reset: not for a local of
    // the activation that raised the error, of its callers, of a Static local, nor of a module variable (measured in Excel, 64-bit).
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "interpreted")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "compiled")]
    public async Task AnUnhandledError_RunsNoTerminate(VBExecutionEngine engine)
    {
        var run = await RunTwiceAsync(engine, Program(
            "Public Total As Long",
            "Public Kept As Noisy",
            "Public Sub Main()",
            "    Dim Local As Noisy",
            "    Debug.Print Kept Is Nothing",
            "    Set Kept = New Noisy",
            "    Kept.Name = \"module\"",
            "    Set Local = New Noisy",
            "    Local.Name = \"main\"",
            "    Inner",
            "End Sub",
            "Private Sub Inner()",
            "    Static Remembered As Noisy",
            "    Dim Here As Noisy, W As Worker",
            "    Set Remembered = New Noisy",
            "    Remembered.Name = \"static\"",
            "    Set Here = New Noisy",
            "    Here.Name = \"inner\"",
            "    Set W = New Worker",
            "    W.Fail",
            "End Sub"), Noisy, Worker);

        Assert.AreEqual(ExecutionOutcome.RuntimeError, run.First.Outcome, run.First.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "True" }, Printed(run.First), "nothing terminates");
        CollectionAssert.AreEqual(new[] { "True" }, Printed(run.Second), "the module variable was reset");
    }

    // the same error caught by Main's handler: the activations it leaves are unwound, and their objects terminate as they go, the module variable's kept.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "interpreted")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "compiled")]
    public async Task AnErrorAHandlerCatches_UnwindsTheActivationsItLeaves(VBExecutionEngine engine)
    {
        var run = await RunTwiceAsync(engine, Program(
            "Public Total As Long",
            "Public Kept As Noisy",
            "Public Sub Main()",
            "    Dim Local As Noisy",
            "    On Error GoTo Caught",
            "    Debug.Print Kept Is Nothing",
            "    Set Kept = New Noisy",
            "    Kept.Name = \"module\"",
            "    Set Local = New Noisy",
            "    Local.Name = \"main\"",
            "    Inner",
            "    Exit Sub",
            "Caught:",
            "    Debug.Print \"caught\"; Err.Number",
            "End Sub",
            "Private Sub Inner()",
            "    Dim Here As Noisy, W As Worker",
            "    Set Here = New Noisy",
            "    Here.Name = \"inner\"",
            "    Set W = New Worker",
            "    W.Fail",
            "End Sub"), Noisy, Worker);

        Assert.AreEqual(ExecutionOutcome.Completed, run.First.Outcome, run.First.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "True", "bye member", "bye inner", "caught 11", "bye main" }, Printed(run.First));
        CollectionAssert.AreEqual(new[] { "False", "bye module", "bye member", "bye inner", "caught 11", "bye main" }, Printed(run.Second),
            "the module variable kept its object, which the second Set let go of");
    }

    // a file the program left open is closed: the next run opens the same file number again.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "interpreted")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "compiled")]
    public async Task AnUnhandledError_ClosesTheFilesTheProgramLeftOpen(VBExecutionEngine engine)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rdcore-error-end-{Guid.NewGuid():N}.txt");
        try
        {
            var run = await RunTwiceAsync(engine, Program(
                "Public Total As Long",
                "Public Sub Main()",
                $"    Open \"{path}\" For Output As #7",
                "    Print #7, \"written\"",
                "    Dim Z As Long",
                "    Debug.Print 1 \\ Z",
                "End Sub"));

            Assert.AreEqual(11, run.First.ErrorNumber, run.First.ErrorMessage);
            Assert.AreEqual(11, run.Second.ErrorNumber, "the file number was free again: " + run.Second.ErrorMessage);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // a program that waits at the error under a debugger is not over: it keeps what it made for as long as it waits.
    [TestMethod]
    public async Task AProgramThatWaitsAtAnUnhandledError_KeepsWhatItMade()
    {
        ExecuteResult? stopped = null;
        long total = -1;
        await ModuleWorkspace.InspectAsync([], Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Total = 7",
            "    Dim Z As Long",
            "    Debug.Print 1 \\ Z",
            "End Sub"), async (provider, first, again) =>
        {
            Assert.AreEqual(ExecutionOutcome.RuntimeError, first.Outcome, first.ErrorMessage);
            _ = await new HostDebugErrorBreakHandler(provider).Handle(new HostDebugErrorBreakParams { Mode = ErrorBreakMode.Unhandled }, CancellationToken.None);
            stopped = await again();
            total = ValueOf(provider, "Program", "Total");
        }, debug: true);

        Assert.AreEqual(ExecutionOutcome.Suspended, stopped!.Outcome, stopped.ErrorMessage);
        Assert.AreEqual(11, stopped.ErrorNumber);
        Assert.AreEqual(7, total);
    }

    // a line a shell runs is a program of its own: one that ends on an error resets the session, the variables a line before it set included.
    [TestMethod]
    [DataRow("Other.Count = 5", ExecutionOutcome.Completed, 5, DisplayName = "a line that completes")]
    [DataRow("Other.Count = 5: Other.Fail", ExecutionOutcome.RuntimeError, 0, DisplayName = "a line that ends on an error")]
    public async Task AShellLineThatEndsOnAnError_ResetsTheSession(string line, ExecutionOutcome outcome, long count)
    {
        var shell = new ReplProgram { AttachedToWorkspace = true };
        long after = -1;
        var result = await ModuleWorkspace.RunSessionModuleAsync(
            [("Other", ModuleWorkspace.StandardModule("Other",
                "Public Count As Long",
                "Public Sub Fail()",
                "    Err.Raise 5",
                "End Sub"))],
            "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n", shell.Module, shell.ToImmediateModuleSource(line), shell.ImmediateEntryPoint,
            afterRun: (_, provider) =>
            {
                after = ValueOf(provider, "Other", "Count");
                return Task.CompletedTask;
            });

        Assert.AreEqual(outcome, result.Outcome, result.ErrorMessage);
        Assert.AreEqual(count, after);
    }
}
