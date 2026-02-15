using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using OmniSharp.Extensions.LanguageServer.Server;
using RDCore.Configuration;
using RDCore.Server.Handlers.Document;
using RDCore.Server.Handlers.Lifecycle;
using RDCore.Server.Handlers.Workspace;
using RDCore.Server.Services;
using RDCore.Server.States;
using RDCore.Workspace.Services;
using System.IO.Pipelines;
using System.IO.Pipes;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace RDCore.Server;

internal interface ILanguageServerApp : IDisposable
{
    ILanguageServer LanguageServer { get; }
    Task RunAsync(IServiceProvider provider);
}

internal class LanguageServerApp(
    IServerStateProvider serverStateProvider,
    IHealthCheckService healthCheckService,
    IWorkspaceService workspaceService,
    CancellationTokenSource cancellationTokenSource,
    ServerOptions options) : ILanguageServerApp
{
    private static string PipeName => "RDCore.Server";

    private OmniSharpLanguageServer? Server { get; set; } = default;
    private ILoggerFactory LoggerFactory { get; set; } = default!;

    private NamedPipeServerStream NamedPipeServerStream { get; set; } = default!;
    private Task? WaitForClientConnectionTask { get; set; }

    public ServerOptions Options { get; } = options;

    public ILanguageServer LanguageServer => Server ?? throw new InvalidOperationException("Language server has not been initialized.");

    public async Task RunAsync(IServiceProvider provider)
    {
        LoggerFactory = provider.GetRequiredService<ILoggerFactory>();
        var logger = LoggerFactory.CreateLogger<LanguageServerApp>();

        logger.LogInformation("Starting RDCore.LSP language server, connecting IO streams...");
        Server = await OmniSharpLanguageServer.From(ConfigureServer, provider, cancellationTokenSource.Token);

        logger.LogInformation("RDCore.LSP language server is ready and awaiting client initialize request.");
        await Server.WaitForExit;
    }

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
        if (LoggerFactory is IDisposable disposableLoggerFactory)
        {
            disposableLoggerFactory.Dispose();
        }
        if (Server is IDisposable disposableServer)
        {
            disposableServer.Dispose();
        }
    }

    private void ConfigureServer(LanguageServerOptions options)
    {
        NamedPipeServerStream = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 16,
            PipeTransmissionMode.Byte,
            System.IO.Pipes.PipeOptions.Asynchronous |
            System.IO.Pipes.PipeOptions.CurrentUserOnly);

        options
            .WithInput(PipeReader.Create(NamedPipeServerStream))
            .WithOutput(PipeWriter.Create(NamedPipeServerStream));

        WaitForClientConnectionTask = NamedPipeServerStream.WaitForConnectionAsync(cancellationTokenSource.Token);

        options
            .WithServerInfo(GetServerInfo())
            .WithHandler<ExitHandler>()
            .WithHandler<ShutdownHandler>()
            .WithHandler<SetTraceHandler>()
            .WithHandler<DidOpenTextDocumentHandler>()
            .WithHandler<DidCloseTextDocumentHandler>()
            .WithHandler<DidChangeTextDocumentHandler>()
            .WithHandler<ExecuteCommandHandler>()
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
    }

    private static ServerInfo GetServerInfo()
    {
        return new ServerInfo
        {
            Name = ServerApp.Info.Name!,
            Version = ServerApp.Info.Version!.ToString(3)
        };
    }

    private async Task OnLanguageServerInitializeAsync(ILanguageServer server, InitializeParams request, CancellationToken token)
    {
        // ## OnLanguageServerInitializeDelegate
        // Gives your class or handler an opportunity to interact with the InitializeParams
        // before it is processed by the server.
        serverStateProvider.OnInitialize();

        var logger = LoggerFactory.CreateLogger<LanguageServerApp>();

        if (request.ProcessId.HasValue)
        {
            // Initialize request specifies a client process ID; start monitoring the client process health
            healthCheckService.Start(() => cancellationTokenSource.Cancel(true), request.ProcessId, Options.HealthCheckIntervalSeconds);
        }
        else
        {
            logger.LogWarning("Initialize request did not specify a client process ID. Skipping client process health checks; this server instance will not automatically exit.");
        }

        // TODO - initialize server capabilities based on the workspace and client capabilities specified in the request

        if ((request.RootUri?.GetFileSystemPath() ?? request.RootPath) is string workspaceRoot)
        {
            await workspaceService.LoadAsync(workspaceRoot);
        }
        else
        {
            throw InvalidRequestException.For(request);
        }
    }

    private async Task OnLanguageServerInitializedAsync(ILanguageServer server, InitializeParams request, InitializeResult response, CancellationToken cancellationToken)
    {
        // ## OnLanguageServerInitializedDelegate
        // Gives your class or handler an opportunity to interact with the InitializeParams and InitializeResult
        // after it is processed by the server but before it is sent to the client.
        serverStateProvider.OnInitialized();
    }

    private async Task OnLanguageServerStartedAsync(ILanguageServer server, CancellationToken token)
    {
        // ## OnLanguageServerStartedDelegate
        // Gives your class or handler an opportunity to interact with the ILanguageServer
        // after the connection has been established.

        // TODO - start parsing, we're ready to receive client requests and notifications at this point.

    }
}
