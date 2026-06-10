using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using OmniSharp.Extensions.LanguageServer.Server;
using RDCore.SDK.Client;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Handlers;
using RDCore.SDK.Server.Handlers.Lifecycle;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Server.Services.States;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO.Pipelines;
using System.IO.Pipes;
using System.Reflection;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace RDCore.SDK.Server;


public interface ILanguageServerProtocolTransportLayer : IDisposable
{

    /// <summary>
    /// Gets a <c>Task</c> that completes then the server establishes a transport-level connection with a client.
    /// </summary>
    /// <param name="options">The <c>OmniSharp</c> language server options.</param>
    /// <param name="processToken">The <c>CancellationToken</c> that controls the application's process termination.</param>
    Task GetWaitForClientConnectionTaskAsync(LanguageServerOptions options, CancellationToken processToken);
}

/// <summary>
/// The default <c>RDCore.SDK</c> transport layer configuration.
/// </summary>
/// <remarks>
/// Implements the client/server connection over <em>named pipes</em> streams.
/// </remarks>
public sealed class RDCorePlatformDefaultTransportLayer(IOptions<TransportOptions> Options, ILogger<RDCorePlatformDefaultTransportLayer> Logger) : ILanguageServerProtocolTransportLayer
{
    private TransportOptions Options { get; } = Options.Value;
    private NamedPipeServerStream NamedPipeServerStream { get; set; } = default!;

    public void Dispose()
    {
        NamedPipeServerStream?.Dispose();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "VSTHRD200:Use \"Async\" suffix for async methods", 
        Justification = "This method yields a task and registers it for disposal, but does not await it - by design.")]
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
}


public interface IRDCoreLSPHandlerConfigurationBuilder
{
    /// <summary>
    /// Configures a <strong>LSP 3.17</strong> (OmniSharp) <em>handler</em> for dependency injection in a <c>RDCore.SDK</c> application.
    /// </summary>
    /// <typeparam name="THandler">The specific concrete implementation type of <em>OmniSharp</em> LSP handler class to register.</typeparam>
    IRDCoreLSPHandlerConfigurationBuilder WithHandler<THandler>() where THandler : class, IJsonRpcHandler;

}
/// <summary>
/// A <em>builder</em> that configures LSP handlers for this server app.
/// </summary>
/// <param name="Options">The LSP language server options.</param>
public class RDCoreLanguageServerHandlersConfigurationBuilder(LanguageServerOptions Options) : IRDCoreLSPHandlerConfigurationBuilder
{
    private LanguageServerOptions Options { get; } = Options;

    IRDCoreLSPHandlerConfigurationBuilder IRDCoreLSPHandlerConfigurationBuilder.WithHandler<THandler>()
    {
        Options.WithHandler<THandler>(new() { RequestProcessType = RequestProcessType.Parallel });
        return this;
    }
}

/// <summary>
/// Any application that hosts an OmniSharp language server, irrespective of its purpose.
/// </summary>
public interface ILanguageServerApp : IRDCoreApp
{
    /// <summary>
    /// Gets the <see cref="ILanguageServer"/> (OmniSharp) LSP server <em>once initialized.</em>
    /// </summary>
    /// <exception cref=""></exception>
    ILanguageServer LanguageServer { get; }
}

