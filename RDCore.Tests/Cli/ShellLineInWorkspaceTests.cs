using RDCore.CLI.App.Repl;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Cli;

/// <summary>
/// A line a shell attached to a workspace runs, as the host runs it: in a module of the client's own named <see cref="ReplProgram.ImmediateModuleName"/>,
/// which no code can name, beside the project's modules. A name the line uses is the project's, its <c>Program</c> module's included, and one the project does
/// not declare is not found.
/// </summary>
[TestClass]
public sealed class ShellLineInWorkspaceTests
{
    private const string Program =
        "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n    Debug.Print \"in Main\"\r\nEnd Sub\r\n";

    private static readonly (string Name, string Source)[] Other =
        [("Other", ModuleWorkspace.StandardModule("Other",
            "Public Sub Hello()", "    Debug.Print \"in Hello\"", "End Sub",
            "Public Sub Immediate()", "    Debug.Print \"in Immediate\"", "End Sub"))];

    private static Task<ExecuteResult> RunAsync(string line, string program = Program)
    {
        var shell = new ReplProgram { AttachedToWorkspace = true };
        return ModuleWorkspace.RunSessionModuleAsync(Other, program, shell.Module, shell.ToImmediateModuleSource(line), shell.ImmediateEntryPoint);
    }

    [TestMethod]
    [DataRow("Main", "in Main", DisplayName = "the workspace's Main")]
    [DataRow("Program.Main", "in Main", DisplayName = "the workspace's Main, qualified by its module")]
    [DataRow("Hello", "in Hello", DisplayName = "a procedure of another module")]
    [DataRow("Other.Hello", "in Hello", DisplayName = "a procedure of another module, qualified")]
    [DataRow("Immediate", "in Immediate", DisplayName = "a procedure of another module named Immediate")]
    public async Task ALine_CallsTheWorkspacesProcedure(string line, string printed)
    {
        var result = await RunAsync(line);

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { printed }, result.Output.Select(each => each.Trim()).ToArray());
        Assert.IsFalse(result.Diagnostics?.Any() ?? false, string.Join("; ", result.Diagnostics ?? []));
    }

    [TestMethod]
    public async Task ALine_InAWorkspaceWithNoProgramModule_Runs()
    {
        var result = await RunAsync("Debug.Print 1 + 1", program: "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n");

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "2" }, result.Output.Select(each => each.Trim()).ToArray());
    }

    [TestMethod]
    public async Task ALine_ThatCallsWhatTheWorkspaceDoesNotDeclare_DoesNotComplete()
    {
        var result = await RunAsync("Nowhere");

        Assert.AreNotEqual(ExecutionOutcome.Completed, result.Outcome);
        Assert.IsFalse(result.Output.Any(), string.Join(" | ", result.Output));
    }
}
