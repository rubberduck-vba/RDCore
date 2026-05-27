using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using OmniSharp.Extensions.LanguageServer.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Handlers;
using RDCore.SDK.Server.Handlers.Lifecycle;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Server.Services.States;
using System.IO.Pipelines;
using System.IO.Pipes;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace RDCore.SDK.Server;


public interface ILanguageServerProtocolTransportLayer : IDisposable
{

#pragma warning disable VSTHRD200 // Use "Async" suffix for async methods
    /// <summary>
    /// Gets a <c>Task</c> that completes then the server establishes a transport-level connection with a client.
    /// </summary>
    /// <param name="options">The <c>OmniSharp</c> language server options.</param>
    /// <param name="processToken">The <c>CancellationToken</c> that controls the application's process termination.</param>
    Task GetWaitForClientConnectionTask(LanguageServerOptions options, CancellationToken processToken);
#pragma warning restore VSTHRD200 // Use "Async" suffix for async methods
}

/// <summary>
/// The default <c>RDCore.SDK</c> transport layer configuration.
/// </summary>
/// <remarks>
/// Implements the client/server connection over <em>named pipes</em> (efficient & guaranteed-local) streams.
/// </remarks>
public sealed class RDCorePlatformDefaultTransportLayer(ServerOptions StartupOptions, ILogger Logger) : ILanguageServerProtocolTransportLayer
{
    private ServerOptions StartupOptions { get; } = StartupOptions;
    private NamedPipeServerStream NamedPipeServerStream { get; set; } = default!;

