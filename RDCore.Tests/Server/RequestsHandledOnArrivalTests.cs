using System.IO.Pipelines;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Server;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Services;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace RDCore.Tests.Server;

/// <summary>
/// A request that acts on what another request is doing is handled while it does (<see cref="HandledOnArrivalAttribute"/>). OmniSharp handles a request once those
/// before it are done, and a request that runs a program is done when the program is: a pause sent while it ran waited for it to end, and a program that never
/// ends could not be stopped (HexIDE's findings desk, <c>pause-queued-behind-run</c>).
/// </summary>
[TestClass]
public sealed class RequestsHandledOnArrivalTests
{
    private static readonly TimeSpan Hung = TimeSpan.FromMinutes(1);

    [Method("test/run", Direction.ClientToServer)]
    public sealed record class RunParams : IRequest, IRequest<string>;

    [HandledOnArrival]
    [Method("test/pause", Direction.ClientToServer)]
    public sealed record class PauseParams : IRequest, IRequest<bool>;

    [Method("test/inOrder", Direction.ClientToServer)]
    public sealed record class InOrderParams : IRequest, IRequest<int>;

    // a program that runs until it is paused, as a program that never ends does. OmniSharp runs a request in order, unless it is told otherwise.
    private sealed class Program
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Paused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Exception? PauseFails { get; init; }
    }

    private sealed class RunHandler(Program program) : RDCoreRequestHandler<RunParams, string>
    {
        protected override async Task<string> HandleAsync(RunParams request, CancellationToken token)
        {
            program.Started.TrySetResult();
            await program.Paused.Task.WaitAsync(token);
            return "suspended";
        }
    }

    private sealed class PauseHandler(Program program) : RDCoreRequestHandler<PauseParams, bool>
    {
        protected override Task<bool> HandleAsync(PauseParams request, CancellationToken token)
            => program.PauseFails is { } failure ? throw failure : Task.FromResult(program.Paused.TrySetResult());
    }

    private sealed class InOrderHandler(Program program) : RDCoreRequestHandler<InOrderParams, int>
    {
        protected override Task<int> HandleAsync(InOrderParams request, CancellationToken token) => Task.FromResult(program.Paused.Task.IsCompleted ? 1 : 0);
    }

    [TestMethod]
    public async Task ARequestHandledOnArrival_IsAnsweredWhileTheRequestBeforeItRuns()
    {
        using var timeout = new CancellationTokenSource(Hung);
        var program = new Program();
        var (server, client) = await ConnectAsync(program, timeout.Token);
        using var _server = server;
        using var _client = client;

        var running = client.SendRequest<string>(new RunParams(), timeout.Token);
        await program.Started.Task.WaitAsync(Hung);
        var inOrder = client.SendRequest<int>(new InOrderParams(), timeout.Token);

        Assert.IsTrue(await client.SendRequest<bool>(new PauseParams(), timeout.Token).WaitAsync(Hung), "the pause is handled while the run runs");
        Assert.AreEqual("suspended", await running.WaitAsync(Hung));
        Assert.AreEqual(1, await inOrder.WaitAsync(Hung), "a request that is not marked still waits for the run before it, which waited for the pause");
    }

    [TestMethod]
    public async Task ARequestHandledOnArrival_ThatFails_IsAnsweredItsError()
    {
        using var timeout = new CancellationTokenSource(Hung);
        var program = new Program { PauseFails = new InvalidOperationException("the pause failed") };
        var (server, client) = await ConnectAsync(program, timeout.Token);
        using var _server = server;
        using var _client = client;

        // as a request whose handler fails is answered when OmniSharp schedules it: an internal error, which OmniSharp's client says no more of.
        _ = await Assert.ThrowsExactlyAsync<OmniSharp.Extensions.JsonRpc.Server.InternalErrorException>(
            () => client.SendRequest<bool>(new PauseParams(), timeout.Token).WaitAsync(Hung));
    }

    // a server that receives what it is sent as an RDCore server does.
    private static async Task<(OmniSharpLanguageServer Server, LanguageClient Client)> ConnectAsync(Program program, CancellationToken token)
    {
        var toServer = new Pipe();
        var toClient = new Pipe();
        var serving = OmniSharpLanguageServer.From(options => options
            .WithInput(toServer.Reader)
            .WithOutput(toClient.Writer)
            .WithEveryRequestAnswered()
            .AddHandler(new RunHandler(program))
            .AddHandler(new PauseHandler(program))
            .AddHandler(new InOrderHandler(program))
            .WithServices(services => services
                .AddSingleton<ArrivalRequests>()
                .AddSingleton<IReceiver, RDCoreLifecycleReceiver>()), token);
        var connecting = LanguageClient.From(options => options.WithInput(toClient.Reader).WithOutput(toServer.Writer), token);
        await Task.WhenAll(serving, connecting).WaitAsync(Hung);
        return (await serving, await connecting);
    }
}
