using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Shared;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Platform;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Handlers;
using RDCore.SDK.Server.Handlers.Lifecycle;
using RDCore.SDK.Server.Services;
using System.IO.Pipelines;
using System.IO.Pipes;
using System.Reflection;
using OmniSharpLanguageClient = OmniSharp.Extensions.LanguageServer.Client.LanguageClient;
namespace RDCore.SDK.Client;

/// <summary>
/// A client-side (LSP) RDCore app.
/// </summary>
public interface IRDCoreClientApp : IRDCoreApp
{
    Task<TResult> SendRequestAsync<TParams, TResult>(TParams request, CancellationToken token) where TParams : IRequest<TResult>;
    Task SendNotificationAsync<TParams>(TParams notification, CancellationToken token) where TParams: IRequest;
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
public abstract class RDCoreClientApp : IRDCoreClientApp
{
    private readonly IOptions<SdkAppOptions> _options;
    private readonly IRDCoreServerProcess _serverProcess;
    private readonly IHealthCheckService<RDCoreClientApp> _healthCheckService;
    private readonly ILanguageServerProtocolTransportLayer _transportLayer;
    private readonly ILogger<RDCoreClientApp> _logger;

    protected RDCoreClientApp(
        IOptions<SdkAppOptions> options,
        IRDCoreServerProcess serverProcess,
        IHealthCheckService<RDCoreClientApp> healthCheckService,
        ILanguageServerProtocolTransportLayer transportLayer,
        ILogger<RDCoreClientApp> logger)
    {
        _options = options;
        _serverProcess = serverProcess;
        _healthCheckService = healthCheckService;
        _transportLayer = transportLayer;
        _logger = logger;

        PipeName = $"RDCore.{PlatformComponent}.Pipe.{Random.Shared.NextInt64()}";
    }

    /// <summary>
    /// The type of platform client.
    /// </summary>
    /// <remarks>
    /// If <see cref="CoreServerComponent.Extension"/>, <see cref="ExtensionInfo"/> should not be <c>null</c>.
    /// </remarks>
    public abstract CoreServerComponent PlatformComponent { get; }
    /// <summary>
    /// The extension manifest for this <see cref="CoreServerComponent.Extension"/> component.
    /// </summary>
    public ExtensionInfo? ExtensionInfo { get; init; }

    private CancellationTokenSource? ServerToken { get; set; }
    private string PipeName { get; }
    private OmniSharpLanguageClient? Client { get; set; }
    //private IServiceProvider? ExternalServiceProvider { get; set; }

    public async Task<TResult> SendRequestAsync<TParams, TResult>(TParams request, CancellationToken token) where TParams : IRequest<TResult> 
        => await Client!.SendRequest(request, token);

    public Task SendNotificationAsync<TParams>(TParams notification, CancellationToken token) where TParams : IRequest
    {
        token.ThrowIfCancellationRequested();
        Client!.SendNotification(notification);
        return Task.CompletedTask;
    }

    protected async virtual Task BeforeRunAsync(string[] args) { }

    /// <summary>
    /// Bootstraps and starts the application.
    /// </summary>
    /// <param name="provider">An <see cref="IServiceProvider"/> to configure the application.</param>
    public async Task RunAsync(IServiceProvider provider, string[] args)
    {
        LogIfEnabled(LogLevel.Information, TraceMessages.LanguageClientStarting);
        await BeforeRunAsync(args);

        //ExternalServiceProvider = provider;
        await StartLanguageClientAsync(provider.GetRequiredService<IPlatformCompositionService>());
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
            Version = (assemblyName?.Version ?? new Version()).ToString(3),
        };
    }

    private NamedPipeClientStream? _namedPipe;

