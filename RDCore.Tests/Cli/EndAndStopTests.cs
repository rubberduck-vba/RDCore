using RDCore.Runtime.Support.Host;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;

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

    private static async Task<(ExecuteResult Result, long Total, ExecuteResult Again, long TotalAgain)> RunTwiceAsync(string program)
    {
        ExecuteResult? first = null, second = null;
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

    // a Static variable is given its storage by its procedure's first call, not by the definition of its module: End lets go of it all the same, and the
    // next run's first call gives it storage again, from nothing.
    [TestMethod]
    public async Task End_LeavesNoStaticOfTheProgram_SoTheNextRunCountsFromTheStart()
    {
        var run = await RunTwiceAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Debug.Print Counted(); Counted()",
            "    End",
            "End Sub",
            "Private Function Counted() As Long",
            "    Static Seen As Long",
            "    Seen = Seen + 1",
            "    Counted = Seen",
            "End Function"));

        CollectionAssert.AreEqual(new[] { "1  2" }, run.Result.Output.Select(line => line.Trim()).ToArray());
        CollectionAssert.AreEqual(new[] { "1  2" }, run.Again.Output.Select(line => line.Trim()).ToArray(), "the Static starts from nothing again");
    }

    [TestMethod]
    public async Task End_RunsNoTerminate_AndAProgramThatEndsNormallyDoes()
    {
        var widget = ModuleWorkspace.ClassModule("Widget", "Private Sub Class_Terminate()", "    Debug.Print \"terminated\"", "End Sub");

        ExecuteResult? ended = null, finished = null;
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

    // the value of an object is its default member's, which a statement reads where it needs a value: an End there ends the program, as an End in a function
    // the statement calls does. Nothing after it runs, an On Error Resume Next has no error to go on from, and the next run of the program starts afresh.
    [TestMethod]
    [DataRow("Total = e", DisplayName = "assigned")]
    [DataRow("Total = e + 1", DisplayName = "an operand")]
    [DataRow("If e = 1 Then Total = 9", DisplayName = "compared")]
    [DataRow("Dim i As Long: For i = e To 3: Next", DisplayName = "a For's start")]
    [DataRow("Debug.Print CStr(e)", DisplayName = "converted")]
    [DataRow("Total = Abs(e)", DisplayName = "a function's argument")]
    [DataRow("Debug.Print e", DisplayName = "printed")]
    [DataRow("If e Then Total = 9", DisplayName = "a condition")]
    [DataRow("Select Case e: Case 1: End Select", DisplayName = "a Select Case's test expression")]
    [DataRow("Debug.Print VarType(e)", DisplayName = "VarType's argument")]
    [DataRow("Dim r As Object: Set r = CreateObject(\"VBScript.RegExp\"): r.Pattern = \"a\": Debug.Print r.Replace(\"a\", e)", DisplayName = "a RegExp's replacement")]
    public Task End_InADefaultMemberRead_EndsTheProgramThere(string read) => EndsTheProgramAt(read);

    [TestMethod]
    public async Task End_InADefaultMemberReadByWriteToAFile_EndsTheProgramThere()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rdcore-end-write-{Guid.NewGuid():N}.txt");
        try
        {
            await EndsTheProgramAt($"Dim ff As Integer: ff = FreeFile: Open \"{path}\" For Output As #ff: Write #ff, e");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private async Task EndsTheProgramAt(string read)
    {
        var ender = ModuleWorkspace.ClassModule("Ender", "Option Explicit",
            "Public Property Get Value() As Long", "Attribute Value.VB_UserMemId = 0", "    Value = 1", "    End", "End Property");
        foreach (var policy in new[] { string.Empty, "On Error Resume Next" })
        {
            ExecuteResult? first = null, again = null;
            long total = -1;
            await ModuleWorkspace.InspectAsync([("Ender", ender)], Program(
                "Public Total As Long",
                "Public Sub Main()",
                "    Dim e As New Ender",
                "    " + policy,
                "    Debug.Print \"before \" & Total",
                "    Total = 7",
                "    " + read,
                "    Debug.Print \"after \" & Err.Number",
                "    Total = 8",
                "End Sub"), async (provider, result, runAgain) =>
            {
                first = result;
                total = Total(provider);
                again = await runAgain();
            });

            var said = $"{(policy.Length == 0 ? "no handler" : policy)}: {first!.Outcome} | {string.Join(" ; ", first.Output)} | {first.ErrorNumber} {first.ErrorMessage}";
            Assert.AreEqual(ExecutionOutcome.Halted, first.Outcome, said);
            CollectionAssert.AreEqual(new[] { "before 0" }, first.Output.Select(line => line.Trim()).ToArray(), said);
            Assert.AreEqual(0, total, $"{said}: End reset the variable the program had set to 7");
            CollectionAssert.AreEqual(new[] { "before 0" }, again!.Output.Select(line => line.Trim()).ToArray(), $"{said}: the program run again starts from the start");
        }
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
    public async Task Stop_InAReleaseBuild_EndsTheProgram_AndTheDebugStatementsRun()
    {
        // a release build keeps the Debug statements unless it is told to drop them; the DEBUG constant is False, and Stop ends the program as End does, as
        // in a compiled VB6 executable.
        ExecuteResult? result = null;
        long total = -1;
        await ModuleWorkspace.InspectAsync(NoClasses, Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Debug.Print \"kept\"",
            "    Debug.Assert True",
            "#If DEBUG Then",
            "    Exit Sub",
            "#End If",
            "    Total = 7",
            "    Stop",
            "    Total = 8",
            "End Sub"), (provider, run, _) =>
            {
                result = run;
                total = Total(provider);
                return Task.CompletedTask;
            }, debug: true, configuration: VBBuildConfiguration.Release);

        Assert.AreEqual(ExecutionOutcome.Halted, result!.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "kept" }, result.Output.Select(line => line.Trim()).ToArray());
        Assert.AreEqual(0, total, "End leaves nothing of the program, and neither does a Stop in a release build");
    }

    [TestMethod]
    [DataRow(false, ExecutionOutcome.Suspended, DisplayName = "kept: it breaks, as in a debug build")]
    [DataRow(true, ExecutionOutcome.Completed, DisplayName = "dropped: nothing is asserted")]
    public async Task AnAssertionThatFails_InAReleaseBuild(bool drops, ExecutionOutcome expected)
    {
        ExecuteResult? result = null;
        await ModuleWorkspace.InspectAsync(NoClasses, Program(
            "Public Sub Main()",
            "    Debug.Assert False",
            "End Sub"), (_, run, _) =>
            {
                result = run;
                return Task.CompletedTask;
            }, debug: true, configuration: VBBuildConfiguration.Release, releaseDropsDebugStatements: drops);

        Assert.AreEqual(expected, result!.Outcome, $"{result.Outcome}: {result.ErrorMessage}");
    }

    [TestMethod]
    [DataRow(false, 1L, DisplayName = "kept: the function is called, and what it returns printed")]
    [DataRow(true, 0L, DisplayName = "dropped: the function is not called")]
    public async Task ADebugPrintOfACall_InAReleaseBuild(bool drops, long calls)
    {
        ExecuteResult? result = null;
        long total = -1;
        await ModuleWorkspace.InspectAsync(NoClasses, Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Debug.Print Touched()",
            "End Sub",
            "Private Function Touched() As Long",
            "    Total = Total + 1",
            "    Touched = Total",
            "End Function"), (provider, run, _) =>
            {
                result = run;
                total = Total(provider);
                return Task.CompletedTask;
            }, debug: true, configuration: VBBuildConfiguration.Release, releaseDropsDebugStatements: drops);

        Assert.AreEqual(ExecutionOutcome.Completed, result!.Outcome, result.ErrorMessage);
        Assert.AreEqual(calls, total);
        CollectionAssert.AreEqual(drops ? Array.Empty<string>() : ["1"], result.Output.Select(line => line.Trim()).ToArray());
    }

    [TestMethod]
    public async Task Stop_InAReleaseBuildThatDropsTheDebugStatements_EndsTheProgram_AndEveryDebugCallIsLeftOut()
    {
        // nothing is printed and nothing is asserted: the arguments are not evaluated.
        ExecuteResult? result = null;
        long total = -1;
        await ModuleWorkspace.InspectAsync(NoClasses, Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Debug.Print \"dropped\"",
            "    Debug.Assert False",
            "#If DEBUG Then",
            "    Exit Sub",
            "#End If",
            "    Total = 7",
            "    Stop",
            "    Total = 8",
            "End Sub"), (provider, run, _) =>
            {
                result = run;
                total = Total(provider);
                return Task.CompletedTask;
            }, debug: true, configuration: VBBuildConfiguration.Release, releaseDropsDebugStatements: true);

        Assert.AreEqual(ExecutionOutcome.Halted, result!.Outcome, result.ErrorMessage);
        Assert.IsEmpty(result.Output);
        Assert.AreEqual(0, total, "End leaves nothing of the program, and neither does a Stop in a release build");
    }

    private static long LocalOf(EnvironmentSessionProvider provider, ICallStackFrame frame, string name)
    {
        var symbols = provider.Session.Symbols;
        Assert.IsTrue(symbols.TryResolveValue("Program", GlobalSymbols.UnresolvedSymbol, out var module));
        Assert.IsTrue(symbols.TryResolveValue(frame.StaticSymbol.Name, module!, out var resolved));
        var procedure = (VBProcedureMemberSymbol)resolved!;
        var local = procedure.Locals.Cast<Symbol>().Concat(procedure.Parameters).Single(symbol => symbol.Name == name);
        return Convert.ToInt64(frame.GetValue(local).Value.BoxedValue);
    }

    [TestMethod]
    public async Task Stop_LeavesTheActivationsOfTheProgramOnTheStack_WithTheirLocals()
    {
        var program = Program(
            "Public Sub Main()",
            "    Dim outer As Long",
            "    outer = 5",
            "    Helper 9",
            "End Sub",
            "Private Sub Helper(ByVal arg As Long)",
            "    Dim inner As Long",
            "    inner = arg + 1",
            "    Stop",
            "End Sub");

        await ModuleWorkspace.InspectAsync(NoClasses, program, async (provider, result, runAgain) =>
        {
            var stack = provider.Session.CallStack;

            Assert.AreEqual(ExecutionOutcome.Interrupted, result.Outcome, result.ErrorMessage);
            Assert.AreEqual(2, stack.Depth);
            Assert.AreEqual("Helper", stack.Current!.StaticSymbol.Name);
            Assert.AreEqual(10, LocalOf(provider, stack.Current, "inner"));
            Assert.AreEqual(9, LocalOf(provider, stack.Current, "arg"));
            Assert.AreEqual(5, LocalOf(provider, stack.Frames.Last(), "outer"));

            var again = await runAgain();

            Assert.AreEqual(ExecutionOutcome.Interrupted, again.Outcome, again.ErrorMessage);
            Assert.AreEqual(2, stack.Depth, "a program that is started lets go of the one that was stopped");
        });
    }

    [TestMethod]
    public async Task AProgramThatRunsToItsEnd_LeavesNothingOnTheStack()
        => await ModuleWorkspace.InspectAsync(NoClasses, Program("Public Sub Main()", "    Debug.Print 1", "End Sub"), (provider, result, _) =>
        {
            Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
            Assert.AreEqual(0, provider.Session.CallStack.Depth);
            return Task.CompletedTask;
        });

    [TestMethod]
    public async Task End_LeavesNothingOnTheStack()
        => await ModuleWorkspace.InspectAsync(NoClasses, Program("Public Sub Main()", "    Helper", "End Sub", "Private Sub Helper()", "    End", "End Sub"), (provider, result, _) =>
        {
            Assert.AreEqual(ExecutionOutcome.Halted, result.Outcome, result.ErrorMessage);
            Assert.AreEqual(0, provider.Session.CallStack.Depth);
            return Task.CompletedTask;
        });

    [TestMethod]
    public async Task AFailedAssert_BreaksAtItself_AsAStopDoes()
    {
        var run = await RunTwiceAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Total = 1",
            "    Debug.Assert Total = 2",
            "    Total = 3",
            "End Sub"));

        Assert.AreEqual(ExecutionOutcome.Interrupted, run.Result.Outcome, run.Result.ErrorMessage);
        Assert.AreEqual(3, run.Result.ErrorLine, "the fourth line of the module");
        Assert.AreEqual(1, run.Total);
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

    // the program is in its loop from the moment its MsgBox is answered: every instruction after that is the Do's or the Loop's, so the break, asked for once the
    // client answered, can only stop the loop, which proves the loop answers a break, compiled as it runs.
    [TestMethod]
    public async Task ABreakFromOutside_IsAStop_OfWhateverTheProgramWasDoing()
    {
        string[] lines =
        [
            "Public Total As Long",
            "Public Sub Main()",
            "    Total = 3",
            "    MsgBox \"in\"",
            "    Do",
            "    Loop",
            "End Sub",
        ];
        var client = new AnswersOk();
        ExecuteResult? result = null;
        long total = 0;
        await ModuleWorkspace.InspectAsync(NoClasses, Program(lines), (provider, outcome, _) =>
        {
            (result, total) = (outcome, Total(provider));
            return Task.CompletedTask;
        }, cancelWhen: _ => client.Asked, interaction: client);

        Assert.AreEqual(ExecutionOutcome.Interrupted, result!.Outcome, result.ErrorMessage);
        CollectionAssert.Contains(new[] { Array.IndexOf(lines, "    Do"), Array.IndexOf(lines, "    Loop") }, result.ErrorLine,
            "it stopped in the loop, and says which line it was on");
        Assert.AreEqual(3, total);
    }
}
