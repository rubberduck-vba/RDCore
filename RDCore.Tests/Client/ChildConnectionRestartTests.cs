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

    // not a timing: a request that is never answered fails the test here instead of hanging the run.
    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(30);

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
        using var hang = new CancellationTokenSource(HangGuard);

        var inFlight = connection.SendRequestAsync<HeldParams, HeldResult>(new HeldParams(), CancellationToken.None);
        await child.FirstRequestReceived;
        child.Crash();

        var lost = await Assert.ThrowsExactlyAsync<ServerProtocolSdkException>(() => inFlight.WaitAsync(hang.Token));
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
        using var hang = new CancellationTokenSource(HangGuard);

        var inFlight = connection.Channel.SendRequestAsync(Held, null, CancellationToken.None);
        await child.FirstRequestReceived;
        child.Crash();

        var lost = await Assert.ThrowsExactlyAsync<ServerProtocolSdkException>(() => inFlight.WaitAsync(hang.Token));
        StringAssert.Contains(lost.Message, Held);

        await connection.WaitForReadyAsync(CancellationToken.None);
        var next = await connection.Channel.SendRequestAsync(Held, null, CancellationToken.None);
        Assert.AreEqual(2, next!.Value.GetProperty("incarnation").GetInt32());
        Assert.AreEqual(1, child.RequestsReceivedBy(1), "the request in flight is not sent again to the next process");
    }

    // the connection is looked at whenever it calls out, on its own thread, between the exit and the next process being ready: when it reads the exit code,
    // starts the next process, and changes state. Each look is where the connection is at that moment, whatever the other threads do.
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AfterTheChildExits_TheConnectionIsNotReady_UntilTheNextProcessIs()
    {
        using var child = new InProcessChild();
        using var connection = await child.ConnectAsync();
        using var hang = new CancellationTokenSource(HangGuard);
        var readyAt = new List<string>();
        var looks = 0;
        var restarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Look(string where)
        {
            looks++;
            if (connection.WaitForReadyAsync(CancellationToken.None).IsCompletedSuccessfully)
            {
                readyAt.Add(where);
            }
        }

        child.Looking = Look;
        connection.StateChanged += state =>
        {
            if (state.Value is ConnectionStateValue.Ready)
            {
                restarted.TrySetResult();
            }
            else
            {
                Look($"state {state.Value}");
            }
        };
        child.Crash();

        await restarted.Task.WaitAsync(hang.Token);
        CollectionAssert.AreEqual(Array.Empty<string>(), readyAt, $"ready while the child that exited was the one a request would go to: at {string.Join(", ", readyAt)}");
        Assert.IsGreaterThanOrEqualTo(6, looks, "the exit code read, the start of the next process, and Faulted, Spawning, Connecting, Initializing");
        Assert.IsTrue(connection.WaitForReadyAsync(CancellationToken.None).IsCompletedSuccessfully);
    }

    // disposed while it waits for the next process, after the monitor has seen the exit: the monitor is held where it reads the exit code, which it does
    // once it has settled the loss, so the wait is taken on the gate of the restart, and the dispose comes after it.
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AConnectionDisposed_WhileAWaitWaitsForTheNextProcess_FaultsTheWait()
    {
        using var child = new InProcessChild();
        using var connection = await child.ConnectAsync();
        using var hang = new CancellationTokenSource(HangGuard);
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        child.Looking = where =>
        {
            if (where == "exit code" && settled.TrySetResult())
            {
                proceed.Task.Wait();
            }
        };

        try
        {
            child.Crash();
            await settled.Task.WaitAsync(hang.Token);
            var waiting = connection.WaitForReadyAsync(CancellationToken.None);
            Assert.IsFalse(waiting.IsCompleted, "the process that exited is not ready, and the next one is not started");

            connection.Dispose();

            await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => waiting.WaitAsync(hang.Token));
            await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => connection.WaitForReadyAsync(CancellationToken.None).WaitAsync(hang.Token));
        }
        finally
        {
            proceed.TrySetResult();
        }
    }

    // disposed while it is ready, before the monitor sees the exit the dispose makes: the request in flight fails once the monitor has settled the loss,
    // and a wait after it is not given the gate of a restart that never comes.
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AConnectionDisposed_WhileItIsReady_FaultsAWaitAfterTheExitIsSeen()
    {
        using var child = new InProcessChild();
        using var connection = await child.ConnectAsync();
        using var hang = new CancellationTokenSource(HangGuard);
        var inFlight = connection.SendRequestAsync<HeldParams, HeldResult>(new HeldParams(), CancellationToken.None);
        await child.FirstRequestReceived;

        connection.Dispose();

        await Assert.ThrowsExactlyAsync<ServerProtocolSdkException>(() => inFlight.WaitAsync(hang.Token));
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => connection.WaitForReadyAsync(CancellationToken.None).WaitAsync(hang.Token));
    }

    // the next process exits while it is initialized: the platform handshake is held, a request is sent to it meanwhile, and it crashes. Nothing it was
    // asked is answered, so the attempt fails, the request fails with it, and with no restart left the wait is told the connection has exited.
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task TheNextProcess_ExitingBeforeItIsInitialized_FailsTheRestart_AndWhatWasSentToIt()
    {
        using var child = new InProcessChild { HoldInitializeOf = 2 };
        using var connection = await child.ConnectAsync();
        using var hang = new CancellationTokenSource(HangGuard);

        child.Crash();
        await child.InitializeHeld.WaitAsync(hang.Token);
        var sentMeanwhile = connection.SendRequestAsync<HeldParams, HeldResult>(new HeldParams(), CancellationToken.None);
        child.Crash();

        var lost = await Assert.ThrowsExactlyAsync<ServerProtocolSdkException>(() => sentMeanwhile.WaitAsync(hang.Token));
        StringAssert.Contains(lost.Message, Held);
        await Assert.ThrowsExactlyAsync<ServerProtocolSdkException>(() => connection.WaitForReadyAsync(CancellationToken.None).WaitAsync(hang.Token));
        Assert.AreEqual(ConnectionStateValue.Exited, connection.State.Value);
    }

    // what ChildConnection starts: each start is a process of its own, an OmniSharp server on the pipe, which answers the platform handshake and holds the
    // first request it is sent until it exits; the processes after it answer at once, with their number. The process HoldInitializeOf names holds the
    // platform handshake, and every request, instead.
    private sealed class InProcessChild : IRDCoreServerProcess
    {
        private readonly object _gate = new();
        private readonly TaskCompletionSource _firstRequestReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _initializeHeld = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Dictionary<int, int> _requests = [];
        private TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private NamedPipeServerStream? _pipe;
        private OmniSharpLanguageServer? _server;
        private int _incarnation;
        // the last process that is down: its server, made after it went down, is disposed as it is made.
        private int _downTo;

        private int _exitCode;
        private bool _crashed;

        public Task FirstRequestReceived => _firstRequestReceived.Task;

        public int HoldInitializeOf { get; init; }

        public Task InitializeHeld => _initializeHeld.Task;

        // called, once the first process has crashed, where the connection asks something of the process: on the connection's own thread.
        public Action<string>? Looking { get; set; }

        public int ProcessId => _incarnation;

        public bool HasExited { get; private set; } = true;

        public int ExitCode
        {
            get
            {
                if (_crashed)
                {
                    Looking?.Invoke("exit code");
                }

                return _exitCode;
            }
            private set => _exitCode = value;
        }

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
            if (_crashed)
            {
                Looking?.Invoke("start");
            }

            int incarnation;
            NamedPipeServerStream pipe;
            lock (_gate)
            {
                incarnation = ++_incarnation;
                _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
                HasExited = false;
                ExitCode = 0;
                _pipe = pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
            }

            _ = ServeAsync(pipe, incarnation);
            return Task.CompletedTask;
        }

        private async Task ServeAsync(NamedPipeServerStream pipe, int incarnation)
        {
            await pipe.WaitForConnectionAsync();
            var made = await OmniSharpLanguageServer.From(server => server
                .WithInput(PipeReader.Create(pipe))
                .WithOutput(PipeWriter.Create(pipe))
                .WithSerializer(new PullDiagnosticsLspSerializer())
                .OnJsonRequest(RDCorePlatformProtocol.PlatformInitialize, (JToken _, CancellationToken _) =>
                {
                    if (incarnation == HoldInitializeOf)
                    {
                        _initializeHeld.TrySetResult();
                        return new TaskCompletionSource<JToken>().Task;
                    }

                    return Task.FromResult<JToken>(JObject.FromObject(new { component = (int)CoreServerComponent.ParsingServer, provided = Array.Empty<string>() }));
                })
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

                    if (incarnation == HoldInitializeOf)
                    {
                        return new TaskCompletionSource<HeldResult>().Task;
                    }

                    return Task.FromResult(new HeldResult { Incarnation = incarnation });
                }));

            bool down;
            lock (_gate)
            {
                down = incarnation <= _downTo;
                if (!down)
                {
                    _server = made;
                }
            }

            if (down)
            {
                made.Dispose();
            }
        }

        // the process dies as a process does: its end of the pipe closes, and it exits with a code that is not 0.
        public void Crash()
        {
            GoDown();
            ExitCode = 1;
            HasExited = true;
            _crashed = true;
            _exit.TrySetResult();
        }

        public Task WaitForExitAsync() => _exit.Task;

        public void Shutdown()
        {
        }

        public void Dispose()
        {
            GoDown();
            HasExited = true;
            _exit.TrySetResult();
        }

        // the server of the process that is up, if it is made yet, and its pipe: a server made later is disposed by ServeAsync.
        private void GoDown()
        {
            OmniSharpLanguageServer? server;
            NamedPipeServerStream? pipe;
            lock (_gate)
            {
                _downTo = _incarnation;
                (server, _server) = (_server, null);
                (pipe, _pipe) = (_pipe, null);
            }

            server?.Dispose();
            pipe?.Dispose();
        }
    }
}
