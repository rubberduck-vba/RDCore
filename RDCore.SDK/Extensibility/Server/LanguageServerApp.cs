using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Server;
using RDCore.SDK.Extensibility.Configuration;
using RDCore.SDK.Extensibility.Server.Handlers;
using RDCore.SDK.Extensibility.Server.Handlers.Lifecycle;
using RDCore.SDK.Extensibility.Server.Services.States;
using System.Reflection;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace RDCore.SDK.Extensibility.Server;

/// <summary>
/// Any application that hosts an OmniSharp language server, irrespective of its purpose.
/// </summary>
public interface ILanguageServerApp : IRDCoreApp
{
    /// <summary>
    /// Gets the encapsulated <c>OmniSharp</c> language server interface once initialized.
    /// </summary>
    ILanguageServer LanguageServer { get; }
}

/// <summary>
/// Any application that hosts an OmniSharp language server, irrespective of its purpose.
/// </summary>
/// <param name="options">The effective configuration settings.</param>
/// <param name="logger">A logger.</param>
/// <param name="serverStateProvider">Manages the <em>operational state</em> of the server.</param>
/// <param name="healthCheckService">Monitors the client process to prevent running an orphaned server.</param>
/// <param name="transportLayer">The transport layer service.</param>
public abstract class LanguageServerApp(
    IOptions<SdkServerOptions> options,
    ILogger<LanguageServerApp> logger,
    IServerStateProvider serverStateProvider,
    IHealthCheckService<ILanguageServerApp> healthCheckService,
    ILanguageServerProtocolTransportLayer transportLayer) : RDCoreApp(options, logger), ILanguageServerApp
{
    /// <summary>
    /// Manages the current <em>operational server state</em>.
    /// </summary>
    protected IServerStateProvider ServerStateProvider { get; } = serverStateProvider;

    private OmniSharpLanguageServer? Server { get; set; }

    private Task? WaitForClientConnectionTask { get; set; }

    /// <summary>
    /// Gets the encapsulated <c>OmniSharp</c> language server interface once initialized.
    /// </summary>
    /// <remarks>
    /// ⚠️ <strong><em>Temporal coupling</em>: this property getter will throw</strong> if it is used before <c>RunAsync</c> is called.
    /// </remarks>
    /// <exception cref="LanguageServerProtocolSdkException">Thrown </exception>
    public ILanguageServer LanguageServer => Server ?? throw new LanguageServerProtocolSdkException(Exceptions.LanguageServerProtocolSdkException_ServerNotInitialized);

    /// <summary>
    /// Starts the application.
    /// </summary>
    protected override async Task StartAppAsync(CancellationToken token)
    {
        LogIfEnabled(LogLevel.Trace, TraceMessages.LanguageServerStarting);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Options.ConnectTimeoutSeconds));
        Server = await OmniSharpLanguageServer.From(lspOptions => ConfigureServer(lspOptions), BootstrapProvider, token);

        LogIfEnabled(LogLevel.Information, TraceMessages.LanguageServerConnected);
        await Server.WaitForExit;

        LogIfEnabled(LogLevel.Trace, TraceMessages.LanguageServerWaitForExitTaskCompleted);
    }

    private void HandleUnhealthyClient(CancellationToken token) => ServerStateProvider.ProcessTokenSource.Cancel();

    /// <summary>
    /// Disposes of any unmanaged resources.
    /// </summary>
    protected abstract void Dispose(bool disposing);

    /// <summary>
    /// Disposes of any unmanaged resources.
    /// </summary>
    public sealed override void Dispose()
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
}

internal static class LanguageServerOptionsExtensions
{
    internal static LanguageServerOptions ConfigureCoreSdkHandlers(this LanguageServerOptions options) => options
        .WithHandler<ShutdownHandler>()
        .WithHandler<ExitHandler>()
        .WithHandler<SetTraceHandler>()

        .WithHandler<ExecuteCommandHandler>();
}
