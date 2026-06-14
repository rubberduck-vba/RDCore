using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Services;
using System.Reflection;

using OmniSharpLanguageClient = OmniSharp.Extensions.LanguageServer.Client.LanguageClient;
namespace RDCore.SDK.Client;

/// <summary>
/// A client-side (LSP) RDCore app.
/// </summary>
public interface IRDCoreClientApp : IRDCoreApp
{
    /// <summary>
    /// The <c>OmniSharp</c> <em>language client</em>, once initialized.
    /// </summary>
    /// <remarks>
    /// ⚠️ This property <strong>will throw</strong> if used before initialization.
    /// </remarks>
    ILanguageClient LanguageClient { get; }
}

/// <summary>
/// A client-side (LSP) RDCore app.
/// </summary>
/// <param name="serverProcess">Encapsulates the <c>Process</c> of the server application.</param>
/// <param name="healthCheckService">A service that monitors the server process.</param>
/// <param name="transportLayer">The RDCore/LSP transport layer.</param>
/// <param name="logger">A standard logger.</param>
/// <remarks>
/// 🧩 Most RDCore apps are server-side, but if you were making an IDE or a CLI app, this would be your LSP app.
/// </remarks>
public abstract class RDCoreClientApp(
    IRDCoreLanguageServerProcess serverProcess,
    IHealthCheckService<RDCoreClientApp> healthCheckService,
    ILanguageServerProtocolTransportLayer transportLayer,
    ILogger<RDCoreClientApp> logger) : IRDCoreClientApp
{
    private CancellationTokenSource? ServerToken { get; set; }
    private OmniSharpLanguageClient? Client { get; set; }
    //private IServiceProvider? ExternalServiceProvider { get; set; }

    /// <summary>
    /// The <c>OmniSharp</c> <em>language client</em>, once initialized.
    /// </summary>
    /// <remarks>
    /// ⚠️ This property <strong>will throw</strong> if used before initialization.
    /// </remarks>
    /// <exception cref="LanguageServerProtocolSdkException"></exception>
    public ILanguageClient LanguageClient => Client
        ?? throw new LanguageServerProtocolSdkException(Exceptions.LanguageServerProtocolSdkException_ClientNotInitialized);

    /// <summary>
    /// Bootstraps and starts the application.
    /// </summary>
    /// <param name="provider">An <see cref="IServiceProvider"/> to configure the application.</param>
    public async Task RunAsync(IServiceProvider provider)
    {
        LogIfEnabled(LogLevel.Information, TraceMessages.LanguageClientStarting);

        //ExternalServiceProvider = provider;
        await StartLanguageClientAsync();
    }

    /// <summary>
    /// Gets information about this LSP client application and its configuration.
    /// </summary>
    /// <remarks>
    /// 🧩 The base implementation returns the <c>Name</c> and <c>Version</c> of the executing <see cref="Assembly"/>,
    /// which is everything <see cref="ClientInfo"/> needs.
    /// </remarks>
    protected virtual ClientInfo GetClientInfo()
    {
        var assemblyName = Assembly.GetEntryAssembly()?.GetName();
        return new()
        {
            Name = assemblyName?.Name ?? "RDCore.CustomLanguageClientApp",
            Version = (assemblyName?.Version ?? new Version()).ToString(3)
        };
    }

    private async Task StartLanguageClientAsync()
    {
        // start the process first:
        serverProcess.Start();

        // by the time we're configured on this side, the server pipe should be ready:
        ServerToken = new CancellationTokenSource();
        Client = await OmniSharpLanguageClient.From(ConfigureClient, ServerToken.Token);
    }

    private void HandleUnhealthyServer()
        // server process died: start a new one and monitor it:
        => StartLanguageClientAsync().RunSynchronously();

    protected abstract ClientCapabilities ConfigureClientCapabilities(ClientCapabilities capabilities);

    protected abstract void Dispose(bool disposing);

    public void Dispose()
    {
        ServerToken?.Dispose();
        Client?.Dispose();
        transportLayer?.Dispose();

        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void ConfigureClient(LanguageClientOptions options)
    {
        transportLayer.ConfigureClient(options);
        options
            // basic client app information:
            .WithClientInfo(GetClientInfo())
            // wire-up lifecycle delegates:
            .OnStarted(OnLanguageClientStartedAsync)
            .OnInitialize(OnLanguageClientInitializeAsync)
            .OnInitialized(OnLanguageClientInitializedAsync);

        var services = options.Services;
        services.AddSingleton<ILanguageClientFacade>(provider => Client!);

        // everything else the app wants to do:
        ConfigureServices(services);
        ConfigureHandlers(new RDCoreLanguageClientHandlersConfigurationBuilder(options));
        LogIfEnabled(LogLevel.Trace, TraceMessages.LanguageClientConfigurationCompleted);
    }

    /// <summary>
    /// Configures services with the OmniSharp service collection.
    /// </summary>
    /// <param name="services">The OmniSharp internal service collection.</param>
    protected abstract void ConfigureServices(IServiceCollection services);

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
    /// Gives your class or handler an opportunity to interact with the <see cref="ILanguageClient" /> after the connection has been established.
    /// </summary>
    /// <remarks>
    /// 🧩 The base implementation simply logs handler completion at <c>Trace</c> level.
    /// </remarks>
    protected async virtual Task OnLanguageClientStartedAsync(ILanguageClient client, CancellationToken token)
        => LogIfEnabled(LogLevel.Trace, TraceMessages.LanguageClientStarted_HandlerCompleted);

    /// <summary>
    /// Signals the completion of the <c>Initialize</c> request handler.
    /// <br/>👉 <em>Gives your class or handler an opportunity to interact with the <see cref="InitializeParams" /> before it is sent to the server</em>.
    /// </summary>
    /// <param name="client">The LSP <em>language client</em>.</param>
    /// <param name="request">The <c>Initialize</c> request payload.</param>
    /// <param name="token">A <see cref="CancellationToken"/> for cooperative cancellation.</param>
    /// <remarks>
    /// 🧩 This method is invoked at the end of the <em>initialization handshake</em>; 
    /// the base implementation logs handler completion at <c>Trace</c> level.
    /// </remarks>
    protected async virtual Task OnLanguageClientInitializeAsync(ILanguageClient client, InitializeParams request, CancellationToken token)
        => LogIfEnabled(LogLevel.Trace, TraceMessages.LanguageClientInitialize_HandlerCompleted);

    /// <summary>
    /// Gives your class or handler an opportunity to interact with the <see cref="InitializeParams" /> before it is sent to the server.
    /// </summary>
    /// <param name="client">The LSP <em>language client</em>.</param>
    /// <param name="request">The <c>Initialize</c> request payload.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> for cooperative cancellation.</param>
    protected async Task HandleLanguageClientInitializeAsync(ILanguageClient client, InitializeParams request, CancellationToken cancellationToken)
    {
        if (request.ProcessId.HasValue)
        {
            if (request.ProcessId.Value <= int.MaxValue)
            {
                healthCheckService.Start(Convert.ToInt32(request.ProcessId.Value), HandleUnhealthyServer);
            }
            else
            {
                LogIfEnabled(LogLevel.Warning, TraceMessages.InitializeClientProcessIdOutOfRange);
            }

        }
        await OnLanguageClientInitializeAsync(client, request, cancellationToken);
    }

    /// <summary>
    /// Signals the completion of the <c>Initialized</c> notification handler.
    /// <br/>👉 <em>Gives your class or handler an opportunity to interact with the <see cref="InitializeParams" /> and <see cref="InitializeResult" /> before it is processed by the client</em>.
    /// </summary>
    /// <remarks>
    /// 🧩 The base implementation logs handler completion at <c>Trace</c> level.
    /// </remarks>
    protected async virtual Task OnLanguageClientInitializedAsync(ILanguageClient client, InitializeParams request, InitializeResult response, CancellationToken cancellationToken)
        => LogIfEnabled(LogLevel.Trace, TraceMessages.LanguageClientInitialized_HandlerCompleted);

    /// <summary>
    /// Gives your class or handler an opportunity to interact with the <see cref="InitializeParams" /> and <see cref="InitializeResult" /> before it is processed by the client.
    /// </summary>
    protected async Task HandleLanguageClientInitializedAsync(ILanguageClient client, InitializeParams request, InitializeResult response, CancellationToken cancellationToken)
        => await OnLanguageClientInitializedAsync(client, request, response, cancellationToken);

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
