using RDCore.CLI.Host;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>End</c> and <c>Stop</c>, and the break at a keyboard that does what <c>Stop</c> does. An <c>End</c> is over and leaves nothing of the program, and takes no
/// <c>Terminate</c> with it; a <c>Stop</c> stops where it is, and the variables stay as the program made them.
/// </summary>
[TestClass]
public sealed class EndAndStopTests
{
    private static readonly (string Name, string Source)[] NoClasses = [];

    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static long Total(EnvironmentSessionProvider provider)
    {
        var session = provider.Session;
        Assert.IsTrue(session.Symbols.TryResolveValue("Program", GlobalSymbols.UnresolvedSymbol, out var module));
        Assert.IsTrue(session.Symbols.TryResolveValue("Total", module!, out var total));
        return Convert.ToInt64(session.Symbols.Resolver.GetValue(total!).Value.BoxedValue);
    }

    private static async Task<(ExecuteSessionResult Result, long Total, ExecuteSessionResult Again, long TotalAgain)> RunTwiceAsync(string program)
    {
        ExecuteSessionResult? first = null, second = null;
        long total = 0, totalAgain = 0;
        await ModuleWorkspace.InspectAsync(NoClasses, program, async (provider, result, runAgain) =>
        {
            first = result;
            total = Total(provider);
            second = await runAgain();
            totalAgain = Total(provider);
        });

        return (first!, total, second!, totalAgain);
    }

    // ---- End ----