public abstract class LanguageServerApp(
    IOptions<SdkServerOptions> options,
    IServerStateProvider serverStateProvider,
    IHealthCheckService<ILanguageServerApp> healthCheckService,
    ILanguageServerProtocolTransportLayer transportLayer,
    ILogger<LanguageServerApp> logger) : ILanguageServerApp
{
    protected IServerStateProvider ServerStateProvider { get; } = serverStateProvider;

    private OmniSharpLanguageServer? Server { get; set; }

    private Task? WaitForClientConnectionTask { get; set; }

    /// <summary>
    /// Gets the encapsulated <c>OmniSharp</c> language server interface.
    /// </summary>
    /// <remarks>
    /// ⚠️ <strong><em>Temporal coupling</em>: this property getter will throw</strong> if it is used before <c>RunAsync</c> is called.
    /// </remarks>
    /// <exception cref="LanguageServerProtocolSdkException">Thrown </exception>
    public ILanguageServer LanguageServer => Server 
        ?? throw new LanguageServerProtocolSdkException(Exceptions.LanguageServerProtocolSdkException_ServerNotInitialized);

    public async Task RunAsync(IServiceProvider externalServiceProvider)
    {
        LogIfEnabled(LogLevel.Trace, TraceMessages.LanguageServerStarting);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(options.Value.ConnectTimeoutSeconds));
        Server = await OmniSharpLanguageServer.From(lspOptions => ConfigureServer(lspOptions), externalServiceProvider, cts.Token);

        LogIfEnabled(LogLevel.Information, TraceMessages.LanguageServerConnected);
        await Server.WaitForExit;

        LogIfEnabled(LogLevel.Trace, TraceMessages.LanguageServerWaitForExitTaskCompleted);
    }

    private void HandleUnhealthyClient() => ServerStateProvider.ProcessTokenSource.Cancel();

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
        WaitForClientConnectionTask = transportLayer.GetWaitForClientConnectionTaskAsync(options, ServerStateProvider.ProcessTokenSource.Token);
        options
            // basic server app information:
            .WithServerInfo(GetServerInfo())
            // wire-up lifecycle delegates:
            .OnStarted(HandleLanguageServerStartedAsync)
            .OnInitialize(HandleLanguageServerInitializeAsync)
            .OnInitialized(HandleLanguageServerInitializedAsync)
            // core SDK handlers:
            .ConfigureCoreSdkHandlers();

        // everything else the app wants to do:
        ConfigureHandlers(new RDCoreLanguageServerHandlersConfigurationBuilder(options));

        options.WithServices(services =>
        {
            services.AddScoped<ILanguageServerFacade>(provider => Server!);
            services.AddLogging(builder =>
            {
                builder.AddLanguageProtocolLogging();
            });
        });

        LogIfEnabled(LogLevel.Trace, TraceMessages.LanguageServerConfigurationCompleted);
    }

    /// <summary>
    /// Configures <c>OmniSharp</c> LSP-compliant JSON-RPC handlers for any <strong>LSP 3.17</strong> specified protocol event.
    /// </summary>
    /// <param name="builder">A <em>builder</em> that lets you fluently chain repetitive calls.</param>
    /// <remarks>
    /// 🧩 This method is invoked immediately after configuring <see cref="ClientInfo"/> and the client/server lifecycle protocol handlers:
    /// <list type="bullet">
    /// <item><see cref="ShutdownHandler"/></item>
    /// <item><see cref="ExitHandler"/></item>
    /// <item><see cref="SetTraceHandler"/></item>
    /// <item><see cref="ExecuteCommandHandler"/></item>
    /// </list>
    /// </remarks>
    protected abstract void ConfigureHandlers(IRDCoreLSPHandlerConfigurationBuilder builder);

    /// <summary>
    /// Gets information about this LSP client/server application and its configuration.
    /// </summary>
    /// <remarks>
    /// 🧩 The base implementation returns the <c>Name</c> and <c>Version</c> of the executing <see cref="Assembly"/>,
    /// which is everything <see cref="ServerInfo"/> needs.
    /// </remarks>
    protected virtual ServerInfo GetServerInfo()
    {
        var assemblyName = Assembly.GetEntryAssembly()?.GetName();
        return new()
        {
            Name = assemblyName?.Name ?? "RDCore.CustomLanguageServerApp",
            Version = (assemblyName?.Version ?? new Version()).ToString(3)
        };
    }

    /// <summary>
    /// Registers the <em>capabilities</em> of this LSP server application, using the provided <see cref="ClientCapabilities"/>.
    /// </summary>
    /// <remarks>
    /// 🧩 This method is invoked during the <em>initialization handshake</em> when the <strong>client</strong> emits its <em>capabilities</em> in an <see cref="InitializeParams"/> (LSP <c>Initialize</c> request).
    /// </remarks>
    /// <param name="server">The initializing <c>OmniSharp</c> LSP server instance.</param>
    /// <param name="clientCapabilities">The <em>client capabilities</em> reported by the client.</param>
    /// <param name="token">A <see cref="CancellationToken"/> for cooperative cancellation.</param>
    protected abstract Task RegisterServerCapabilitiesAsync(ILanguageServer server, ClientCapabilities clientCapabilities, CancellationToken token);

    private async Task HandleLanguageServerStartedAsync(ILanguageServer server, CancellationToken token)
        => await OnLanguageServerStartedAsync(server, token);
    /// <summary>
    /// Gives your class or handler an opportunity to interact with <see cref="ILanguageServer" /> after the connection has been established.
    /// </summary>
    /// <remarks>
    /// 🧩 The base implementation simply logs handler completion at <c>Trace</c> level.
    /// </remarks>
    protected async virtual Task OnLanguageServerStartedAsync(ILanguageServer server, CancellationToken token)
        => LogIfEnabled(LogLevel.Trace, TraceMessages.LanguageServerStarted_HandlerCompleted);

    /// <summary>
    /// Signals the completion of the <c>Initialize</c> request handler.
    /// </summary>
    /// <remarks>
    /// 🧩 This method is invoked at the end of the <em>initialization handshake</em> after the server configured capabilities and determined a <em>workspace URI</em>.
    /// </remarks>
    /// <param name="workspaceUri">The <em>workspace</em> <see cref="Uri"/>, if one could be determined from the supplied initialization parameters.</param>
    /// <param name="token">A <see cref="CancellationToken"/> for cooperative cancellation.</param>
    protected virtual async Task OnLanguageServerInitializeCompletedAsync(Uri? workspaceUri, CancellationToken token) { }

    /// <summary>
    /// 🚀 LSP initialization has completed, server is ready to start receiving and responding to client requests and notifications.
    /// </summary>
    /// <param name="token">A <see cref="CancellationToken"/> for cooperative cancellation.</param>
    /// <remarks>
    /// <strong>The client owns the file system</strong> for any document that is currently <em>opened</em>.<br/>
    /// ❌ <strong>DO NOT</strong> configure any server-side <see cref="System.IO.FileSystemWatcher"/>.
    /// </remarks>
    protected virtual async Task OnLanguageServerStartedAsync(CancellationToken token) { }

    private async Task HandleLanguageServerInitializeAsync(ILanguageServer server, InitializeParams request, CancellationToken token)
    {
        ServerStateProvider.OnInitialize();
        if (request.ProcessId is not null)
        {
            if (request.ProcessId.Value <= int.MaxValue)
            {
                // Initialize request specifies a client process ID; start monitoring the client process health
                healthCheckService.Start(Convert.ToInt32(request.ProcessId.Value), HandleUnhealthyClient);
            }
            else 
            {
                LogIfEnabled(LogLevel.Warning, TraceMessages.InitializeClientProcessIdOutOfRange);
            }
        }
        else
        {
            LogIfEnabled(LogLevel.Warning, TraceMessages.InitializeMissingClientProcessId);
        }

        if (request.Capabilities is ClientCapabilities capabilities)
        {
            await RegisterServerCapabilitiesAsync(server, capabilities, token);
        }

        await OnLanguageServerInitializeAsync(server, request, token);
        await OnLanguageServerInitializeCompletedAsync(request.RootUri?.ToUri(), token);
        LogIfEnabled(LogLevel.Trace, TraceMessages.LanguageServerInitialize_HandlerCompleted);
    }

    /// <summary>
    /// Gives your class or handler an opportunity to interact with the <see cref="InitializeParams" /> before it is processed by the server
    /// </summary>
    /// <remarks>
    /// 🧩 The base implementation simply logs handler completion at <c>Trace</c> level.
    /// This method runs <strong>after server capabilities registration </strong>.
    /// </remarks>
    protected virtual async Task OnLanguageServerInitializeAsync(ILanguageServer server, InitializeParams request, CancellationToken cancellationToken)
        => LogIfEnabled(LogLevel.Trace, TraceMessages.LanguageServerInitialize_HandlerCompleted);

    private async Task HandleLanguageServerInitializedAsync(ILanguageServer server, InitializeParams request, InitializeResult response, CancellationToken cancellationToken)
    {
        ServerStateProvider.OnInitialized();
        await OnLanguageServerInitializedAsync(server, request, response, cancellationToken);
    }
    /// <summary>
    /// Gives your class or handler an opportunity to interact with the <see cref="InitializeParams" /> and <see cref="InitializeResult" /> after it is processed by the server but before it is sent to the client
    /// </summary>
    /// <remarks>
    /// 🧩 The base implementation simply logs handler completion at <c>Trace</c> level.
    /// </remarks>
    protected virtual async Task OnLanguageServerInitializedAsync(ILanguageServer server, InitializeParams request, InitializeResult response, CancellationToken cancellationToken) 
        => LogIfEnabled(LogLevel.Trace, TraceMessages.LanguageServerInitialized_HandlerCompleted);

    public void LogIfEnabled(LogLevel logLevel, string message)
    {
        if (logger.IsEnabled(logLevel))
        {
            logger.Log(logLevel, "{message}", message);
        }
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
