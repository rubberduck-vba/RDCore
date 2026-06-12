using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Extensibility.Configuration;
using RDCore.SDK.Extensibility.Server;
using RDCore.SDK.Extensibility.Server.Handlers;
using RDCore.SDK.Extensibility.Server.Handlers.Lifecycle;
using System.Reflection;
using OmniSharpLanguageClient = OmniSharp.Extensions.LanguageServer.Client.LanguageClient;
namespace RDCore.SDK.Extensibility.Client;


/// <summary>
/// Simplifies implementing a <c>RDCore</c> <em>LSP client</em> application.
/// </summary>
/// <param name="ProcessTokenSource">A <see cref="CancellationTokenSource"/> created in the application entry point.</param>
/// <remarks>
/// 🧩 <c>override</c> templated methods to customize your application.<br/>
/// <list type="bullet">
/// <item>Implement <see cref="AppHost{TApp}.ConfigureAdditionalExternalServices"/> to supply additional services if your application needs to inject new dependencies.</item>
/// </list>
/// <c>TApp</c> is <see cref="ILanguageClientApp"/>.
/// </remarks>
public class RDCoreLanguageClientHost<TApp>(CancellationTokenSource ProcessTokenSource)
    : AppHost<TApp>(ProcessTokenSource) 
where TApp : LanguageClientApp
{
    /// <summary>
    /// Configures only the services needed to resolve the <see cref="IRDCoreApp"/> instance.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> being built this <em>application host</em>.</param>
    protected sealed override void ConfigureExternalServices(IServiceCollection services)
    {
        base.ConfigureExternalServices(services);
        services.AddSingleton<ILanguageClientApp, TApp>();
    }
}

/// <summary>
/// A <em>builder</em> that configures LSP handlers for this client app.
/// </summary>
/// <param name="Options">The LSP language client options.</param>
public class RDCoreLanguageClientHandlersConfigurationBuilder(LanguageClientOptions Options) : IRDCoreLSPHandlerConfigurationBuilder
{
    private LanguageClientOptions Options { get; } = Options;

    IRDCoreLSPHandlerConfigurationBuilder IRDCoreLSPHandlerConfigurationBuilder.WithHandler<THandler>()
    {
        Options.WithHandler<THandler>(new() { RequestProcessType = RequestProcessType.Parallel });
        return this;
    }
}

/// <summary>
/// A <see cref="IRDCoreApp"/> that is a <em>language client</em>.
/// </summary>
public interface ILanguageClientApp : IRDCoreApp
{
    /// <summary>
    /// The <em>OmniSharp</em> <see cref="ILanguageClient"/> for this application.
    /// </summary>
    ILanguageClient LanguageClient { get; }
}

/// <summary>
/// The base class for implementing a <strong>client</strong> <see cref="RDCoreApp"/>.
/// </summary>
/// <param name="options"></param>
/// <param name="logger"></param>
/// <param name="serverProcess"></param>
/// <param name="healthCheckService"></param>
/// <param name="transportLayer"></param>
public abstract class LanguageClientApp(
    IOptions<SdkServerOptions> options,
    ILogger<LanguageClientApp> logger,
    IRDCoreLanguageServerProcess serverProcess,
    IHealthCheckService<ILanguageClientApp> healthCheckService,
    ILanguageServerProtocolTransportLayer transportLayer) 
    : RDCoreApp(options, logger), ILanguageClientApp
{
    private OmniSharpLanguageClient? Client { get; set; }
    /// <summary>
    /// The <em>OmniSharp</em> <see cref="ILanguageClient"/> for this application.
    /// </summary>
    public ILanguageClient LanguageClient => Client
        ?? throw new LanguageServerProtocolSdkException(Exceptions.LanguageServerProtocolSdkException_ClientNotInitialized);

    /// <summary>
    /// Starts the application.
    /// </summary>
    protected override async Task StartAppAsync(CancellationToken token) => await StartLanguageClientAsync(token);

    /// <summary>
    /// Creates and returns a new <see cref="ClientInfo"/> encapsulting the <c>Name</c> and <c>Version</c> of this application.
    /// </summary>
    protected virtual ClientInfo GetClientInfo()
    {
        var assemblyName = Assembly.GetEntryAssembly()?.GetName();
        return new()
        {
            Name = assemblyName?.Name ?? "RDCore.CustomLanguageClientApp",
            Version = (assemblyName?.Version ?? new Version()).ToString(3)
        };
    }

    private async Task StartLanguageClientAsync(CancellationToken token)
    {
        // start the process first:
        serverProcess.Start();

        // by the time we're configured on this side, the server pipe should be ready:
        Client = await OmniSharpLanguageClient.From(lspOptions => ConfigureClient(lspOptions.Services, lspOptions), BootstrapProvider, token);
    }

    private void HandleUnhealthyServer(CancellationToken token)
    {
        // server process died: start a new one and monitor it:
        Client?.Dispose();
        StartLanguageClientAsync(token).RunSynchronously();
    }

    /// <summary>
    /// Configures the <em>capabilities</em> for this client application.
    /// </summary>
    /// <param name="capabilities">The <see cref="ClientCapabilities"/> configuration.</param>
    /// <returns>A configured <see cref="ClientCapabilities"/> instance.</returns>
    protected abstract ClientCapabilities ConfigureClientCapabilities(ClientCapabilities capabilities);

    /// <summary>
    /// Implement to dispose of any unmanaged resources.
    /// </summary>
    protected abstract void Dispose(bool disposing);
    /// <summary>
    /// Implement to dispose of any unmanaged resources.
    /// </summary>
    public sealed override void Dispose()
    {
        if (transportLayer is IDisposable disposableTransport)
        {
            disposableTransport.Dispose();
        }
        if (Client is IDisposable disposableServer)
        {
            disposableServer.Dispose();
        }

        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void ConfigureClient(IServiceCollection services, LanguageClientOptions options)
    {
        transportLayer.ConfigureClientPipe(options);
        options
            // basic client app information:
            .WithClientInfo(GetClientInfo())
            // wire-up lifecycle delegates:
            .OnStarted(OnLanguageClientStartedAsync)
            .OnInitialize(OnLanguageClientInitializeAsync)
            .OnInitialized(OnLanguageClientInitializedAsync);

        services.AddSingleton<ILanguageClientFacade>(provider => Client!)
            .AddSingleton<IRDCoreLanguageServerProcess, RDCoreLanguageServerProcess>();

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
}
