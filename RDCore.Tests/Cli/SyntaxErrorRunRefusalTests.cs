using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Cli;

/// <summary>
/// A module with a syntax error does not run (<strong>RD-VBAL §2.6.1</strong>): a run of it is refused with its errors before any of it runs, whatever the error is
/// - a statement the parser stopped in, a statement no rule reads, two output items written side by side (<c>VBC00061</c>), a line continued past the limit. The
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

    private static string Module(string name, params string[] lines) => string.Join("\r\n", [$"Attribute VB_Name = \"{name}\"", .. lines, ""]);

    // a syntax error anywhere in the project refuses every run, with the module it is in named before it: nothing of the project runs while a module of it
    // does not load. The rows are the ones MS-VBA was measured with (Excel, 2026-11-07), and say what it does with Compile On Demand on, then off. MS-VBA
    // compiles what a run needs - with Compile On Demand on, the procedures the run reaches and every module's declarations section; off, every module the
    // run uses - so it runs some of what is refused here: O1 in both modes.
    [TestMethod]
    [DataRow("P", "", DisplayName = "P1: another procedure of the module run (MS-VBA: runs; refused)")]
    [DataRow("B", "", DisplayName = "O1: a procedure of another module, not called (MS-VBA: runs; runs)")]
    [DataRow("B", "Good", DisplayName = "O2: a procedure of another module, another procedure of it called (MS-VBA: runs; refused)")]
    [DataRow("B", "Bad", DisplayName = "O3: a procedure of another module, called (MS-VBA: the syntax error at the call; refused)")]
    [DataRow("D", "", DisplayName = "O4: the declarations section of another module (MS-VBA: refused; refused)")]
    [DataRow("D", "Good", DisplayName = "O5: the declarations section of another module, a procedure of it called (MS-VBA: refused; refused)")]
    public async Task ASyntaxErrorAnywhereInTheProject_RefusesTheRun_WithTheModuleItIsIn(string where, string call)
    {
        var (program, other) = ProgramAndOther(where, call);

        var result = await ModuleWorkspace.RunWithSyntaxErrorsAsync(program, [("Other", other)]);

        Assert.AreEqual(ExecutionOutcome.SyntaxError, result.Outcome, result.ErrorMessage);
        Assert.IsEmpty(result.Output);
        var prefix = where == "P" ? "" : "Other: ";
        StringAssert.StartsWith(result.Diagnostics[0], prefix + RDCore.SDK.Exceptions.VBSyntaxError_DefaultMessage, string.Join("; ", result.Diagnostics));
    }

    // O5: the procedure called is not found, its module's declarations not being read; the syntax error that is why is said first.
    [TestMethod]
    public async Task AMemberOfAModuleWithASyntaxError_IsSaidNotFound_AfterTheSyntaxError()
    {
        var (program, other) = ProgramAndOther("D", "Good");

        var result = await ModuleWorkspace.RunWithSyntaxErrorsAsync(program, [("Other", other)]);

        Assert.AreEqual(ExecutionOutcome.SyntaxError, result.Outcome, result.ErrorMessage);
        Assert.IsTrue(result.Diagnostics.Count >= 2, string.Join("; ", result.Diagnostics));
        Assert.IsTrue(result.Diagnostics.Take(result.Diagnostics.Count - 1).All(error => error.StartsWith("Other: ", StringComparison.Ordinal)),
            string.Join("; ", result.Diagnostics));
        StringAssert.Contains(result.Diagnostics[^1], "Good");
    }

    // the controls, C0 and C1: the same modules with no syntax error run (MS-VBA: runs; runs).
    [TestMethod]
    [DataRow("", new[] { "before", "after" }, DisplayName = "C0: nothing called")]
    [DataRow("Good", new[] { "before", "good", "after" }, DisplayName = "C1: a procedure of the other module called")]
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
        Assert.IsTrue(result.Diagnostics!.Any(error => error.Contains(RDCore.SDK.Exceptions.VBCompileError_TooManyLineContinuations_Verbose)),
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
