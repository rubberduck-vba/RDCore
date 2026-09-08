using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using OmniSharp.Extensions.LanguageServer.Server;
using RDCore.SDK.Client;
using RDCore.SDK.Platform;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Handlers;
using RDCore.SDK.Server.Handlers.Lifecycle;
using RDCore.SDK.Server.Handlers.Platform;
using RDCore.SDK.Server.Logging;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Server.Services.States;
using System.IO;
using System.IO.Pipelines;
using System.IO.Pipes;
using System.Reflection;

using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;
namespace RDCore.SDK.Server;


/// <summary>
/// A server-side (LSP) RDCore app.
/// </summary>
public interface IRDCoreServerApp : IRDCoreApp
{
    Task<TResult> SendRequestAsync<TParams, TResult>(TParams request, CancellationToken token) where TParams : IRequest<TResult>;
    Task SendNotificationAsync<TParams>(TParams notification, CancellationToken token) where TParams : IRequest;
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
    IOptions<SdkAppOptions> options,
    IServerStateProvider serverStateProvider,
    IHealthCheckService<RDCoreServerApp> healthCheckService,
    ILanguageServerProtocolTransportLayer transportLayer,
    ILogger<RDCoreServerApp> logger) : IRDCoreServerApp
{
    protected IServerStateProvider ServerStateProvider { get; } = serverStateProvider;

    /// <summary>
    /// The <em>external</em> (host) service provider, as opposed to the <c>OmniSharp</c> language server's
    /// internal <see cref="ILanguageServer.Services"/>.
    /// </summary>
    /// <remarks>
    /// 👉 Platform services such as <see cref="Platform.IPlatformCompositionService"/> and the orchestration
    /// services are registered here, not on the internal server container. Server apps that bring up child
    /// components must pass <strong>this</strong> provider to <see cref="IRDCoreClientApp.RunAsync"/>.
    /// </remarks>
    protected IServiceProvider ExternalServices { get; private set; } = default!;

    public abstract CoreServerComponent PlatformComponent { get; }
    private OmniSharpLanguageServer? Server { get; set; }
    public async Task<TResult> SendRequestAsync<TParams, TResult>(TParams request, CancellationToken token) where TParams : IRequest<TResult>
        => await Server!.SendRequest(request, token);

    public Task SendNotificationAsync<TParams>(TParams notification, CancellationToken token) where TParams : IRequest
    {
        token.ThrowIfCancellationRequested();
        Server!.SendNotification(notification);
        return Task.CompletedTask;
    }

    private NamedPipeServerStream _namedPipe = default!;
    private bool _disposed;

    protected async virtual Task BeforeRunAsync(string[] args) { }

    public async Task RunAsync(IServiceProvider externalServiceProvider, string[] args)
    {
        ExternalServices = externalServiceProvider;
        LogIfEnabled(LogLevel.Information, TraceMessages.LanguageServerStarting);
        await BeforeRunAsync(args);

        _namedPipe = await CreateServerPipeAsync();
        await _namedPipe.WaitForConnectionAsync();
        LogIfEnabled(LogLevel.Information, TraceMessages.LanguageServerConnected);

        Server = await OmniSharpLanguageServer.From(ConfigureServer, externalServiceProvider, ServerStateProvider.ProcessTokenSource.Token);

        var shutdownTimeout = TimeSpan.FromSeconds(Math.Max(1, options.Value.Server.ShutdownTimeoutSeconds));
        Task? stopping = null;

        using (ServerStateProvider.ProcessTokenSource.Token.Register(() =>
        {
            // shutdown was requested (owning client died, Exit handled, or fatal child fault):
            // begin tearing down any supervised children while our own server stops.
            stopping = OnServerStoppingAsync();
            Server?.ForcefulShutdown();
        }))
        {
            // WaitForExit is `_exitSubject.ToTask(...)` — a fresh Task per access — so capture it once;
            // comparing two separate reads for reference equality is always false.
            var serverExit = Server.WaitForExit;

            // block until the server actually exits (client disconnect) OR shutdown is requested (token).
            try
            {
                await serverExit.WaitAsync(ServerStateProvider.ProcessTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                // shutdown was requested; the token registration above forced the server down.
            }

            // the OmniSharp Rx pipeline does not always complete WaitForExit even after ForcefulShutdown;
            // bound the wait so it cannot hang process exit, then dispose explicitly.
            if (!serverExit.IsCompleted
                && await Task.WhenAny(serverExit, Task.Delay(shutdownTimeout)) != serverExit)
            {
                LogIfEnabled(LogLevel.Warning, "Language server did not stop within the shutdown timeout; forcing.");
            }
        }

        if (stopping is not null)
        {
            try
            {
                await stopping.WaitAsync(shutdownTimeout);
            }
            catch (Exception exception)
            {
                LogIfEnabled(LogLevel.Warning, $"Child shutdown did not settle: {exception.Message}");
            }
        }

        Server.Dispose();
        LogIfEnabled(LogLevel.Information, TraceMessages.LanguageServerWaitForExitTaskCompleted);
    }

    /// <summary>
    /// Invoked when the server has been asked to stop, concurrently with its own teardown.
    /// Override to gracefully shut down any supervised child components. Base implementation does nothing.
    /// </summary>
    protected virtual Task OnServerStoppingAsync() => Task.CompletedTask;

    private void HandleUnhealthyClient() => ServerStateProvider.ProcessTokenSource.Cancel();

    // a just-killed predecessor can leave the named pipe instance briefly reserved (MaximumInstances=1).
    private async Task<NamedPipeServerStream> CreateServerPipeAsync()
    {
        const int maxAttempts = 10;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return transportLayer.ConfigureServer();
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                LogIfEnabled(LogLevel.Warning, $"Transport pipe is busy; retrying ({attempt}/{maxAttempts}).");
                await Task.Delay(250);
            }
        }
    }

    /// <summary>
    /// Disposes of any unmanaged resources held at instance level.
    /// </summary>
    protected abstract void Dispose(bool disposing);

    /// <summary>
    /// Disposes of any unmanaged resources held at instance level.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (transportLayer is IDisposable disposableTransport)
        {
            disposableTransport.Dispose();
        }
        if (_namedPipe is IDisposable disposablePipe)
        {
            disposablePipe.Dispose();
        }
        if (Server is IDisposable disposableServer)
        {
            disposableServer.Dispose();
        }

        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void ConfigureServer(LanguageServerOptions serverOptions)
    {
        serverOptions
            .WithInput(PipeReader.Create(_namedPipe))
            .WithOutput(PipeWriter.Create(_namedPipe))
            // basic server app information:
            .WithServerInfo(GetServerInfo())
            // wire-up lifecycle delegates:
            .OnStarted(HandleLanguageServerStartedAsync)
            .OnInitialize(HandleLanguageServerInitializeAsync)
            .OnInitialized(HandleLanguageServerInitializedAsync)
            // core SDK handlers:
            .ConfigureCoreSdkHandlers();

        // everything else the app wants to do:
        ConfigureHandlers(new RDCoreLanguageServerHandlersConfigurationBuilder(serverOptions));

        serverOptions.WithServices(services =>
        {
            services.AddScoped<ILanguageServerFacade>(provider => Server!);
            services.AddSingleton(new PlatformComponentContext(PlatformComponent));

            // bridge the configured server options into the OmniSharp-internal container: its own
            // AddOptions() would otherwise hand handlers (and the scrubbing logger below) a default,
            // unconfigured SdkServerOptions. a closed-type registration wins over the open generic.
            services.AddSingleton<IOptions<SdkServerOptions>>(Options.Create(options.Value.Server));

            services.AddLogging(builder =>
            {
                // NOT AddLanguageProtocolLogging(): OmniSharp's protocol logger appends a caught
                // exception's full ToString() to the window/logMessage it forwards, leaking the build
                // machine's absolute source paths on a PDB build. Forward the same records with every
                // message scrubbed, at the operator's WireErrorDetail.
                builder.Services.AddSingleton<ILoggerProvider, ScrubbingLanguageServerLoggerProvider>();
            });

            // app-specific service registrations:
            ConfigureServices(services);
        });

        LogIfEnabled(LogLevel.Information, TraceMessages.LanguageServerConfigurationCompleted);
    }

    /// <summary>
    /// Configures services with the <c>OmniSharp</c> language server's <strong>internal</strong> service collection.
    /// </summary>
    /// <remarks>
    /// 🧩 Register here the dependencies required by any handler configured in <see cref="ConfigureHandlers"/>.
    /// The base implementation does nothing.
    /// </remarks>
    /// <param name="services">The <c>OmniSharp</c> language server's internal service collection.</param>
    protected virtual void ConfigureServices(IServiceCollection services) { }

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
    /// <item><see cref="PlatformInitializeHandler"/></item>
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
    protected virtual void OnLanguageServerStarted(ILanguageServer server) 
    {
        LogIfEnabled(LogLevel.Information, "✅ Language server started.");
    }

    private async Task HandleLanguageServerInitializeAsync(ILanguageServer server, InitializeParams request, CancellationToken token)
    {
        LogIfEnabled(LogLevel.Information, "Received LSP/Initialize request.");
        ServerStateProvider.OnInitialize();
        
        if (options.Value.Server.ClientProcessId != 0)
        {
            healthCheckService.Start(options.Value.Server.ClientProcessId, HandleUnhealthyClient);
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
        LogIfEnabled(LogLevel.Information, TraceMessages.LanguageServerInitialize_HandlerCompleted);
    }

    /// <summary>
    /// Gives your class or handler an opportunity to interact with the <see cref="InitializeParams" /> before it is processed by the server
    /// </summary>
    /// <remarks>
    /// 🧩 The base implementation simply logs handler completion at <c>Trace</c> level.
    /// This method runs <strong>after server capabilities registration </strong>.
    /// </remarks>
    protected virtual async Task OnLanguageServerInitializeAsync(ILanguageServer server, InitializeParams request, CancellationToken cancellationToken)
        => LogIfEnabled(LogLevel.Information, TraceMessages.LanguageServerInitialize_HandlerCompleted);

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
        => LogIfEnabled(LogLevel.Information, TraceMessages.LanguageServerInitialized_HandlerCompleted);

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
        .WithHandler<PlatformInitializeHandler>();
}
