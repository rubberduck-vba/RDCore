using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Client.Connection;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Platform;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Handlers;
using RDCore.SDK.Server.Handlers.Lifecycle;
using System.Reflection;
namespace RDCore.SDK.Client;

/// <summary>
/// A client-side (LSP) RDCore app.
/// </summary>
public interface IRDCoreClientApp : IRDCoreApp
{
    Task<TResult> SendRequestAsync<TParams, TResult>(TParams request, CancellationToken token) where TParams : IRequest<TResult>;
    Task SendNotificationAsync<TParams>(TParams notification, CancellationToken token) where TParams: IRequest;
    /// <summary>
    /// Completes once the connection to the child server is <see cref="ConnectionStateValue.Ready"/>;
    /// faults if the connection reaches a terminal state first.
    /// </summary>
    Task WaitForReadyAsync(CancellationToken token);
    /// <summary>
    /// Completes once the connection is terminally lost — the child exited and restart-with-backoff was exhausted.
    /// </summary>
    Task WaitForTerminalAsync();
    /// <summary>
    /// Gracefully tears down the child connection: LSP <c>shutdown</c> request, <c>exit</c> notification, then a kill fallback.
    /// </summary>
    Task ShutdownAsync();
    /// <summary>
    /// The result of the <c>rdcore/platform/initialize</c> handshake; <c>null</c> until the connection is Ready.
    /// </summary>
    PlatformInitializeResult? PlatformInfo { get; }
}

/// <summary>
/// A client-side (LSP) RDCore app.
/// </summary>
/// <param name="connectionFactory">Creates the <see cref="ChildConnection"/> to the child server.</param>
/// <param name="logger">A standard logger.</param>
/// <remarks>
/// 🧩 Most RDCore apps are server-side, but if you were making an IDE or a CLI app, this would be your LSP app.
/// </remarks>
public abstract class RDCoreClientApp : IRDCoreClientApp
{
    private readonly IOptions<SdkAppOptions> _options;
    private readonly IChildConnectionFactory _connectionFactory;
    private readonly ILogger<RDCoreClientApp> _logger;

    private ChildConnection? _connection;
    private IServiceProvider? _hostServices;
    private bool _disposed;

    protected RDCoreClientApp(
        IOptions<SdkAppOptions> options,
        IChildConnectionFactory connectionFactory,
        ILogger<RDCoreClientApp> logger)
    {
        _options = options;
        _connectionFactory = connectionFactory;
        _logger = logger;
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

    /// <summary>
    /// The connection to the child server. Throws before <see cref="RunAsync"/>.
    /// </summary>
    protected ChildConnection Connection => _connection ?? throw new InvalidOperationException("The client has not started.");

    public async Task<TResult> SendRequestAsync<TParams, TResult>(TParams request, CancellationToken token) where TParams : IRequest<TResult>
        => await Connection.SendRequestAsync<TParams, TResult>(request, token);

    public Task SendNotificationAsync<TParams>(TParams notification, CancellationToken token) where TParams : IRequest
        => Connection.SendNotificationAsync(notification, token);

    public Task WaitForReadyAsync(CancellationToken token) => Connection.WaitForReadyAsync(token);

    public Task WaitForTerminalAsync() => Connection.WaitForTerminalAsync();

    public Task ShutdownAsync() => _connection?.ShutdownAsync() ?? Task.CompletedTask;

    public PlatformInitializeResult? PlatformInfo => _connection?.PlatformInfo;

    /// <summary>
    /// The platform capabilities this app expects the child to provide (sent in the platform handshake).
    /// The base implementation expects nothing; a server proxy overrides this with the LS's expectations.
    /// </summary>
    protected virtual CorePlatformClientCapabilities GetExpectedCapabilities() => new();

    protected async virtual Task BeforeRunAsync(string[] args) { }

    /// <summary>
    /// Bootstraps and starts the application.
    /// </summary>
    /// <param name="provider">An <see cref="IServiceProvider"/> to configure the application.</param>
    public async Task RunAsync(IServiceProvider provider, string[] args)
    {
        _hostServices = provider;
        LogIfEnabled(LogLevel.Information, TraceMessages.LanguageClientStarting);
        await BeforeRunAsync(args);

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

    private async Task StartLanguageClientAsync(IPlatformCompositionService platform)
    {
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

        _connection = _connectionFactory.Create();
        var startupToken = _hostServices?.GetService<IHostApplicationLifetime>()?.ApplicationStopping ?? CancellationToken.None;

        var server = _options.Value.Server;
        await _connection.ConnectAsync(new ChildConnectionRequest
        {
            ServerExecutablePath = path,
            PipeName = $"RDCore.{PlatformComponent}.Pipe.{Random.Shared.NextInt64()}",
            // the environment host is rdc.exe itself, run in host mode:
            HostMode = PlatformComponent == CoreServerComponent.EnvironmentHost,
            // ExpectedComponent is what we are connecting TO (a proxy's PlatformComponent is the child's;
            // the standalone client connects to the language server).
            ExpectedComponent = PlatformComponent == CoreServerComponent.ClientApp ? CoreServerComponent.LanguageServer : PlatformComponent,
            ExpectedCapabilities = GetExpectedCapabilities(),
            ConnectTimeoutSeconds = server.ConnectTimeoutSeconds,
            MaxRestartAttempts = server.MaxRestartAttempts,
            RestartBackoffBaseMs = server.RestartBackoffBaseMs,
            RestartBackoffMaxMs = server.RestartBackoffMaxMs,
            ShutdownTimeoutSeconds = server.ShutdownTimeoutSeconds,
            ConfigureClient = ConfigureClient,
            OnPeerExited = OnConnectionTerminated,
        }, startupToken);
    }

    /// <summary>
    /// The child server was lost and restart-with-backoff is exhausted. The base implementation stops
    /// the host application, which is correct for a standalone client (e.g. <c>rdc.exe</c>). A server
    /// that supervises the child (the language server) overrides this and escalates through its own
    /// shutdown path instead.
    /// </summary>
    protected virtual void OnConnectionTerminated()
    {
        LogIfEnabled(LogLevel.Warning, "Child server could not be restarted; stopping the client application.");
        _hostServices?.GetService<IHostApplicationLifetime>()?.StopApplication();
    }

    protected abstract ClientCapabilities ConfigureClientCapabilities(ClientCapabilities capabilities);

    protected abstract void Dispose(bool disposing);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _connection?.Dispose();

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

    /// <summary>
    /// Applies the app-specific parts of the language client configuration. The transport (input/output)
    /// and the <c>ILanguageClientFacade</c> registration are owned by <see cref="ChildConnection"/>.
    /// </summary>
    private void ConfigureClient(LanguageClientOptions options)
    {
        options
            // basic client app information:
            .WithClientInfo(GetClientInfo())
            .WithClientCapabilities(GetClientCapabilities())
            // wire-up lifecycle delegates:
            .OnStarted(OnLanguageClientStartedAsync)
            .OnInitialize(HandleLanguageClientInitializeAsync)
            .OnInitialized(HandleLanguageClientInitializedAsync);

        // everything else the app wants to do:
        ConfigureServices(options.Services);
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
    /// <item><see cref="Handlers.Platform.PlatformInitializeHandler"/></item>
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
        => await OnLanguageClientInitializeAsync(client, request, cancellationToken);

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
