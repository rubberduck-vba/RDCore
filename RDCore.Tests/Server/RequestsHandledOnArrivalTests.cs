using System.IO.Pipelines;
using MediatR;
using Newtonsoft.Json.Linq;
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
/// ends could not be stopped.
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

    [HandledOnArrival]
    [Method("test/count", Direction.ClientToServer)]
    public sealed record class CountParams : IRequest, IRequest<int>
    {
        public int Count { get; init; }
    }

    [Method("test/echo", Direction.ClientToServer)]
    public sealed record class EchoParams : IRequest, IRequest<int>
    {
        public int Value { get; init; }
    }

    // a program that runs until it is paused, as a program that never ends does. OmniSharp runs a request in order, unless it is told otherwise.
    private sealed class Program
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Paused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Exception? PauseFails { get; init; }

        // the first count waits until it is let go; each is said once it is done.
        public TaskCompletionSource FirstCountLetGo { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<int> Counted { get; } = [];
    }

    private sealed class CountHandler(Program program) : RDCoreRequestHandler<CountParams, int>
    {
        protected override async Task<int> HandleAsync(CountParams request, CancellationToken token)
        {
            if (request.Count == 1)
            {
                await program.FirstCountLetGo.Task;
            }

            lock (program.Counted)
            {
                program.Counted.Add(request.Count);
            }

            return request.Count;
        }
    }

    private sealed class EchoHandler : RDCoreRequestHandler<EchoParams, int>
    {
        protected override Task<int> HandleAsync(EchoParams request, CancellationToken token) => Task.FromResult(request.Value);
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

    // what the server receives is read before OmniSharp's own try, which answers a request whose parameters can't be read: such a request is answered so, and the
    // server goes on reading - whether the request is marked or not.
    [TestMethod]
    [DataRow("test/count", "count", DisplayName = "a marked request")]
    [DataRow("test/echo", "value", DisplayName = "a request that is not marked")]
    public async Task ARequestWhoseParametersCannotBeRead_IsAnsweredAnError_AndTheServerGoesOnAnswering(string method, string field)
    {
        using var timeout = new CancellationTokenSource(Hung);
        var (server, client) = await ConnectAsync(new Program(), timeout.Token);
        using var _server = server;
        using var _client = client;

        _ = await Assert.ThrowsAsync<Exception>(
            () => client.SendRequest(method, new JObject { [field] = "not a number" }).Returning<int>(timeout.Token).WaitAsync(Hung));

        Assert.AreEqual(7, await client.SendRequest<int>(new EchoParams { Value = 7 }, timeout.Token).WaitAsync(Hung));
        Assert.AreEqual(3, await client.SendRequest<int>(new CountParams { Count = 3 }, timeout.Token).WaitAsync(Hung));
    }

    // an error break of All then None is None; a pause sent before a run is not a pause of the run.
    [TestMethod]
    public async Task RequestsHandledOnArrival_AreHandledInTheOrderTheyArrived()
    {
        using var timeout = new CancellationTokenSource(Hung);
        var program = new Program();
        var (server, client) = await ConnectAsync(program, timeout.Token);
        using var _server = server;
        using var _client = client;

        var first = client.SendRequest<int>(new CountParams { Count = 1 }, timeout.Token);
        var second = client.SendRequest<int>(new CountParams { Count = 2 }, timeout.Token);
        Assert.AreEqual(7, await client.SendRequest<int>(new EchoParams { Value = 7 }, timeout.Token).WaitAsync(Hung), "both counts were read");
        program.FirstCountLetGo.SetResult();

        Assert.AreEqual(1, await first.WaitAsync(Hung));
        Assert.AreEqual(2, await second.WaitAsync(Hung));
        CollectionAssert.AreEqual(new[] { 1, 2 }, program.Counted);
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
            .AddHandler(new CountHandler(program))
            .AddHandler(new EchoHandler())
            .WithServices(services => services
                .AddSingleton<ArrivalRequests>()
                .AddSingleton<IReceiver, RDCoreLifecycleReceiver>()), token);
        var connecting = LanguageClient.From(options => options.WithInput(toClient.Reader).WithOutput(toServer.Writer), token);
        await Task.WhenAll(serving, connecting).WaitAsync(Hung);
        return (await serving, await connecting);
    }
}
