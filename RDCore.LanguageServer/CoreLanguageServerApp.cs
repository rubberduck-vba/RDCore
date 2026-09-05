using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.SDK.Client;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Platform;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Server.Services.States;

namespace RDCore.LanguageServer;

/// <summary>
/// The RDCore <strong>RD-VBA Language Server</strong> application.
/// </summary>
/// <remarks>
/// 👉 This application implements a <em>Language Server Protocol (LSP)</em> <strong>server</strong> and is responsible for 
/// <strong>orchestrating communications</strong> between the IDE editor and the applications and services of the RDCore platform.
/// </remarks>
internal sealed class CoreLanguageServerApp(
    IOptions<SdkAppOptions> options,
    IServerStateProvider serverStateProvider,
    IPlatformCompositionService composition,
    IPlatformOrchestrationService orchestration,
    IExtensionsProvider extensionsProvider,
    IHealthCheckService<CoreLanguageServerApp> healthCheckService,
    ILanguageServerProtocolTransportLayer transportLayer,
    ILogger<CoreLanguageServerApp> logger)
    : RDCoreServerApp(options, serverStateProvider, healthCheckService, transportLayer, logger)
{
    public override CoreServerComponent PlatformComponent => CoreServerComponent.LanguageServer;

    /// <summary>Cancels in-flight core-component bring-up when the server is shutting down.</summary>
    private readonly CancellationTokenSource _componentsCts = new();
    private Task? _parsingServerBringUp;

    protected override async Task BeforeRunAsync(string[] args)
    {
        var platform = composition.GetManifest();
        LogIfEnabled(LogLevel.Information, "✅ Acquired platform manifest");

        orchestration.RegisterCoreComponent(factory =>
            factory.Create(CoreServerComponent.ParsingServer,
                new CorePlatformClientCapabilities
                {
                    Parsing = new ParserCapabilities
                    {
                        ParseFullDocument = new ParseFullDocument(true)
                    }
                }));
        //.RegisterCoreComponent(factory => factory.Create(CoreServerComponent.EnvironmentHost, TODO));

        LogIfEnabled(LogLevel.Information, "✅ Registered RDCore platform components");

        try
        {
            foreach (var extension in extensionsProvider.Discover())
            {
                LogIfEnabled(LogLevel.Information, $"🧩 Validating discovered platform extension: {extension.Title}...");
                orchestration.RegisterExtension(extension, factory => factory.Create(CoreServerComponent.Extension,
                    new() /*TODO provide the extension capabilities here*/));
            }
            //var loadExtensionTasks = extensionsProvider.Discover().Select(extension => Task.Run(() =>
            //{
            //    orchestration.RegisterExtension(extension, factory => factory.Create(CoreServerComponent.Extension,
            //        new() /*TODO provide the extension capabilities here*/));
            //}));
            //await Task.WhenAll(loadExtensionTasks);
            LogIfEnabled(LogLevel.Information, "✅ Registered RDCore platform extensions");
        }
        catch (Exception exception)
        {
            LogIfEnabled(LogLevel.Error, $"Platform extensions could not be loaded.\n{exception}");
        }
    }

    protected override void ConfigureHandlers(IRDCoreLSPHandlerConfigurationBuilder builder)
    {
        // TODO configure Client <=> LangServer handlers here
    }

    protected override void RegisterServerCapabilities(ILanguageServer server, ClientCapabilities clientCapabilities)
    {
        clientCapabilities.TextDocument = new()
        {
            //CallHierarchy = new(true),
            //CodeAction = new(true),
            //CodeLens = new(true),
            //ColorProvider = new(true),
            //Completion = new(true),
            Declaration = new(true),
            Definition = new(true),
            Diagnostic = new(true),
            //DocumentHighlight = new(true),
            //DocumentLink = new(true),
            DocumentSymbol = new(true),
            //FoldingRange = new(true),
            //Formatting = new(true),
            //Hover = new(true),
            //Implementation = new(true),
            //InlayHint = new(true),
            //InlineValue = new(true),
            //LinkedEditingRange = new(true),
            //Moniker = new(true),
            //OnTypeFormatting = new(true),
            //RangeFormatting = new(true),
            //References = new(true),
            //Rename = new(true),
            //SemanticTokens = new(true),
            //SignatureHelp = new(true),
            //SelectionRange = new(true),
            Synchronization = new(true),
            PublishDiagnostics = new(true),
            //TypeDefinition = new(true),
            //TypeHierarchy = new(true),
        };
        clientCapabilities.Window = new()
        {
            //ShowDocument = new(true),
            ShowMessage = new(true),
            WorkDoneProgress = new(true),
        };
        clientCapabilities.Workspace = new()
        {
            //ApplyEdit = new(true),
            Diagnostics = new(true),
            //FileOperations = new(true),
            //SemanticTokens = new(true),
            Symbol = new(true),
            //WorkspaceEdit = new(true),
            //WorkspaceFolders = new(true),
        };
    }

    protected override Task OnLanguageServerInitializeAsync(ILanguageServer server, InitializeParams request, CancellationToken cancellationToken)
    {
        LogIfEnabled(LogLevel.Information, "Received LSP/Initialize request.");
        return base.OnLanguageServerInitializeAsync(server, request, cancellationToken);
    }

    protected async override Task OnLanguageServerInitializedAsync(ILanguageServer server, InitializeParams request, InitializeResult response, CancellationToken cancellationToken)
    {
        LogIfEnabled(LogLevel.Information, "🤝 LSP initialization handshake completed");
        await base.OnLanguageServerInitializedAsync(server, request, response, cancellationToken);
    }

    protected override void OnLanguageServerStarted(ILanguageServer server)
    {
        LogIfEnabled(LogLevel.Information, "🚀 Language Server app started");

        // Bring up the core child components once the client<->LS connection is live. This runs as a
        // supervised background task (not awaited): a child that is slow or fails to attach must not
        // block or fault the language server. Exceptions are logged here; the connection state machine
        // will later consume these as component state transitions.
        _parsingServerBringUp = BringUpCoreComponentAsync("parsing server", orchestration.ParsingService, _componentsCts.Token);

        // TODO some ParsingClientService should be responsible for caching ASTs.
    }

    /// <summary>
    /// Launches and connects a core child component via its <see cref="RDCore.SDK.Client.IRDCoreClientApp"/> proxy,
    /// isolating any failure from the language server's own lifecycle.
    /// </summary>
    private async Task BringUpCoreComponentAsync(string label, IRDCoreClientApp component, CancellationToken token)
    {
        try
        {
            // The proxy derives its own process arguments (owner PID, generated pipe name, workspace)
            // from configuration in RDCoreServerProcess, so no command-line arguments are passed here.
            // ExternalServices (not the OmniSharp internal container) is where IPlatformCompositionService lives.
            await component.RunAsync(ExternalServices, []);
            LogIfEnabled(LogLevel.Information, $"✅ Connected to {label}");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            LogIfEnabled(LogLevel.Information, $"Bring-up of {label} was cancelled; language server is shutting down.");
        }
        catch (Exception exception)
        {
            LogIfEnabled(LogLevel.Error, $"❌ Failed to bring up {label}:\n{exception}");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        _componentsCts.Cancel();
        try
        {
            _parsingServerBringUp?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception exception)
        {
            LogIfEnabled(LogLevel.Warning, $"Core-component bring-up did not settle cleanly on shutdown:\n{exception}");
        }
        _componentsCts.Dispose();
    }
}
