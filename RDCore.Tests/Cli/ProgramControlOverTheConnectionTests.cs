using System.IO.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Client;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Services;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace RDCore.Tests.Cli;

/// <summary>
/// A program that runs under a debugger is paused and ended over the host's connection while the request that runs it runs. Each request waited for the one
/// before it, and the request that runs a program is done when the program is: a pause or an end sent to a program that never ends waited forever (HexIDE's
/// findings desk, <c>pause-queued-behind-run</c>).
/// </summary>
[TestClass]
public sealed class ProgramControlOverTheConnectionTests
{
    private static readonly (string Name, string Source)[] NoClasses = [];

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static readonly string Looping = string.Join("\r\n",
        "Public Total As Long",
        "Public Sub Main()",
        "    Do While True",
        "        Total = Total + 1",
        "    Loop",
        "End Sub") + "\r\n";

    [TestMethod]
    public async Task APause_SentWhileAResumedProgramRuns_StopsIt_AndTheResumeAnswersSuspended()
        => await ConnectedAsync(async (provider, client) =>
        {
            var running = client.SendRequest<ExecuteSessionResult>(new HostDebugResumeParams(), CancellationToken.None);
            await WaitForAsync(() => provider.Execution.State is ProgramState.Running);

            var ack = await client.SendRequest<HostDebugAck>(new HostDebugPauseParams(), CancellationToken.None).WaitAsync(Patience);
            var paused = await running.WaitAsync(Patience);

            Assert.IsTrue(ack.Acted);
            Assert.AreEqual(ExecutionOutcome.Suspended, paused.Outcome, paused.ErrorMessage);
            Assert.IsTrue((await client.SendRequest<HostDebugAck>(new HostDebugTerminateParams(), CancellationToken.None).WaitAsync(Patience)).Acted);
        });

    [TestMethod]
    public async Task ATerminate_SentWhileAResumedProgramRuns_EndsIt()
        => await ConnectedAsync(async (provider, client) =>
        {
            var running = client.SendRequest<ExecuteSessionResult>(new HostDebugResumeParams(), CancellationToken.None);
            await WaitForAsync(() => provider.Execution.State is ProgramState.Running);

            var ack = await client.SendRequest<HostDebugAck>(new HostDebugTerminateParams(), CancellationToken.None).WaitAsync(Patience);

            Assert.IsTrue(ack.Acted);
            Assert.AreEqual(ExecutionOutcome.Halted, (await running.WaitAsync(Patience)).Outcome);
            Assert.AreEqual(ProgramState.Idle, provider.Execution.State);
        });

    // the looping program, broken into as it starts so that it waits, and the host's program requests served over a connection as the host serves them.
    private static Task ConnectedAsync(Func<EnvironmentSessionProvider, LanguageClient, Task> test)
        => ModuleWorkspace.InspectAsync(NoClasses, Looping, async (provider, first, _) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, "the cancellation of the request is a break");

            using var timeout = new CancellationTokenSource(Patience);
            var toServer = new Pipe();
            var toClient = new Pipe();
            var serving = OmniSharpLanguageServer.From(options => options
                .WithInput(toServer.Reader)
                .WithOutput(toClient.Writer)
                .WithEveryRequestAnswered()
                .AddHandler(new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance))
                .AddHandler(new HostDebugPauseHandler(provider))
                .AddHandler(new HostDebugTerminateHandler(provider))
                .WithServices(services => services
                    .AddSingleton<ArrivalRequests>()
                    .AddSingleton<IReceiver, RDCoreLifecycleReceiver>()), timeout.Token);
            var connecting = LanguageClient.From(options => options.WithInput(toClient.Reader).WithOutput(toServer.Writer), timeout.Token);
            await Task.WhenAll(serving, connecting).WaitAsync(Patience);
            using var server = await serving;
            using var client = await connecting;

            await test(provider, client);
        }, cancelWhen: provider => provider.Execution.State is ProgramState.Running, debug: true);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            Assert.IsLessThan(deadline, DateTime.UtcNow, "timed out");
            await Task.Delay(10);
        }
    }
}
