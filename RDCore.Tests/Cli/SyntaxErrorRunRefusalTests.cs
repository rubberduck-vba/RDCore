using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.Parsing;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Platform.Protocol;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Workspace;
using RDCore.Tests.Platform;

namespace RDCore.Tests.Cli;

/// <summary>
/// A module with a syntax error does not run (<strong>RD-VBAL §2.6.1</strong>): a run of it is refused with its errors before any of it runs, whatever the error is:
/// a statement the parser stopped in, a statement no rule reads, two output items written side by side (<c>VBC00061</c>), a line continued past the limit. The
/// statements around the error do not run without it.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 2.6.1 Syntax Errors")]
public sealed class SyntaxErrorRunRefusalTests
{
    private static string Program(params string[] body)
        => string.Join("\r\n", ["Attribute VB_Name = \"Program\"", "Public Sub Main()", .. body.Select(line => "    " + line), "End Sub", ""]);

    [TestMethod]
    [DataRow("Debug.Print 1 +", DisplayName = "a binary operator the parse reads, at the end of its statement")]
    [DataRow("Debug.Print 1 -", DisplayName = "a binary operator the parse leaves unread, at the end of its statement")]
    [DataRow("a = 1-", DisplayName = "a statement no rule reads")]
    [DataRow("If a = 1 - Then", DisplayName = "a block If no rule reads")]
    [DataRow("a = = 1", DisplayName = "an operator where an expression is")]
    [DataRow("Debug.Print 1(2)", DisplayName = "two output items side by side")]
    [DataRow("a = 1 +\r\na = 2", DisplayName = "a binary operator at the end of its line, then a statement")]
    [DataRow("a = 1 &\r\na = 2", DisplayName = "a concatenation at the end of its line, then a statement")]
    [DataRow("a = 1 + _\r\n\r\na = 2", DisplayName = "a binary operator, a line continuation and a blank line, then a statement")]
    [DataRow("a = 1 -\r\na = 2", DisplayName = "an operator the parse leaves unread at the end of its line, then a statement")]
    [DataRow("Print #1, 1 +\r\nClose #1", DisplayName = "an item of a Print at the end of its line, then a Close")]
    [DataRow("Debug.Print 1 +\r\nDebug.Print 2", DisplayName = "an item of Debug.Print at the end of its line, then another")]
    [DataRow("a = Array(1 -, 2)", DisplayName = "a binary operator before a comma")]
    [DataRow("Dim d(1 -)", DisplayName = "a binary operator before the parenthesis of a Dim")]
    [DataRow("ReDim d(1 -)", DisplayName = "a binary operator before the parenthesis of a ReDim")]
    [DataRow("Call Foo(1) -", DisplayName = "a binary operator after a Call")]
    [DataRow("a = 1 - -", DisplayName = "two binary operators")]
    [DataRow("Debug.Print Tab(2) -", DisplayName = "a binary operator after a Tab clause")]
    [DataRow("Debug.Print 1; 2 *", DisplayName = "a binary operator after the second item")]
    [DataRow("If a Then a = 1 -", DisplayName = "a binary operator in the statement of an inline If")]
    public async Task AModuleWithASyntaxError_IsRefused_BeforeAnyOfItRuns(string statement)
    {
        var result = await ModuleWorkspace.RunWithSyntaxErrorsAsync(Program(
            "Dim a As Variant",
            "Debug.Print \"before\"",
            statement,
            "Debug.Print \"after\""));

        Assert.AreEqual(ExecutionOutcome.SyntaxError, result.Outcome, result.ErrorMessage);
        Assert.IsEmpty(result.Output);
        Assert.IsNotEmpty(result.Diagnostics!);
    }

