using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using OmniSharp.Extensions.LanguageServer.Server;
using RDCore.SDK.Server.Handlers;
using RDCore.SDK.Server.Handlers.Lifecycle;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Server.Services.States;
using System.IO.Pipelines;
using System.IO.Pipes;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace RDCore.SDK.Server;

public record class LanguageServerAppOptions(string PipeName) { }

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
    ILogger<LanguageServerApp> logger) : ILanguageServerApp
{
    private OmniSharpLanguageServer? Server { get; set; } = default;

    private NamedPipeServerStream NamedPipeServerStream { get; set; } = default!;
    private Task? WaitForClientConnectionTask { get; set; }

    public ILanguageServer LanguageServer => Server ?? throw new InvalidOperationException("Language server has not been initialized.");

    public async Task RunAsync(IServiceProvider provider)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogInformation("Starting language server...");
        }
        Server = await OmniSharpLanguageServer.From(ConfigureServer, provider, serverStateProvider.ProcessToken);
        logger.LogInformation("✅ Language server is connected and ready.");

        await Server.WaitForExit;
        logger.LogInformation("✅ RunAsync completed; process will now exit.");
    }

    protected abstract void Dispose(bool disposing);

    public void Dispose()
    {
        if (NamedPipeServerStream is IDisposable disposableStream)
        {
            disposableStream.Dispose();
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
        var pipeName = appSettings.Value.PipeName;
        NamedPipeServerStream = new NamedPipeServerStream(pipeName, PipeDirection.InOut,
            serverStateProvider.Options.MaximumInstances,
            PipeTransmissionMode.Byte,
            System.IO.Pipes.PipeOptions.Asynchronous |
            System.IO.Pipes.PipeOptions.CurrentUserOnly);

        options
            .WithInput(PipeReader.Create(NamedPipeServerStream))
            .WithOutput(PipeWriter.Create(NamedPipeServerStream));

        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("⏳ Named pipe '{pipeName}' initialized; asynchronously awaiting client connection...", appSettings.Value.PipeName);
        }
        WaitForClientConnectionTask = NamedPipeServerStream.WaitForConnectionAsync(serverStateProvider.ProcessToken);

        options
            .WithServerInfo(serverStateProvider.ServerInfo)
            .WithHandler<ExitHandler>()
            .WithHandler<ShutdownHandler>()
            .WithHandler<SetTraceHandler>()
            .WithHandler<ExecuteCommandHandler>()
            
            // TODO add the rest of LSP 3.17 handlers...

            .OnStarted(OnLanguageServerStartedAsync)
            .OnInitialize(OnLanguageServerInitializeAsync)
            .OnInitialized(OnLanguageServerInitializedAsync)
            ;

        options.WithServices(services =>
        {
            services.AddLogging(builder =>
            {
                builder.AddLanguageProtocolLogging();
            });
        });

        logger.LogInformation("✅ ConfigureServer completed.");
    }

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