    [TestMethod]
    public async Task End_StopsTheProgram_WhereverItIsCalledFrom()
    {
        var run = await RunTwiceAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Debug.Print \"before\"",
            "    Helper",
            "    Debug.Print \"after\"",
            "End Sub",
            "Private Sub Helper()",
            "    End",
            "End Sub"));

        Assert.AreEqual(ExecutionOutcome.Halted, run.Result.Outcome, run.Result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "before" }, run.Result.Output.Select(line => line.Trim()).ToArray());
    }

    [TestMethod]
    public async Task End_InTheMiddleOfAnExpression_IsNotAnErrorOfTheStatementThatWaitedForIt()
    {
        var run = await RunTwiceAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Total = Compute() + 1",
            "    Debug.Print \"not reached\"",
            "End Sub",
            "Private Function Compute() As Long",
            "    Compute = 1",
            "    End",
            "End Function"));

        Assert.AreEqual(ExecutionOutcome.Halted, run.Result.Outcome, run.Result.ErrorMessage);
        Assert.IsEmpty(run.Result.Output);
        Assert.AreEqual(0, run.Total, "the assignment that was waiting for the function never happened");
    }

    [TestMethod]
    public async Task End_LeavesNothingOfTheProgram_SoTheNextRunStartsFromTheStart()
    {
        var run = await RunTwiceAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Debug.Print Total",
            "    Total = 7",
            "    End",
            "End Sub"));

        CollectionAssert.AreEqual(new[] { "0" }, run.Result.Output.Select(line => line.Trim()).ToArray());
        Assert.AreEqual(0, run.Total, "the variable is what it was defined as");
        CollectionAssert.AreEqual(new[] { "0" }, run.Again.Output.Select(line => line.Trim()).ToArray(), "and so it is to the program that runs next");
        Assert.AreEqual(ExecutionOutcome.Halted, run.Again.Outcome);
    }

    [TestMethod]
    public async Task End_RunsNoTerminate_AndAProgramThatEndsNormallyDoes()
    {
        var widget = ModuleWorkspace.ClassModule("Widget", "Private Sub Class_Terminate()", "    Debug.Print \"terminated\"", "End Sub");

        ExecuteSessionResult? ended = null, finished = null;
        await ModuleWorkspace.InspectAsync([("Widget", widget)], Program(
            "Public Sub Main()",
            "    Dim w As Widget",
            "    Set w = New Widget",
            "    Debug.Print \"made\"",
            "    End",
            "End Sub"), (_, result, _) =>
        {
            ended = result;
            return Task.CompletedTask;
        });
        await ModuleWorkspace.InspectAsync([("Widget", widget)], Program(
            "Public Sub Main()",
            "    Dim w As Widget",
            "    Set w = New Widget",
            "    Debug.Print \"made\"",
            "End Sub"), (_, result, _) =>
        {
            finished = result;
            return Task.CompletedTask;
        });

        CollectionAssert.AreEqual(new[] { "made" }, ended!.Output.Select(line => line.Trim()).ToArray(), "the object was not let go of: nothing of the program is left to run its Terminate");
        CollectionAssert.AreEqual(new[] { "made", "terminated" }, finished!.Output.Select(line => line.Trim()).ToArray());
    }

    [TestMethod]
    public async Task End_IsNotAnError_ThatOnErrorResumeNextCouldSwallow()
    {
        var run = await RunTwiceAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    On Error Resume Next",
            "    End",
            "    Debug.Print \"not reached\"",
            "End Sub"));

        Assert.AreEqual(ExecutionOutcome.Halted, run.Result.Outcome);
        Assert.IsEmpty(run.Result.Output);
    }

    // ---- Stop ----

    [TestMethod]
    public async Task Stop_StopsWhereItIs_AndTheVariablesStayAsTheProgramMadeThem()
    {
        var run = await RunTwiceAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Debug.Print Total",
            "    Total = 7",
            "    Stop",
            "    Debug.Print \"not reached\"",
            "End Sub"));

        Assert.AreEqual(ExecutionOutcome.Interrupted, run.Result.Outcome, run.Result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "0" }, run.Result.Output.Select(line => line.Trim()).ToArray());
        Assert.AreEqual(7, run.Total);
        CollectionAssert.AreEqual(new[] { "7" }, run.Again.Output.Select(line => line.Trim()).ToArray(), "unlike an End, a Stop leaves the session as it found it");
    }

    [TestMethod]
    public async Task Stop_SaysWhereItStopped()
    {
        var run = await RunTwiceAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Total = 1",
            "    Stop",
            "End Sub"));

        Assert.AreEqual(3, run.Result.ErrorLine, "the fourth line of the module");
    }

    [TestMethod]
    public async Task Stop_InTheMiddleOfAnExpression_StopsTheProgram()
    {
        var run = await RunTwiceAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Total = Compute() + 1",
            "End Sub",
            "Private Function Compute() As Long",
            "    Stop",
            "End Function"));

        Assert.AreEqual(ExecutionOutcome.Interrupted, run.Result.Outcome, run.Result.ErrorMessage);
        Assert.AreEqual(0, run.Total);
    }

    // ---- in the shell's language ----

    [TestMethod]
    public async Task ABasicProgram_EndsAtItsEnd_AndStopsAtItsStop()
    {
        var ended = await ShellHost.Compose().RunAsync([(10, "PRINT 1"), (20, "END"), (30, "PRINT 2")]);
        var stopped = await ShellHost.Compose().RunAsync([(10, "PRINT 1"), (20, "STOP"), (30, "PRINT 2")]);

        Assert.AreEqual(ExecutionOutcome.Halted, ended.Outcome, ended.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "1" }, ended.Output.Select(line => line.Trim()).ToArray());
        Assert.AreEqual(ExecutionOutcome.Interrupted, stopped.Outcome, stopped.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "1" }, stopped.Output.Select(line => line.Trim()).ToArray());
    }

    [TestMethod]
    public async Task AnEnd_InsideABlock_StopsTheProgramAllTheSame()
    {
        var result = await ShellHost.Compose().RunAsync([(10, "FOR I = 1 TO 3"), (20, "PRINT I"), (30, "IF I = 2 THEN END"), (40, "NEXT")]);

        Assert.AreEqual(ExecutionOutcome.Halted, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "1", "2" }, result.Output.Select(line => line.Trim()).ToArray());
    }

    // ---- a break from outside ----

    [TestMethod]
    public async Task ABreakFromOutside_IsAStop_OfWhateverTheProgramWasDoing()
    {
        ExecuteSessionResult? result = null;
        await ModuleWorkspace.InspectAsync(NoClasses, Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Total = 3",
            "    Do",
            "    Loop",
            "End Sub"), (_, outcome, _) =>
        {
            result = outcome;
            return Task.CompletedTask;
        }, cancelAfter: TimeSpan.FromMilliseconds(300));

        Assert.AreEqual(ExecutionOutcome.Interrupted, result!.Outcome, result.ErrorMessage);
        Assert.IsGreaterThanOrEqualTo(3, result.ErrorLine, "it stopped in the loop, and says which line it was on");
    }
}