    public void Dispose()
    {
        NamedPipeServerStream?.Dispose();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "VSTHRD200:Use \"Async\" suffix for async methods", 
        Justification = "This method yields a task and registers it for disposal, but does not await it - by design.")]
    public Task GetWaitForClientConnectionTask(LanguageServerOptions options, CancellationToken processToken)
    {
        var pipeName = StartupOptions.PipeName;
        NamedPipeServerStream = new NamedPipeServerStream(pipeName, PipeDirection.InOut,
            StartupOptions.MaximumInstances,
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
}


public record class LanguageServerAppOptions 
{
    public required string PipeName { get; init; }
}

public interface IRDCoreLSPHandlerConfigurationBuilder
{
    /// <summary>
    /// Configures a <strong>LSP 3.17</strong> (OmniSharp) <em>handler</em> for dependency injection in a <c>RDCore.SDK</c> application.
    /// </summary>
    /// <typeparam name="THandler">The specific concrete implementation type of <em>OmniSharp</em> LSP handler class to register.</typeparam>
    IRDCoreLSPHandlerConfigurationBuilder WithHandler<THandler>() where THandler : class, IJsonRpcHandler;

}
internal class RDCoreLanguageServerProtocolHandlersConfigurationBuilder(LanguageServerOptions LanguageServerOptions) : IRDCoreLSPHandlerConfigurationBuilder
{
    private LanguageServerOptions Options { get; } = LanguageServerOptions;

    IRDCoreLSPHandlerConfigurationBuilder IRDCoreLSPHandlerConfigurationBuilder.WithHandler<THandler>()
    {
        Options.WithHandler<THandler>(new JsonRpcHandlerOptions() { RequestProcessType = RequestProcessType.Parallel });
        return this;
    }
}


/// <summary>
/// Any application that runs an OmniSharp language server, irrespective of its purpose.
/// </summary>
public interface ILanguageServerApp : IDisposable
{
    ILanguageServer LanguageServer { get; }
    Task RunAsync(IServiceProvider provider);
}

public abstract class LanguageServerApp(IOptions<LanguageServerAppOptions> appSettings,
    IServerStateProvider serverStateProvider,
    IHealthCheckService healthCheckService,
    ILanguageServerProtocolTransportLayer transportLayer,
    ILogger<LanguageServerApp> logger) : ILanguageServerApp
{
    private OmniSharpLanguageServer? Server { get; set; } = default;

    private Task? WaitForClientConnectionTask { get; set; }

    /// <summary>
    /// Gets the encapsulated <c>OmniSharp</c> language server interface.
    /// </summary>
    /// <remarks>
    /// ⚠️ <strong><em>Temporal coupling</em>: this property getter will throw</strong> if it is used before <c>RunAsync</c> is called.
    /// </remarks>
    /// <exception cref="LanguageServerProtocolSdkException">Thrown </exception>
    public ILanguageServer LanguageServer => Server 
        ?? throw new LanguageServerProtocolSdkException(Exceptions.LanguageServerProtocolSdkException_NotInitialized);

    public async Task RunAsync(IServiceProvider provider)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogInformation(TraceMessages.LanguageServerStarting);
        }
        Server = await OmniSharpLanguageServer.From(ConfigureServer, provider, serverStateProvider?.ProcessToken ?? CancellationToken.None);
        logger.LogInformation(TraceMessages.LanguageServerConnected);

        await Server.WaitForExit;
        logger.LogInformation(TraceMessages.LanguageServerWaitForExitTaskCompleted);
    }

    protected abstract void Dispose(bool disposing);

    public void Dispose()
    {
        if (transportLayer is IDisposable disposableTransport)
        {
            disposableTransport.Dispose();
        }
        if (WaitForClientConnectionTask is IDisposable disposableTask)
        {
            disposableTask.Dispose();
        }
        if (Server is IDisposable disposableServer)
        {
            disposableServer.Dispose();
        }

        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void ConfigureServer(LanguageServerOptions options)
    {
        WaitForClientConnectionTask = transportLayer.GetWaitForClientConnectionTask(options, serverStateProvider.ProcessToken);
        options
            .WithServerInfo(serverStateProvider.ServerInfo)
            // wire-up lifecycle delegates:
            .OnStarted(OnLanguageServerStartedAsync)
            .OnInitialize(OnLanguageServerInitializeAsync)
            .OnInitialized(OnLanguageServerInitializedAsync)
            // core SDK handlers:
            .ConfigureCoreSdkHandlers();

        // everything else:
        ConfigureHandlers(new RDCoreLanguageServerProtocolHandlersConfigurationBuilder(options));

        options.WithServices(services =>
        {
            services.AddLogging(builder =>
            {
                builder.AddLanguageProtocolLogging();
            });
        });

        logger.LogInformation("✅ ConfigureServer completed.");
    }

    /// <summary>
    /// Configures <c>OmniSharp</c> LSP-compliant JSON-RPC handlers for any <strong>LSP 3.17</strong> specified protocol event.
    /// </summary>
    /// <param name="builder">A <em>builder</em> that lets you fluently chain repetitive calls.</param>
    protected abstract void ConfigureHandlers(IRDCoreLSPHandlerConfigurationBuilder builder);

    /// <summary>
    /// Gets information about this LSP client/server application and its configuration.
    /// </summary>
    protected abstract ServerInfo GetServerInfo();

    protected abstract Task RegisterServerCapabilitiesAsync(ILanguageServer server, ClientCapabilities clientCapabilities, CancellationToken token);
    protected virtual async Task OnLanguageServerInitializeCompletedAsync(Uri workspaceUri, CancellationToken token) { }

    protected virtual async Task OnLanguageServerStartedAsync(CancellationToken token) { }

    private async Task OnLanguageServerInitializeAsync(ILanguageServer server, InitializeParams request, CancellationToken token)
    {
        // ## OnLanguageServerInitializeDelegate
        // Gives your class or handler an opportunity to interact with the InitializeParams
        // before it is processed by the server.
        serverStateProvider.OnInitialize();

        if (request.ProcessId.HasValue)
        {
            // Initialize request specifies a client process ID; start monitoring the client process health
            healthCheckService.Start(request.ProcessId);
        }
        else
        {
            logger.LogWarning("Initialize request did not specify a client process ID. Skipping client process health checks; this server instance will not automatically exit.");
        }

        if (request.Capabilities is ClientCapabilities capabilities)
        {
            await RegisterServerCapabilitiesAsync(server, capabilities, token);
        }
        

        logger.LogInformation("✅ OnLanguageServerInitializeAsync handler completed.");
    }

    private async Task OnLanguageServerInitializedAsync(ILanguageServer server, InitializeParams request, InitializeResult response, CancellationToken cancellationToken)
    {
        // ## OnLanguageServerInitializedDelegate
        // Gives your class or handler an opportunity to interact with the InitializeParams and InitializeResult
        // after it is processed by the server but before it is sent to the client.
        serverStateProvider.OnInitialized();
        logger.LogInformation("✅ OnLanguageServerInitializedAsync handler completed.");
    }

    private async Task OnLanguageServerStartedAsync(ILanguageServer server, CancellationToken token)
    {
        // ## OnLanguageServerStartedDelegate
        // Gives your class or handler an opportunity to interact with the ILanguageServer
        // after the connection has been established.

        // TODO - start parsing, we're ready to receive client requests and notifications at this point.
        logger.LogInformation("✅ OnLanguageServerStartedAsync handler completed.");
    }
}

public static class LanguageServerOptionsExtensions
{
    internal static LanguageServerOptions ConfigureCoreSdkHandlers(this LanguageServerOptions options) => options
        .WithHandler<ShutdownHandler>()
        .WithHandler<ExitHandler>()
        .WithHandler<SetTraceHandler>()

        .WithHandler<ExecuteCommandHandler>();
}
