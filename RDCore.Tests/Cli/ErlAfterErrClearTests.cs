using RDCore.Runtime.Compiled;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// What <c>Err.Clear</c> does to <c>Erl</c> (<strong>RD-VBAL §6.1.2.7.1.14</strong>), run from source by the interpreter and by compiled code. The programs
/// and their <c>Kept</c> output are MS-VBA's, as Excel (64-bit, en-US) runs them; <c>Erl</c> counts line-number labels, as MS-VBA's does.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §6.1.2.7 Information")]
public sealed class ErlAfterErrClearTests
{
    private static async Task<string[]> RunAsync(VBExecutionEngine engine, VBErlAfterErrClear afterErrClear, params string[] case1)
    {
        if (engine is VBExecutionEngine.Compiled)
        {
            CompiledExecutionTests.RequirePayloads();
        }

        string[] output = [];
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync([], Program(["Public Sub Main()", "    Case1", "End Sub", .. case1]), (_, run, _) =>
        {
            Assert.AreEqual(ExecutionOutcome.Completed, run.Outcome, run.ErrorMessage);
            output = [.. run.Output.Select(line => line.Trim())];
            return Task.CompletedTask;
        }, engine: engine, environment: profile => profile with { ErlLineNumbering = VBErlLineNumbering.LineLabel, ErlAfterErrClear = afterErrClear });
        return output;
    }

    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, VBErlAfterErrClear.Reset, "cleared 0 0")]
    [DataRow(VBExecutionEngine.Interpreter, VBErlAfterErrClear.Kept, "cleared 20 0")]
    [DataRow(VBExecutionEngine.Compiled, VBErlAfterErrClear.Reset, "cleared 0 0")]
    [DataRow(VBExecutionEngine.Compiled, VBErlAfterErrClear.Kept, "cleared 20 0")]
    public async Task ErrClear_InAHandler_ResetsErl_UnlessTheEnvironmentKeepsIt(VBExecutionEngine engine, VBErlAfterErrClear afterErrClear, string cleared)
    {
        var output = await RunAsync(engine, afterErrClear,
            "Private Sub Case1()",
            "    Dim x As Variant",
            "    On Error GoTo H",
            "10  Debug.Print \"a\"",
            "20  x = 1 / 0",
            "    Exit Sub",
            "H:",
            "    Debug.Print \"erl \" & Erl",
            "    Err.Clear",
            "    Debug.Print \"cleared \" & Erl & \" \" & Err.Number",
            "End Sub");

        CollectionAssert.AreEqual(new[] { "a", "erl 20", cleared }, output);
    }

    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, VBErlAfterErrClear.Reset, "cleared 0")]
    [DataRow(VBExecutionEngine.Interpreter, VBErlAfterErrClear.Kept, "cleared 20")]
    [DataRow(VBExecutionEngine.Compiled, VBErlAfterErrClear.Reset, "cleared 0")]
    [DataRow(VBExecutionEngine.Compiled, VBErlAfterErrClear.Kept, "cleared 20")]
    public async Task ResumeNext_AfterErrClear_ResetsErl_EitherWay(VBExecutionEngine engine, VBErlAfterErrClear afterErrClear, string cleared)
    {
        var output = await RunAsync(engine, afterErrClear,
            "Private Sub Case1()",
            "    Dim x As Variant",
            "    On Error GoTo H",
            "20  x = 1 / 0",
            "    Debug.Print \"resumed next \" & Erl & \" \" & Err.Number",
            "    Exit Sub",
            "H:",
            "    Err.Clear",
            "    Debug.Print \"cleared \" & Erl",
            "    Resume Next",
            "End Sub");

        CollectionAssert.AreEqual(new[] { cleared, "resumed next 0 0" }, output);
    }

    // under On Error Resume Next, the next error sets Erl again: to the last line-number label, 20, as the line it is on has none.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, VBErlAfterErrClear.Reset, "cleared 0")]
    [DataRow(VBExecutionEngine.Interpreter, VBErlAfterErrClear.Kept, "cleared 20")]
    [DataRow(VBExecutionEngine.Compiled, VBErlAfterErrClear.Reset, "cleared 0")]
    [DataRow(VBExecutionEngine.Compiled, VBErlAfterErrClear.Kept, "cleared 20")]
    public async Task ErrClear_UnderResumeNext_ResetsErl_UnlessTheEnvironmentKeepsIt(VBExecutionEngine engine, VBErlAfterErrClear afterErrClear, string cleared)
    {
        var output = await RunAsync(engine, afterErrClear,
            "Private Sub Case1()",
            "    Dim x As Variant",
            "    On Error Resume Next",
            "20  x = 1 / 0",
            "    Err.Clear",
            "    Debug.Print \"cleared \" & Erl",
            "    x = 1 / 0",
            "    Debug.Print \"second \" & Erl & \" \" & Err.Number",
            "End Sub");

        CollectionAssert.AreEqual(new[] { cleared, "second 20 11" }, output);
    }

    // leaving a procedure under On Error Resume Next keeps the error, and Erl with it, for the caller to see: by its end, or by Exit Sub. Err.Clear in it
    // clears the error, and Erl as the environment says.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, VBErlAfterErrClear.Reset, "", "inner 20 11|after 20 11")]
    [DataRow(VBExecutionEngine.Interpreter, VBErlAfterErrClear.Kept, "", "inner 20 11|after 20 11")]
    [DataRow(VBExecutionEngine.Interpreter, VBErlAfterErrClear.Reset, "Err.Clear", "inner 0 0|after 0 0")]
    [DataRow(VBExecutionEngine.Interpreter, VBErlAfterErrClear.Kept, "Err.Clear", "inner 20 0|after 20 0")]
    [DataRow(VBExecutionEngine.Interpreter, VBErlAfterErrClear.Reset, "Exit Sub", "after 20 11")]
    [DataRow(VBExecutionEngine.Interpreter, VBErlAfterErrClear.Kept, "Exit Sub", "after 20 11")]
    [DataRow(VBExecutionEngine.Compiled, VBErlAfterErrClear.Reset, "", "inner 20 11|after 20 11")]
    [DataRow(VBExecutionEngine.Compiled, VBErlAfterErrClear.Kept, "", "inner 20 11|after 20 11")]
    [DataRow(VBExecutionEngine.Compiled, VBErlAfterErrClear.Reset, "Err.Clear", "inner 0 0|after 0 0")]
    [DataRow(VBExecutionEngine.Compiled, VBErlAfterErrClear.Kept, "Err.Clear", "inner 20 0|after 20 0")]
    [DataRow(VBExecutionEngine.Compiled, VBErlAfterErrClear.Reset, "Exit Sub", "after 20 11")]
    [DataRow(VBExecutionEngine.Compiled, VBErlAfterErrClear.Kept, "Exit Sub", "after 20 11")]
    public async Task LeavingAProcedureUnderResumeNext_KeepsTheErrorAndErl_ForTheCaller(VBExecutionEngine engine, VBErlAfterErrClear afterErrClear, string then, string expected)
    {
        var output = await RunAsync(engine, afterErrClear,
            "Private Sub Case1()",
            "    Inner",
            "    Debug.Print \"after \" & Erl & \" \" & Err.Number",
            "End Sub",
            "Private Sub Inner()",
            "    Dim x As Variant",
            "    On Error Resume Next",
            "20  x = 1 / 0",
            then.Length == 0 ? "" : "    " + then,
            "    Debug.Print \"inner \" & Erl & \" \" & Err.Number",
            "End Sub");

        CollectionAssert.AreEqual(expected.Split('|'), output);
    }

    // a Resume resets Erl whatever the environment says of Err.Clear: each form, with the error not cleared first.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, "Resume Next", "after 0 0")]
    [DataRow(VBExecutionEngine.Interpreter, "Resume Done", "done 0 0")]
    [DataRow(VBExecutionEngine.Compiled, "Resume Next", "after 0 0")]
    [DataRow(VBExecutionEngine.Compiled, "Resume Done", "done 0 0")]
    public async Task AResume_ResetsErl_WhenTheEnvironmentKeepsItAfterErrClear(VBExecutionEngine engine, string resume, string expected)
    {
        var output = await RunAsync(engine, VBErlAfterErrClear.Kept,
            "Private Sub Case1()",
            "    Dim x As Variant",
            "    On Error GoTo H",
            "20  x = 1 / 0",
            "    Debug.Print \"after \" & Erl & \" \" & Err.Number",
            "    Exit Sub",
            "H:",
            "    Debug.Print \"erl \" & Erl",
            $"    {resume}",
            "Done:",
            "    Debug.Print \"done \" & Erl & \" \" & Err.Number",
            "End Sub");

        Assert.AreEqual("erl 20", output[0]);
        Assert.AreEqual(expected, output[1]);
    }
}
