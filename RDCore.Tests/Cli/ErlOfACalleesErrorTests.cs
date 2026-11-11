using RDCore.Runtime.Compiled;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// What <c>Erl</c> reports in a handler of the caller, for an error its callee raised (<strong>RD-VBAL §6.1.2.7</strong>): the line of the call, as MS-VBA's does,
/// counted the way the environment counts lines. Run from source by the interpreter and by compiled code.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §6.1.2.7 Information")]
public sealed class ErlOfACalleesErrorTests
{
    private static async Task<string[]> RunAsync(VBExecutionEngine engine, VBErlLineNumbering numbering, string program)
    {
        if (engine is VBExecutionEngine.Compiled)
        {
            CompiledExecutionTests.RequirePayloads();
        }

        string[] output = [];
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync([], program, (_, run, _) =>
        {
            Assert.AreEqual(ExecutionOutcome.Completed, run.Outcome, run.ErrorMessage);
            output = [.. run.Output.Select(line => line.Trim())];
            return Task.CompletedTask;
        }, engine: engine, environment: profile => profile with { ErlLineNumbering = numbering });
        return output;
    }

    // the call is on line 20 of the caller, or on an unnumbered line after line 10 of it.
    private static string ProgramCalling(string call) => Program(
        "Public Sub Main()",
        "    On Error GoTo H",
        "10  Debug.Print \"a\"",
        call,
        "    Exit Sub",
        "H:",
        "    Debug.Print \"erl \" & Erl",
        "End Sub",
        "Private Sub Callee()",
        "40  Debug.Print \"b\"",
        "50  Err.Raise 5",
        "End Sub");

    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, "20  Callee", "erl 20", DisplayName = "interpreter, the call numbered")]
    [DataRow(VBExecutionEngine.Interpreter, "    Callee", "erl 10", DisplayName = "interpreter, the call unnumbered")]
    [DataRow(VBExecutionEngine.Compiled, "20  Callee", "erl 20", DisplayName = "compiled, the call numbered")]
    [DataRow(VBExecutionEngine.Compiled, "    Callee", "erl 10", DisplayName = "compiled, the call unnumbered")]
    public async Task CountingLineLabels_ErlIsTheLastLabelTheCallerPassed(VBExecutionEngine engine, string call, string erl)
        => CollectionAssert.AreEqual(new[] { "a", "b", erl }, await RunAsync(engine, VBErlLineNumbering.LineLabel, ProgramCalling(call)));

    // the call is on the fourth line of the module, counted from 1.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter)]
    [DataRow(VBExecutionEngine.Compiled)]
    public async Task CountingDocumentLines_ErlIsTheLineOfTheCall(VBExecutionEngine engine)
        => CollectionAssert.AreEqual(new[] { "a", "b", "erl 4" }, await RunAsync(engine, VBErlLineNumbering.DocumentLine, ProgramCalling("    Callee")));
}
