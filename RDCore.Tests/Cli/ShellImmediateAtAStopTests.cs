using Microsoft.Extensions.Logging.Abstractions;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Cli;

/// <summary>
/// A statement typed at the prompt while a program waits is run alongside it, and the program can be gone on with afterwards - the shell types the program again with
/// the statement as a second procedure, which is not a change to the program.
/// </summary>
[TestClass]
public sealed class ShellImmediateAtAStopTests
{
    private static readonly (int Number, string Statement)[] Program = [(10, "X = 1"), (20, "STOP"), (30, "X = X + 1"), (40, "PRINT X")];

    [TestMethod]
    public async Task AStatement_TypedAtAStop_ChangesTheVariables_AndTheProgramGoesOnWithThem()
    {
        var shell = ShellHost.Compose();
        var stopped = await shell.RunAsync(Program, debug: true);
        Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);

        var typed = await shell.ImmediateAsync(Program, "X = 5", alongside: true);
        Assert.AreEqual(ExecutionOutcome.Completed, typed.Outcome, typed.ErrorMessage);

        var resumed = await new HostDebugResumeHandler(shell.Provider, NullLogger<HostDebugResumeHandler>.Instance).Handle(new HostDebugResumeParams(), CancellationToken.None);

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "6" }, resumed.Output.Select(line => line.Trim()).ToArray());
    }
}
