using Microsoft.Extensions.Logging.Abstractions;
using RDCore.CLI.App.Repl;
using RDCore.CLI.Host.Handlers;
using RDCore.Runtime.Support.Host;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Cli;

/// <summary>
/// A line typed at the prompt runs under the debugger, as the VBE's Immediate window runs one: what it calls stops at a <c>Stop</c> and waits there, its stack is the
/// procedure that stopped over the line's own procedure, the variables of the procedure that stopped are read, and the program goes on to its end. With a workspace
/// and without one.
/// </summary>
[TestClass]
public sealed class ShellLineDebuggingTests
{
    private const string Program =
        "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n    Dim n As Long\r\n    n = 42\r\n    Stop\r\n    Debug.Print n\r\nEnd Sub\r\n";

    private static readonly (int Number, string Statement)[] Numbered = [(10, "N = 42"), (20, "STOP"), (30, "PRINT N")];

    [TestMethod]
    public async Task ALineInAWorkspace_ThatReachesAStop_WaitsThere_UnderTheLinesOwnProcedure_AndGoesOnToTheEnd()
    {
        var shell = new ReplProgram { AttachedToWorkspace = true };
        DebugStackResult? stack = null;
        DebugVariablesResult? locals = null;
        ExecuteResult? resumed = null;

        var stopped = await ModuleWorkspace.RunSessionModuleAsync([], Program, shell.Module, shell.ToImmediateModuleSource("Main"), shell.ImmediateEntryPoint,
            debug: true, afterRun: async (result, provider) =>
            {
                if (result.Outcome is not ExecutionOutcome.Suspended)
                {
                    return;
                }

                (stack, locals, resumed) = await LookAndGoOnAsync(provider);
            });

        Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);
        CollectionAssert.AreEqual(
            new[] { ("Main", "Program"), (ReplProgram.WorkspaceImmediateEntryPointName, ReplProgram.ImmediateModuleName) },
            stack!.Frames.Select(frame => (frame.Procedure, frame.Module)).ToArray());
        Assert.AreEqual("42", locals!.Variables.Single(variable => variable.Name == "n").Value);
        Assert.AreEqual(ExecutionOutcome.Completed, resumed!.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "42" }, resumed.Output.Select(line => line.Trim()).ToArray());
    }

    [TestMethod]
    public async Task ALineWithoutAWorkspace_ThatReachesAStop_WaitsThere_UnderTheLinesOwnProcedure_AndGoesOnToTheEnd()
    {
        var shell = ShellHost.Compose();

        var stopped = await shell.ImmediateAsync(Numbered, ReplProgram.EntryPointName, debug: true);
        Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);
        // a variable a line of the program assigns, with no Dim, is the module's.
        var (stack, locals, resumed) = await LookAndGoOnAsync(shell.Provider, DebugVariableScope.Module);

        CollectionAssert.AreEqual(
            new[] { (ReplProgram.EntryPointName, ReplProgram.ModuleName), (ReplProgram.ImmediateEntryPointName, ReplProgram.ModuleName) },
            stack.Frames.Select(frame => (frame.Procedure, frame.Module)).ToArray());
        Assert.AreEqual("42", locals.Variables.Single(variable => string.Equals(variable.Name, "N", StringComparison.OrdinalIgnoreCase)).Value);
        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "42" }, resumed.Output.Select(line => line.Trim()).ToArray());
    }

    // as STACK, VARS and CONT ask the host for them.
    private static async Task<(DebugStackResult Stack, DebugVariablesResult Variables, ExecuteResult Resumed)> LookAndGoOnAsync(
        EnvironmentSessionProvider provider, DebugVariableScope scope = DebugVariableScope.Locals)
    {
        var stack = await new HostDebugStackHandler(provider).Handle(new HostDebugStackParams(), CancellationToken.None);
        var locals = await new HostDebugVariablesHandler(provider)
            .Handle(new HostDebugVariablesParams { FrameId = 0, Scope = scope }, CancellationToken.None);
        var resumed = await new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance).Handle(new HostDebugResumeParams(), CancellationToken.None);
        return (stack, locals, resumed);
    }
}
