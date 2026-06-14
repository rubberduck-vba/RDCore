using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using OmniSharp.Extensions.LanguageServer.Server;
using RDCore.SDK.Server.Configuration;
using System.IO.Pipelines;
using System.IO.Pipes;

namespace RDCore.SDK.Server;

/// <summary>
/// The RDCore LSP transport layer interface.
/// </summary>
public interface ILanguageServerProtocolTransportLayer : IDisposable
{

    /// <summary>
    /// Gets a <c>Task</c> that completes then the server establishes a transport-level connection with a client.
    /// </summary>
    /// <param name="options">The <c>OmniSharp</c> language server options.</param>
    /// <param name="processToken">The <c>CancellationToken</c> that controls the application's process termination.</param>
    Task GetWaitForClientConnectionTaskAsync(LanguageServerOptions options, CancellationToken processToken);
    /// <summary>
    /// Configures client-side transport.
    /// </summary>
    /// <param name="options">The <see cref="LanguageClientOptions"/> to configure the LSP client.</param>
    void ConfigureClient(LanguageClientOptions options);
}


/// <summary>
/// The default <c>RDCore.SDK</c> transport layer implementation.
/// </summary>
/// <remarks>
/// Implements the client/server connection over <em>named pipes</em> streams.
/// </remarks>
public sealed class RDCorePlatformDefaultTransportLayer(IOptions<TransportOptions> Options, ILogger<RDCorePlatformDefaultTransportLayer> Logger) : ILanguageServerProtocolTransportLayer
{
    private TransportOptions Options { get; } = Options.Value;
    private NamedPipeServerStream NamedPipeServerStream { get; set; } = default!;
    private NamedPipeClientStream NamedPipeClientStream { get; set; } = default!;

    /// <summary>
    /// Disposes unmanaged resources.
    /// </summary>
    public void Dispose()
    {
        NamedPipeServerStream?.Dispose();
    }

    public Task GetWaitForClientConnectionTaskAsync(LanguageServerOptions options, CancellationToken processToken)
    {
        var pipeName = Options.PipeConfig.PipeName;
        NamedPipeServerStream = new NamedPipeServerStream(pipeName, PipeDirection.InOut,
            Options.PipeConfig.MaximumInstances,
            PipeTransmissionMode.Byte, // NOTE: 'Message' transmission mode is only supported with Windows pipes.
            System.IO.Pipes.PipeOptions.Asynchronous |
            System.IO.Pipes.PipeOptions.CurrentUserOnly);

        options
            .WithInput(PipeReader.Create(NamedPipeServerStream))
            .WithOutput(PipeWriter.Create(NamedPipeServerStream));

        if (Logger.IsEnabled(LogLevel.Trace))
        {
            Logger.LogTrace("⏳ Named pipe '{pipeName}' initialized; asynchronously awaiting client connection...", pipeName);
        }
        return NamedPipeServerStream.WaitForConnectionAsync(processToken);
    }

    public void ConfigureClient(LanguageClientOptions options)
    {
        var pipeName = Options.PipeConfig.PipeName;
        NamedPipeClientStream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, System.IO.Pipes.PipeOptions.CurrentUserOnly);
        options
            .WithInput(PipeReader.Create(NamedPipeClientStream))
            .WithOutput(PipeWriter.Create(NamedPipeClientStream));
    }
}
