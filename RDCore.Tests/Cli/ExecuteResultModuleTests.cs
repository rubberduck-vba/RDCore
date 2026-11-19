using RDCore.CLI.App.Repl;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Cli;

/// <summary>
/// A run that a run-time error ends says the module the error was raised in, and the module of each activation of its stack trace: a line of a module of the
/// workspace is a line of that module's file, which the shell's program does not number. The shell's own program is in <see cref="ReplProgram.ModuleName"/>
/// (<see cref="ShellErrorBreakTests"/>).
/// </summary>
[TestClass]
public sealed class ExecuteResultModuleTests
{
    private const string ProgramModule = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n";

    [TestMethod]
    public async Task AnErrorAModuleOfTheWorkspaceRaises_EndsTheRun_InThatModule()
    {
        var other = ModuleWorkspace.StandardModule("Other",
            "Public Sub Divide()", "    Dim X As Double", "    X = 1 / 0", "End Sub", "Public Sub Nested()", "    Divide", "End Sub");
        var shell = new ReplProgram { AttachedToWorkspace = true };

        var ended = await ModuleWorkspace.RunSessionModuleAsync(
            [("Other", other)], ProgramModule, shell.Module, shell.ToImmediateModuleSource("Nested"), shell.ImmediateEntryPoint);

        var lines = other.Split("\r\n");
        Assert.AreEqual((ExecutionOutcome.RuntimeError, 11), (ended.Outcome, ended.ErrorNumber), ended.ErrorMessage);
        Assert.AreEqual(("Other", Array.FindIndex(lines, line => line.Trim() == "X = 1 / 0")), (ended.ErrorModule, ended.ErrorLine));
        CollectionAssert.AreEqual(
            new[] { ("Divide", "Other"), ("Nested", "Other"), (ReplProgram.WorkspaceImmediateEntryPointName, ReplProgram.ImmediateModuleName) },
            ended.StackTrace.Select(frame => (frame.Procedure, frame.Module)).ToArray());
        Assert.AreEqual(Array.FindIndex(lines, line => line.Trim() == "Divide"), ended.StackTrace[1].Line);
    }
}
