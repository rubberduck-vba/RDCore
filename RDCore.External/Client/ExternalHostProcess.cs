using RDCore.External.Protocol;
using RDCore.SDK.Client;
using RDCore.SDK.Client.Connection;
using RDCore.SDK.Platform;
using System.IO.Pipes;

namespace RDCore.External.Client;

/// <summary>
/// One process of an external host, as an <see cref="ExternalHost"/> starts it: what it starts, the stream its calls go over, and how it is stopped.
/// </summary>
public interface IExternalHostProcess : IDisposable
{
    /// <summary>
    /// The identifier of the process, once it is started.
    /// </summary>
    int ProcessId { get; }

    /// <summary>
    /// Starts the process and connects to it, and gives the stream the calls go over: the stream of a <see cref="SDK.Platform.Channels.CallChannel"/>. It
    /// throws when the process cannot be started or does not come up.
    /// </summary>
    /// <param name="onExited">What is told that the process stopped.</param>
    /// <returns>The stream the calls go over.</returns>
    Stream Start(Action onExited);

    /// <summary>
    /// Asks the process to stop, and waits at most <paramref name="timeout"/> for it to.
    /// </summary>
    /// <param name="timeout">How long to wait for the process to stop.</param>
    void Stop(TimeSpan timeout);
}

/// <summary>
/// The process of an external host that <c>rdc</c> is, run in external mode, which a <see cref="ChildConnection"/> starts, supervises and stops.
/// </summary>
/// <param name="connection">The connection to the process, and the process.</param>
/// <param name="executable">The executable the external host is: <c>rdc</c> (<see cref="ExternalHost.DefaultExecutable"/>).</param>
public sealed class ExternalHostProcess(ChildConnection connection, string executable) : IExternalHostProcess
{
    /// <inheritdoc/>
    public int ProcessId => connection.ProcessId;

    /// <inheritdoc/>
    public Stream Start(Action onExited)
    {
        var pipeName = PlatformPipeName.New(CoreServerComponent.ExternalHost);
        // a call of the program into a server waits for the host to come up, as it waits for the server: the connection completes on the thread pool, and
        // never needs the thread that waits for it.
#pragma warning disable VSTHRD002
        connection.ConnectAsync(new ChildConnectionRequest
        {
            ServerExecutablePath = executable,
            PipeName = pipeName,
            Mode = RDCoreServerProcess.ExternalMode,
            ExpectedComponent = CoreServerComponent.ExternalHost,
            // a process that stopped is not started again behind the program's back: what it held is gone, and the next call starts a new one.
            MaxRestartAttempts = 0,
            ConfigureClient = _ => { },
            OnPeerExited = onExited,
        }, CancellationToken.None).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002

        // the calls go over a pipe of their own, which the process opened before it said it was up.
        var calls = new NamedPipeClientStream(
            ".", ExternalProtocol.CallsPipeName(pipeName), PipeDirection.InOut, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
        calls.Connect(TimeSpan.FromSeconds(30));
        return calls;
    }

    // stopping is synchronous: the wait is bounded, and the shutdown never needs the thread that waits for it.
    /// <inheritdoc/>
    public void Stop(TimeSpan timeout)
    {
#pragma warning disable VSTHRD002
        _ = connection.ShutdownAsync().Wait(timeout);
#pragma warning restore VSTHRD002
    }

    /// <inheritdoc/>
    public void Dispose() => connection.Dispose();
}
