using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Server;
using RDCore.SDK;
using RDCore.SDK.Client;
using RDCore.SDK.Client.Connection;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Serialization;
using System.IO.Pipelines;
using System.IO.Pipes;
using PipeOptions = System.IO.Pipes.PipeOptions;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace RDCore.Tests.Client;

/// <summary>
/// A child process that exits while a request to it is in flight: the request fails, the connection restarts, and the next request is answered by the next
/// process. A request is not sent again, and none waits for an answer that cannot come.
/// </summary>
[TestClass]
public sealed class ChildConnectionRestartTests
{
    private const string Held = "test/held";

    [Method(Held)]
    public sealed record class HeldParams : IRequest<HeldResult>;

    public sealed record class HeldResult
    {
        public int Incarnation { get; init; }
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ARequestInFlight_WhenTheChildExits_Fails_AndTheRestartedChildAnswersTheNext()
    {
        using var child = new InProcessChild();
        using var connection = await child.ConnectAsync();

        var inFlight = connection.SendRequestAsync<HeldParams, HeldResult>(new HeldParams(), CancellationToken.None);
        await child.FirstRequestReceived;
        child.Crash();

        var lost = await Assert.ThrowsExactlyAsync<ServerProtocolSdkException>(() => inFlight);
        StringAssert.Contains(lost.Message, Held);

        await connection.WaitForReadyAsync(CancellationToken.None);
        var next = await connection.SendRequestAsync<HeldParams, HeldResult>(new HeldParams(), CancellationToken.None);
        Assert.AreEqual(2, next.Incarnation);
        Assert.AreEqual(1, child.RequestsReceivedBy(1), "the request in flight is not sent again to the next process");
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ARequestInFlightOnTheChannel_WhenTheChildExits_Fails_AndTheRestartedChildAnswersTheNext()
    {
        using var child = new InProcessChild();
        using var connection = await child.ConnectAsync();

        var inFlight = connection.Channel.SendRequestAsync(Held, null, CancellationToken.None);
        await child.FirstRequestReceived;
        child.Crash();

        var lost = await Assert.ThrowsExactlyAsync<ServerProtocolSdkException>(() => inFlight);
        StringAssert.Contains(lost.Message, Held);

        await connection.WaitForReadyAsync(CancellationToken.None);
        var next = await connection.Channel.SendRequestAsync(Held, null, CancellationToken.None);
        Assert.AreEqual(2, next!.Value.GetProperty("incarnation").GetInt32());
        Assert.AreEqual(1, child.RequestsReceivedBy(1), "the request in flight is not sent again to the next process");
    }

    // what ChildConnection starts: each start is a process of its own, an OmniSharp server on the pipe, which answers the platform handshake and holds the
    // first request it is sent until it exits; the processes after it answer at once, with their number.
    private sealed class InProcessChild : IRDCoreServerProcess
    {
        private readonly object _gate = new();
        private readonly TaskCompletionSource _firstRequestReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Dictionary<int, int> _requests = [];
        private TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private NamedPipeServerStream? _pipe;
        private OmniSharpLanguageServer? _server;
        private int _incarnation;

        public Task FirstRequestReceived => _firstRequestReceived.Task;

        public int ProcessId => _incarnation;

        public bool HasExited { get; private set; } = true;

        public int ExitCode { get; private set; }

        public int RequestsReceivedBy(int incarnation)
        {
            lock (_gate)
            {
                return _requests.GetValueOrDefault(incarnation);
            }
        }

        public async Task<ChildConnection> ConnectAsync()
        {
            var connection = new ChildConnection(this, new RDCorePlatformDefaultTransportLayer(Options.Create(new SdkAppOptions())), NullLogger<ChildConnection>.Instance);
            await connection.ConnectAsync(new ChildConnectionRequest
            {
                ServerExecutablePath = "child.exe",
                PipeName = $"RDCore.Tests.ChildConnectionRestart.{Guid.NewGuid():N}",
                ExpectedComponent = CoreServerComponent.ParsingServer,
                ConfigureClient = _ => { },
                OnPeerExited = () => { },
                MaxRestartAttempts = 1,
                RestartBackoffBaseMs = 1,
            }, CancellationToken.None);
            return connection;
        }

        public Task StartAsync(string relativePath, string pipeName, CancellationTokenSource tokenSource, string? mode = null)
        {
            var incarnation = ++_incarnation;
            _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
            HasExited = false;
            ExitCode = 0;
            _pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
            _ = ServeAsync(_pipe, incarnation);
            return Task.CompletedTask;
        }

        private async Task ServeAsync(NamedPipeServerStream pipe, int incarnation)
        {
            await pipe.WaitForConnectionAsync();
            _server = await OmniSharpLanguageServer.From(server => server
                .WithInput(PipeReader.Create(pipe))
                .WithOutput(PipeWriter.Create(pipe))
                .WithSerializer(new PullDiagnosticsLspSerializer())
                .OnJsonRequest(RDCorePlatformProtocol.PlatformInitialize, (JToken _, CancellationToken _) =>
                    Task.FromResult<JToken>(JObject.FromObject(new { component = (int)CoreServerComponent.ParsingServer, provided = Array.Empty<string>() })))
                .OnRequest<HeldParams, HeldResult>(Held, (_, _) =>
                {
                    lock (_gate)
                    {
                        _requests[incarnation] = _requests.GetValueOrDefault(incarnation) + 1;
                    }

                    if (incarnation == 1)
                    {
                        _firstRequestReceived.TrySetResult();
                        return new TaskCompletionSource<HeldResult>().Task;
                    }

                    return Task.FromResult(new HeldResult { Incarnation = incarnation });
                }));
        }

        // the process dies as a process does: its end of the pipe closes, and it exits with a code that is not 0.
        public void Crash()
        {
            _server?.Dispose();
            _pipe?.Dispose();
            ExitCode = 1;
            HasExited = true;
            _exit.TrySetResult();
        }

        public Task WaitForExitAsync() => _exit.Task;

        public void Shutdown()
        {
        }

        public void Dispose()
        {
            _server?.Dispose();
            _pipe?.Dispose();
            HasExited = true;
            _exit.TrySetResult();
        }
    }
}