    // a syntax error in a branch of a #If that is excluded is in no code: the branch is not compiled, and the module runs, the statements around the branch
    // with it.
    [TestMethod]
    [DataRow(new[] { "#If False Then", "Debug.Print 1 +", "#End If" }, new[] { "before", "after" }, DisplayName = "in the branch of a #If False")]
    [DataRow(new[] { "#If True Then", "Debug.Print \"live\"", "#Else", "a = = 1", "#End If" }, new[] { "before", "live", "after" },
        DisplayName = "in the #Else of a #If True")]
    [DataRow(new[] { "#If False Then", "a = (1 +", "#End If", "Debug.Print \"next\"" }, new[] { "before", "next", "after" },
        DisplayName = "in the branch of a #If False, then a statement")]
    public async Task ASyntaxErrorInAnExcludedBranch_DoesNotRefuseTheRun(string[] lines, string[] printed)
    {
        var result = await ModuleWorkspace.RunWithSyntaxErrorsAsync(Program(["Dim a As Variant", "Debug.Print \"before\"", .. lines, "Debug.Print \"after\""]));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, string.Join("; ", result.Diagnostics));
        CollectionAssert.AreEqual(printed, result.Output.ToArray());
    }

    // a block that a branch of a #If that is excluded opens and does not close is reported where its end was expected, in live code, and the statements after
    // the branch are read into it: RD-VBA refuses the run, and says why. MS-VBA runs it (RD-VBAL 2.6.1).
    [TestMethod]
    [DataRow("If a Then", DisplayName = "a block If")]
    [DataRow("With a", DisplayName = "a With")]
    [DataRow("For i = 1 To 2", DisplayName = "a For")]
    [DataRow("Select Case a", DisplayName = "a Select Case, which reads no statement before its first Case")]
    public async Task ABlockAnExcludedBranchOpensAndDoesNotClose_RefusesTheRun_AndSaysSo(string opened)
    {
        var result = await ModuleWorkspace.RunWithSyntaxErrorsAsync(Program(
            "Dim a As Variant, i As Long", "Debug.Print \"before\"", "#If False Then", opened, "#End If", "Debug.Print \"after\""));

        Assert.AreEqual(ExecutionOutcome.SyntaxError, result.Outcome, result.ErrorMessage);
        Assert.IsEmpty(result.Output);
        Assert.IsTrue(result.Diagnostics!.Any(error => error.Contains(RDCore.Runtime.Exceptions.VBCompileError_BlockNotClosedInItsBranch_Verbose, StringComparison.Ordinal)),
            string.Join("; ", result.Diagnostics!));
    }

    // a syntax error in live code after such a block is the error it is, said with its own message; the block's is said once, at its end. A name or a number
    // can begin a label, which can be written before the end of a block (L1: End If): the error at one is no block's.
    [TestMethod]
    [DataRow("a = = 1", DisplayName = "an operator where an expression is")]
    [DataRow("With a", DisplayName = "a With not closed")]
    [DataRow("b = 1 c", DisplayName = "a name after the end of an expression")]
    [DataRow("b = 1 2", DisplayName = "a number after the end of an expression")]
    [DataRow("b = 1 Loop", DisplayName = "a Loop after the end of an expression")]
    [DataRow("b = 1 Next", DisplayName = "a Next after the end of an expression")]
    [DataRow("b = 1 Wend", DisplayName = "a Wend after the end of an expression")]
    [DataRow("b = 1 Else", DisplayName = "an Else after the end of an expression")]
    [DataRow("b = 1 End", DisplayName = "an End after the end of an expression")]
    [DataRow("b = -1 Loop", DisplayName = "a Loop after the end of an expression with a sign")]
    public async Task ALiveSyntaxErrorAfterABlockAnExcludedBranchDoesNotClose_RefusesTheRun_WithItsOwnError(string live)
    {
        var result = await ModuleWorkspace.RunWithSyntaxErrorsAsync(Program(
            "Dim a As Variant, b As Variant, c As Variant", "#If False Then", "If a Then", "#End If", live));

        Assert.AreEqual(ExecutionOutcome.SyntaxError, result.Outcome, result.ErrorMessage);
        var said = string.Join("; ", result.Diagnostics!);
        Assert.IsTrue(result.Diagnostics!.Any(error => !error.Contains(RDCore.Runtime.Exceptions.VBCompileError_BlockNotClosedInItsBranch_Verbose, StringComparison.Ordinal)), said);
        Assert.IsLessThanOrEqualTo(1, result.Diagnostics!.Count(error => error.Contains(RDCore.Runtime.Exceptions.VBCompileError_BlockNotClosedInItsBranch_Verbose, StringComparison.Ordinal)),
            said);
    }

    // the end of a block where a statement begins - first on its line, after a separator, after a label or the number of its line - is where the block that
    // the excluded branch opens is not closed: the block's message is said.
    [TestMethod]
    [DataRow("Loop", DisplayName = "first on its line")]
    [DataRow("10 Loop", DisplayName = "after the number of its line")]
    [DataRow("-10 Loop", DisplayName = "after the number of its line, with a sign")]
    [DataRow("L1: Loop", DisplayName = "after a label")]
    [DataRow("b = 1: Loop", DisplayName = "after a separator")]
    public async Task TheEndOfABlockWhereAStatementBegins_AfterABlockAnExcludedBranchDoesNotClose_IsTheBlocks(string live)
    {
        var result = await ModuleWorkspace.RunWithSyntaxErrorsAsync(Program(
            "Dim a As Variant, b As Variant", "#If False Then", "If a Then", "#End If", live));

        Assert.AreEqual(ExecutionOutcome.SyntaxError, result.Outcome, result.ErrorMessage);
        Assert.IsTrue(result.Diagnostics!.Any(error => error.Contains(RDCore.Runtime.Exceptions.VBCompileError_BlockNotClosedInItsBranch_Verbose, StringComparison.Ordinal)),
            string.Join("; ", result.Diagnostics!));
    }

    // a syntax error in the header of a procedure that is not run refuses the run of the module as well.
    [TestMethod]
    public async Task ASyntaxErrorInTheHeaderOfAnotherProcedure_RefusesTheRun()
    {
        var program = string.Join("\r\n",
            "Attribute VB_Name = \"Program\"",
            "Public Sub F(Optional ByVal a As Long = 1 -)",
            "End Sub",
            "Public Sub Main()",
            "    Debug.Print \"before\"",
            "End Sub",
            "");

        var result = await ModuleWorkspace.RunWithSyntaxErrorsAsync(program);

        Assert.AreEqual(ExecutionOutcome.SyntaxError, result.Outcome, result.ErrorMessage);
        Assert.IsEmpty(result.Output);
    }

    // a procedure with no End Sub is a syntax error at the header of the procedure after it: the run is refused, and neither procedure runs.
    [TestMethod]
    [DataRow("Sub Other()", DisplayName = "before a Sub")]
    [DataRow("Public Sub Other()", DisplayName = "before a Public Sub")]
    [DataRow("Private Function Other() As Long", DisplayName = "before a Private Function")]
    public async Task AProcedureWithNoEndSub_RefusesTheRun(string header)
    {
        var program = string.Join("\r\n",
            "Attribute VB_Name = \"Program\"",
            "Public Sub Main()",
            "    Debug.Print \"before\"",
            "",
            header,
            "    Debug.Print \"other\"",
            header.Contains("Function") ? "End Function" : "End Sub",
            "");

        var result = await ModuleWorkspace.RunWithSyntaxErrorsAsync(program);

        Assert.AreEqual(ExecutionOutcome.SyntaxError, result.Outcome, result.ErrorMessage);
        Assert.IsEmpty(result.Output);
        Assert.IsNotEmpty(result.Diagnostics!);
    }

    private static string Module(string name, params string[] lines) => string.Join("\r\n", [$"Attribute VB_Name = \"{name}\"", .. lines, ""]);

    // a syntax error anywhere in the project refuses every run, with the module it is in named before it: nothing of the project runs while a module of it
    // does not load. MS-VBA compiles what a run needs (with Compile On Demand on, the procedures the run reaches and every module's declarations section;
    // off, every module the run uses), so it runs some of what is refused here (RD-VBAL 2.6.1).
    [TestMethod]
    [DataRow("P", "", DisplayName = "in another procedure of the module run")]
    [DataRow("B", "", DisplayName = "in a procedure of another module, none of it called")]
    [DataRow("B", "Good", DisplayName = "in a procedure of another module, another procedure of it called")]
    [DataRow("B", "Bad", DisplayName = "in a procedure of another module, that procedure called")]
    [DataRow("D", "", DisplayName = "in the declarations section of another module, none of it called")]
    [DataRow("D", "Good", DisplayName = "in the declarations section of another module, a procedure of it called")]
    public async Task ASyntaxErrorAnywhereInTheProject_RefusesTheRun_WithTheModuleItIsIn(string where, string call)
    {
        var (program, other) = ProgramAndOther(where, call);

        var result = await ModuleWorkspace.RunWithSyntaxErrorsAsync(program, [("Other", other)]);

        Assert.AreEqual(ExecutionOutcome.SyntaxError, result.Outcome, result.ErrorMessage);
        Assert.IsEmpty(result.Output);
        var prefix = where == "P" ? "" : "Other: ";
        StringAssert.StartsWith(result.Diagnostics[0], prefix + RDCore.SDK.Exceptions.VBSyntaxError_DefaultMessage, string.Join("; ", result.Diagnostics));
    }

    // a syntax error in the declarations section of a module the run calls is the one error the run is refused with: the parse reads the procedures after
    // the declaration's line, and the one called is declared, not said not to be found.
    [TestMethod]
    public async Task ASyntaxErrorInTheDeclarationsOfAModuleTheRunCalls_IsTheOneErrorOfTheRefusal()
    {
        var (program, other) = ProgramAndOther("D", "Good");

        var result = await ModuleWorkspace.RunWithSyntaxErrorsAsync(program, [("Other", other)]);

        Assert.AreEqual(ExecutionOutcome.SyntaxError, result.Outcome, result.ErrorMessage);
        StringAssert.StartsWith(Assert.ContainsSingle(result.Diagnostics!), "Other: " + RDCore.SDK.Exceptions.VBSyntaxError_DefaultMessage,
            string.Join("; ", result.Diagnostics!));
    }

    // the controls: the same modules with no syntax error run.
    [TestMethod]
    [DataRow("", new[] { "before", "after" }, DisplayName = "nothing of the other module called")]
    [DataRow("Good", new[] { "before", "good", "after" }, DisplayName = "a procedure of the other module called")]
    public async Task TheSameModulesWithNoSyntaxError_Run(string call, string[] printed)
    {
        var (program, other) = ProgramAndOther("", call);

        var result = await ModuleWorkspace.RunWithSyntaxErrorsAsync(program, [("Other", other)]);

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, string.Join("; ", result.Diagnostics));
        CollectionAssert.AreEqual(printed, result.Output.ToArray());
    }

    // Program.Main notes "before", calls nothing or a procedure of Other, then notes "after"; the syntax error is in another procedure of Program (P), in
    // Other.Bad (B), or in Other's declarations section (D).
    private static (string Program, string Other) ProgramAndOther(string where, string call)
        => (Module("Program",
                "Public Sub Main()",
                "    Debug.Print \"before\"",
                call.Length > 0 ? $"    Other.{call}" : "",
                "    Debug.Print \"after\"",
                "End Sub",
                "Public Sub Unrelated()",
                where == "P" ? "    Debug.Print 1 +" : "",
                "End Sub"),
            Module("Other",
                where == "D" ? "Public x As Integer +" : "",
                "Public Sub Good()",
                "    Debug.Print \"good\"",
                "End Sub",
                "Public Sub Bad()",
                where == "B" ? "    Debug.Print 1 +" : "",
                "End Sub"));

    // the modules that do not load are said in the order of their names, whatever order they were loaded in, each error after the module's name.
    [TestMethod]
    public async Task TheModulesThatDoNotLoad_AreSaidInTheOrderOfTheirNames()
    {
        var (program, _) = ProgramAndOther("", "");
        var bad = Module("Zed", "Public Sub Bad()", "    Debug.Print 1 +", "End Sub");

        // six modules, so that an order that is not the names' is all but certain to show.
        string[] loaded = ["Zed", "Mike", "Alpha", "Kilo", "Bravo", "Yankee"];

        var result = await ModuleWorkspace.RunWithSyntaxErrorsAsync(program, [.. loaded.Select(name => (name, bad.Replace("\"Zed\"", $"\"{name}\"")))]);

        Assert.AreEqual(ExecutionOutcome.SyntaxError, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(loaded.Order(StringComparer.OrdinalIgnoreCase).ToArray(), result.Diagnostics!.Select(error => error[..error.IndexOf(':')]).ToArray(),
            string.Join("; ", result.Diagnostics!));
    }

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static readonly (string Name, ModuleType Type, string Source) GoodOther = ("Other", ModuleType.StdModule, ProgramAndOther("", "").Other);
    private static readonly (string Name, ModuleType Type, string Source) BadOther = ("Other", ModuleType.StdModule, ProgramAndOther("B", "").Other);

    // Main notes "before", waits at its Stop, then calls a procedure of Other and notes "after".
    private static string StoppedProgram(string call = "Good") => Module("Program",
        "Public Sub Main()",
        "    Debug.Print \"before\"",
        "    Stop",
        $"    Other.{call}",
        "    Debug.Print \"after\"",
        "End Sub");

    private static Task<ExecuteResult> Resume(EnvironmentSessionProvider provider)
        => new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
            .Handle(new HostDebugResumeParams(), CancellationToken.None).WaitAsync(Patience);

    // the program waits at its Stop, and a module it calls is edited to a syntax error: the program is not resumed on the code the module had, nor on what of
    // it did not load.
    [TestMethod]
    [DataRow("Good", DisplayName = "a procedure of the module called, not the one the error is in")]
    [DataRow("Bad", DisplayName = "the procedure the error is in called")]
    public async Task AModuleThatDoesNotLoad_AtAStop_IsNotResumedOn(string call)
    {
        ExecuteResult? resumed = null;
        await ModuleWorkspace.InspectWithModulesAsync([("Other", GoodOther.Source)], StoppedProgram(call), async (provider, stopped, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);
            _ = await ModuleWorkspace.ReloadOnlyAsync(provider, ["Other"], syntaxErrors: true, ("Program", ModuleType.StdModule, StoppedProgram(call)), BadOther);
            resumed = await Resume(provider);
        }, debug: true);

        Assert.AreEqual(ExecutionOutcome.Refused, resumed!.Outcome, resumed.ErrorMessage);
        StringAssert.Contains(resumed.ErrorMessage, "Other");
        StringAssert.Contains(resumed.ErrorMessage, "does not compile");
        Assert.IsEmpty(resumed.Output, "nothing after the Stop runs");
    }

    // what a debugger evaluates or runs at the stop is code of the project too, which can call into the module that does not load.
    [TestMethod]
    [DataRow(false, DisplayName = "an expression evaluated")]
    [DataRow(true, DisplayName = "a statement run")]
    public async Task AModuleThatDoesNotLoad_AtAStop_RefusesWhatADebuggerRunsThere(bool statement)
    {
        DebugEvaluateResult? answer = null;
        await ModuleWorkspace.InspectWithModulesAsync([("Other", GoodOther.Source)], StoppedProgram(), async (provider, stopped, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);
            _ = await ModuleWorkspace.ReloadOnlyAsync(provider, ["Other"], syntaxErrors: true, ("Program", ModuleType.StdModule, StoppedProgram()), BadOther);
            var frame = provider.Execution.Stack().Frames[0].Id;
            var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Typed.bas"), "Public Sub E()\r\n__e = 1 + 1\r\nEnd Sub\r\n");
            var typed = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single().Children.OfType<AssignmentStatementNode>().Single();
            answer = statement
                ? await new HostDebugExecuteHandler(provider, Substitute.For<IErrorDetailsRecorder>())
                    .Handle(new HostDebugExecuteParams { FrameId = frame, Json = PlatformJson.Serialize<SyntaxNode>(typed) }, CancellationToken.None).WaitAsync(Patience)
                : await new HostDebugEvaluateHandler(provider, Substitute.For<IErrorDetailsRecorder>())
                    .Handle(new HostDebugEvaluateParams { FrameId = frame, Json = PlatformJson.Serialize(typed.Value) }, CancellationToken.None).WaitAsync(Patience);
        }, debug: true);

        Assert.IsFalse(answer!.Success);
        StringAssert.StartsWith(answer.Error, "Other: " + RDCore.SDK.Exceptions.VBSyntaxError_DefaultMessage, answer.Error);
    }

    // a module taken out of the session is no module of the project: what it failed to load with refuses no run after it.
    [TestMethod]
    public async Task AModuleThatDoesNotLoad_Discarded_RefusesTheRunNoLonger()
    {
        var (program, _) = ProgramAndOther("", "");
        ExecuteResult? whileThere = null, discarded = null;
        await ModuleWorkspace.InspectWithModulesAsync([("Other", GoodOther.Source)], program, async (provider, ran, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Completed, ran.Outcome, ran.ErrorMessage);
            _ = await ModuleWorkspace.ReloadOnlyAsync(provider, ["Other"], syntaxErrors: true, ("Program", ModuleType.StdModule, program), BadOther);
            whileThere = await runAgain();
            _ = await new HostDiscardHandler(provider, NullLogger<HostDiscardHandler>.Instance).Handle(new HostDiscardParams
            {
                ModuleUri = new UriBuilder(Wire.FileUri(ModuleWorkspace.Root)) { Fragment = "Other" }.Uri,
                ModuleName = "Other",
            }, CancellationToken.None).WaitAsync(Patience);
            discarded = await runAgain();
        });

        Assert.AreEqual(ExecutionOutcome.SyntaxError, whileThere!.Outcome, whileThere.ErrorMessage);
        Assert.AreEqual(ExecutionOutcome.Completed, discarded!.Outcome, string.Join("; ", discarded.Diagnostics ?? []));
        CollectionAssert.AreEqual(new[] { "before", "after" }, discarded.Output.Select(line => line.Trim()).ToArray());
    }

    // a request that defines a module's symbols, and carries no parse of it, loads none of its code: what the code failed to load with still refuses the run.
    [TestMethod]
    public async Task AModuleThatDoesNotLoad_GivenItsSymbolsAlone_StillRefusesTheRun()
    {
        var (program, _) = ProgramAndOther("", "");
        ExecuteResult? again = null;
        await ModuleWorkspace.InspectWithModulesAsync([("Other", GoodOther.Source)], program, async (provider, ran, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Completed, ran.Outcome, ran.ErrorMessage);
            _ = await ModuleWorkspace.ReloadOnlyAsync(provider, ["Other"], syntaxErrors: true, ("Program", ModuleType.StdModule, program), BadOther);
            _ = await new DefineSymbolsHandler(provider, Substitute.For<IErrorDetailsRecorder>(), NullLogger<DefineSymbolsHandler>.Instance)
                .Handle(new DefineSymbolsParams
                {
                    WorkspaceRoot = Wire.FileUri(ModuleWorkspace.Root),
                    ModuleUri = new UriBuilder(Wire.FileUri(ModuleWorkspace.Root)) { Fragment = "Other" }.Uri,
                    ModuleName = "Other",
                }, CancellationToken.None);
            again = await runAgain();
        });

        Assert.AreEqual(ExecutionOutcome.SyntaxError, again!.Outcome, again.ErrorMessage);
        StringAssert.StartsWith(again.Diagnostics![0], "Other: ", string.Join("; ", again.Diagnostics!));
    }

    // the output items written side by side are refused with the error that says so, and only with it.
    [TestMethod]
    public async Task TwoOutputItemsSideBySide_AreRefused_WithTheErrorThatSaysSo()
    {
        var result = await ModuleWorkspace.RunWithSyntaxErrorsAsync(Program("Debug.Print 1(2)"));

        Assert.AreEqual(ExecutionOutcome.SyntaxError, result.Outcome, result.ErrorMessage);
        StringAssert.StartsWith(Assert.ContainsSingle(result.Diagnostics!), RDCore.SDK.Exceptions.VBCompileError_OutputItemsNotSeparated);
    }

    // a statement that would have opened a file is not run: the file is never created, and the Close the error stood before is not needed.
    [TestMethod]
    public async Task AFileStatementAfterASyntaxError_IsNotRun_NorTheOneBeforeIt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rdcore-refused-{Guid.NewGuid():N}.txt");
        try
        {
            var result = await ModuleWorkspace.RunWithSyntaxErrorsAsync(Program(
                "Dim ff As Integer",
                "ff = FreeFile",
                $"Open \"{path}\" For Output As #ff",
                "Print #ff, Tab(2) & \"x\"",
                "Close #ff"));

            Assert.AreEqual(ExecutionOutcome.SyntaxError, result.Outcome, result.ErrorMessage);
            Assert.IsEmpty(result.Output);
            Assert.IsFalse(File.Exists(path), "the Open before the error is not run either");
        }
        finally
        {
            File.Delete(path);
        }
    }

    // the control: the same statements, with a Print # that has no error, run, and the file holds what it printed.
    [TestMethod]
    public async Task TheSameFileStatementsWithNoSyntaxError_Run()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rdcore-refused-{Guid.NewGuid():N}.txt");
        try
        {
            var result = await ModuleWorkspace.RunWithSyntaxErrorsAsync(Program(
                "Dim ff As Integer",
                "Dim s As String",
                "ff = FreeFile",
                $"Open \"{path}\" For Output As #ff",
                "Print #ff, Tab(2); 1",
                "Close #ff",
                $"Open \"{path}\" For Input As #ff",
                "Line Input #ff, s",
                "Close #ff",
                "Debug.Print \"[\" & s & \"]\""));

            Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, string.Join("; ", result.Diagnostics ?? []));
            Assert.AreEqual("[  1 ]", Assert.ContainsSingle(result.Output));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // a logical line of 25 physical lines: 24 line continuations are valid, and the 25th is the error.
    [TestMethod]
    public async Task ALineContinuedPastTheLimit_IsRefused()
    {
        var continued = string.Join(" _\r\n", Enumerable.Range(1, 26).Select(term => term == 1 ? "Debug.Print 1" : "+ 1"));

        var result = await ModuleWorkspace.RunWithSyntaxErrorsAsync(Program("Debug.Print \"before\"", continued));

        Assert.AreEqual(ExecutionOutcome.SyntaxError, result.Outcome, result.ErrorMessage);
        Assert.IsEmpty(result.Output);
        Assert.IsTrue(result.Diagnostics!.Any(error => error.Contains(RDCore.Parsing.Exceptions.VBCompileError_TooManyLineContinuations_Verbose)),
            string.Join("; ", result.Diagnostics!));
    }

    // the control: 24 line continuations are one line of the module, which runs.
    [TestMethod]
    public async Task ALineContinuedToTheLimit_Runs()
    {
        var continued = string.Join(" _\r\n", Enumerable.Range(1, 25).Select(term => term == 1 ? "Debug.Print 1" : "+ 1"));

        var result = await ModuleWorkspace.RunWithSyntaxErrorsAsync(Program(continued));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, string.Join("; ", result.Diagnostics ?? []));
        Assert.AreEqual(" 25 ", Assert.ContainsSingle(result.Output));
    }
}