    private IPlatformCompositionService? _platform;
    private async Task StartLanguageClientAsync(IPlatformCompositionService platform)
    {
        _platform = platform;
        ServerToken = new CancellationTokenSource();
        var manifest = platform.GetManifest();
        var path = PlatformComponent switch
        {
            // the client app (rdc.exe) launches and connects to the language server;
            CoreServerComponent.ClientApp => manifest.LangService,
            // a server proxy owned by the language server launches and connects to a child component:
            CoreServerComponent.ParsingServer => manifest.ParseServer,
            CoreServerComponent.EnvironmentHost => manifest.HostService,
            //CoreServerComponent.Extension => fileSystem.Path.Combine(manifest.ExtensionsDirectory, ExtensionInfo!.Name),
            _ => throw new NotSupportedException($"Cannot resolve a server executable for platform component '{PlatformComponent}'.")
        };

        // start the process first:
        await _serverProcess.StartAsync(path, PipeName, ServerToken);

        // configure client-side transport:
        _namedPipe = _transportLayer.ConfigureClient(PipeName);
        await _namedPipe.ConnectAsync((int)TimeSpan.FromSeconds(30 /*_options.Value.Server.ConnectTimeoutSeconds*/).TotalMilliseconds);

        // by the time we're configured on this side, the server pipe should be ready:
        Client = await OmniSharpLanguageClient.From(ConfigureClient, ServerToken.Token);
    }

    private void HandleUnhealthyServer(IPlatformCompositionService platform)
        // server process died: start a new one and monitor it:
        => StartLanguageClientAsync(platform).RunSynchronously();

    protected abstract ClientCapabilities ConfigureClientCapabilities(ClientCapabilities capabilities);

    protected abstract void Dispose(bool disposing);

    public void Dispose()
    {
        ServerToken?.Dispose();
        Client?.Dispose();
        _namedPipe?.Dispose();

        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual ClientCapabilities GetClientCapabilities() => new();

    protected virtual InitializeParams CreateInitializationParams() => new()
    {
        ClientInfo = GetClientInfo(),
        Capabilities = GetClientCapabilities(),
        RootUri = _options.Value.Workspace.WorkspaceUri,
        ProcessId = Environment.ProcessId,
        Locale = Thread.CurrentThread.CurrentUICulture.Name,
        Trace = _options.Value.Server.TraceLevel == LogLevel.None ? InitializeTrace.Off
            : _options.Value.Server.Verbose ? InitializeTrace.Verbose : InitializeTrace.Messages,
    };

    private void ConfigureClient(LanguageClientOptions options)
    {
        options
            .WithInput(PipeReader.Create(_namedPipe!))
            .WithOutput(PipeWriter.Create(_namedPipe!))
            // basic client app information:
            .WithClientInfo(GetClientInfo())
            .WithClientCapabilities(GetClientCapabilities())
            // wire-up lifecycle delegates:
            .OnStarted(OnLanguageClientStartedAsync)
            .OnInitialize(HandleLanguageClientInitializeAsync)
            .OnInitialized(HandleLanguageClientInitializedAsync);

        var services = options.Services;
        services.AddSingleton<ILanguageClientFacade>(provider => Client!);

        // everything else the app wants to do:
        ConfigureServices(services);
        ConfigureHandlers(new RDCoreLanguageClientHandlersConfigurationBuilder(options));
        LogIfEnabled(LogLevel.Information, TraceMessages.LanguageClientConfigurationCompleted);
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
        => LogIfEnabled(LogLevel.Information, TraceMessages.LanguageClientStarted_HandlerCompleted);

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
        => LogIfEnabled(LogLevel.Information, TraceMessages.LanguageClientInitialize_HandlerCompleted);

    /// <summary>
    /// Gives your class or handler an opportunity to interact with the <see cref="InitializeParams" /> before it is sent to the server.
    /// </summary>
    /// <param name="client">The LSP <em>language client</em>.</param>
    /// <param name="request">The <c>Initialize</c> request payload.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> for cooperative cancellation.</param>
    protected async Task HandleLanguageClientInitializeAsync(ILanguageClient client, InitializeParams request, CancellationToken cancellationToken)
    {
        if (_serverProcess.ProcessId != 0)
        {
            _healthCheckService.Start(_serverProcess.ProcessId, () => HandleUnhealthyServer(_platform!));
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
        => LogIfEnabled(LogLevel.Information, TraceMessages.LanguageClientInitialized_HandlerCompleted);

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
        if (_logger.IsEnabled(logLevel))
        {
            _logger.Log(logLevel, "{message}", message);
        }
    }
}
