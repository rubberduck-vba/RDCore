using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Server;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK;
using RDCore.SDK.Client;

namespace RDCore.LanguageServer.Debugging;

/// <summary>
/// The streams a debug adapter talks to its client over: what it reads and what it writes, and nothing else may.
/// </summary>
internal sealed class DebugAdapterTransport
{
    private readonly TaskCompletionSource _inputEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <param name="input">What the client says.</param>
    /// <param name="output">What the adapter says.</param>
    public DebugAdapterTransport(Stream input, Stream output)
    {
        Input = new EndNotifyingStream(input, () => _inputEnded.TrySetResult());
        Output = output;
    }

    /// <summary>What the client says.</summary>
    public Stream Input { get; }

    /// <summary>What the adapter says.</summary>
    public Stream Output { get; }

    /// <summary>Completes when the client has nothing more to say: it closed the stream, which is how a client that is gone is known.</summary>
    public Task InputEnded => _inputEnded.Task;

    // reads like the stream it wraps, and says when there is nothing left in it.
    private sealed class EndNotifyingStream(Stream inner, Action ended) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Observe(inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => Observe(await inner.ReadAsync(buffer, cancellationToken));

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Observe(await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken));

        private int Observe(int read)
        {
            if (read == 0)
            {
                ended();
            }

            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>
/// The RDCore <strong>debug adapter</strong> application: a <strong>DAP</strong> server over a pair of streams, for a program that runs on the platform.
/// </summary>
/// <remarks>
/// 👉 It is the language server's executable in another mode (<c>--dap</c>), because the language server is what knows how to bring the platform up; what it serves is
/// the debugger of a client that does not speak the language server's protocol. The requests are answered by <see cref="ProgramDebugAdapter"/>.
/// </remarks>
internal sealed class DebugAdapterApp(
    DebugAdapterTransport transport,
    ProgramDebugAdapter adapter,
    IDebugWorkspace workspace,
    ILogger<DebugAdapterApp> logger) : IRDCoreApp
{
    public CoreServerComponent PlatformComponent => CoreServerComponent.LanguageServer;

    public void LogIfEnabled(LogLevel logLevel, string message)
    {
        if (logger.IsEnabled(logLevel))
        {
            logger.Log(logLevel, "{message}", message);
        }
    }

    public async Task RunAsync(IServiceProvider provider, string[] args)
    {
        LogIfEnabled(LogLevel.Information, "🚀 Debug adapter starting");

        using var server = await StartServerAsync(transport, adapter);
        var lost = WatchAsync();

        // the client disconnects, or closes the streams.
        _ = await Task.WhenAny(adapter.Closed, transport.InputEnded);

        // the response to a disconnect is on its way out.
        await Task.Delay(TimeSpan.FromMilliseconds(250));
        await workspace.CloseAsync();
        LogIfEnabled(LogLevel.Information, "Debug adapter stopped");
        _ = lost;
    }

    /// <summary>
    /// Starts speaking <strong>DAP</strong> to a client over <paramref name="transport"/>, with <paramref name="adapter"/> answering what it asks.
    /// </summary>
    /// <param name="transport">What the client is talked to over.</param>
    /// <param name="adapter">What answers the client.</param>
    internal static Task<DebugAdapterServer> StartServerAsync(DebugAdapterTransport transport, ProgramDebugAdapter adapter)
        => DebugAdapterServer.From(options =>
        {
            options.Capabilities = new Capabilities
            {
                SupportsConfigurationDoneRequest = true,
                SupportsGotoTargetsRequest = true,
                SupportsTerminateRequest = true,
                SupportsEvaluateForHovers = true,
                SupportsExceptionInfoRequest = true,
                ExceptionBreakpointFilters = new Container<ExceptionBreakpointsFilter>(
                    new ExceptionBreakpointsFilter { Filter = ProgramDebugAdapter.UnhandledErrors, Label = DebuggerMessages.FilterUnhandledErrors, Default = true },
                    new ExceptionBreakpointsFilter { Filter = ProgramDebugAdapter.AllErrors, Label = DebuggerMessages.FilterAllErrors }),
            };

            options
                .WithInput(transport.Input)
                .WithOutput(transport.Output)
                .AddHandler(adapter)
                .OnInitialize((server, request, token) =>
                {
                    adapter.Attach(server);
                    adapter.Initialize(request);
                    return Task.CompletedTask;
                });
        });

    private async Task WatchAsync()
    {
        await adapter.LostAsync(await workspace.Lost);
    }

    public void Dispose()
    {
    }
}
