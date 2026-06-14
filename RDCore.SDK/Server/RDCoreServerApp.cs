using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Client;
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
using System.Reflection;

using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;
namespace RDCore.SDK.Server;

/// <summary>
/// A server-side (LSP) RDCore app.
/// </summary>
public interface IRDCoreServerApp : IRDCoreApp
{
    /// <summary>
    /// The <c>OmniSharp</c> <em>language server</em>, once initialized.
    /// </summary>
    /// <remarks>
    /// ⚠️ This property <strong>will throw</strong> if used before initialization.
    /// </remarks>
    ILanguageServer LanguageServer { get; }
}

/// <summary>
/// A server-side (LSP) RDCore app.
/// </summary>
/// <param name="options">The current server configuration options.</param>
/// <param name="serverStateProvider">Manages the <em>operational state</em> of the server application.</param>
/// <param name="healthCheckService">A service that monitors the server process.</param>
/// <param name="transportLayer">The RDCore/LSP transport layer.</param>
/// <param name="logger">A standard logger.</param>
/// <remarks>
/// 🧩 Since RDCore extensions are LSP servers, this is the base class for most RDCore applications.
/// </remarks>
public abstract class RDCoreServerApp(
    IOptions<SdkServerOptions> options,
    IServerStateProvider serverStateProvider,
    IHealthCheckService<RDCoreServerApp> healthCheckService,
    ILanguageServerProtocolTransportLayer transportLayer,
    ILogger<RDCoreServerApp> logger) : IRDCoreServerApp
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

        Server = await OmniSharpLanguageServer.From(ConfigureServer, externalServiceProvider, ServerStateProvider.ProcessTokenSource.Token);

        if (WaitForClientConnectionTask is not null && Server is not null)
        {
            await WaitForClientConnectionTask;
            LogIfEnabled(LogLevel.Information, TraceMessages.LanguageServerConnected);

            await Server.WaitForExit;
            LogIfEnabled(LogLevel.Trace, TraceMessages.LanguageServerWaitForExitTaskCompleted);
        }
    }

    private void HandleUnhealthyClient() => ServerStateProvider.ProcessTokenSource.Cancel();

    /// <summary>
    /// Disposes of any unmanaged resources held at instance level.
    /// </summary>
    protected abstract void Dispose(bool disposing);

    /// <summary>
    /// Disposes of any unmanaged resources held at instance level.
    /// </summary>
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
    /// Gets information about this LSP server application and its configuration.
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
    protected abstract void RegisterServerCapabilities(ILanguageServer server, ClientCapabilities clientCapabilities);

    private async Task HandleLanguageServerStartedAsync(ILanguageServer server, CancellationToken token) => OnLanguageServerStarted(server);

    /// <summary>
    /// 🚀 LSP initialization has completed, server is ready to start receiving and responding to client requests and notifications.
    /// </summary>
    /// <remarks>
    /// <strong>The client owns the file system</strong> for any document that is currently <em>opened</em>.<br/>
    /// ❌ <strong>DO NOT</strong> configure any server-side <see cref="System.IO.FileSystemWatcher"/>.
    /// </remarks>
    protected virtual void OnLanguageServerStarted(ILanguageServer server) { }

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
            RegisterServerCapabilities(server, capabilities);
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

    /// <summary>
    /// Logs the specified message at the specified level, if logging is enabled at that level.
    /// </summary>
    /// <param name="logLevel">The <see cref="LogLevel"/> for this message.</param>
    /// <param name="message">The log <c>message</c>.</param>
    public void LogIfEnabled(LogLevel logLevel, string message)
    {
        if (logger.IsEnabled(logLevel))
        {
            logger.Log(logLevel, "{message}", message);
        }
    }
}

internal static class LanguageServerOptionsExtensions
{
    internal static LanguageServerOptions ConfigureCoreSdkHandlers(this LanguageServerOptions options) => options
        .WithHandler<ShutdownHandler>()
        .WithHandler<ExitHandler>()
        .WithHandler<SetTraceHandler>()

        .WithHandler<ExecuteCommandHandler>();
}
