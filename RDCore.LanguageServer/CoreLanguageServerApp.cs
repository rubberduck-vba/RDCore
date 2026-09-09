using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.LanguageServer.Diagnostics;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace.Services;
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
    IWorkspaceService workspace,
    IParsingClientService parsing,
    ISymbolSyncService symbolSync,
    ILogger<CoreLanguageServerApp> logger)
    : RDCoreServerApp(options, serverStateProvider, healthCheckService, transportLayer, logger)
{
    public override CoreServerComponent PlatformComponent => CoreServerComponent.LanguageServer;

    /// <summary>
    /// Cancels in-flight core-component bring-up when the server is shutting down.
    /// </summary>
    private readonly CancellationTokenSource _componentsCts = new();
    private readonly List<Task> _coreComponentBringUps = [];

    protected override async Task BeforeRunAsync(string[] args)
    {
        var platform = composition.GetManifest();
        LogIfEnabled(LogLevel.Information, "✅ Acquired platform manifest");

        orchestration
            .RegisterCoreComponent(factory =>
                factory.Create(CoreServerComponent.ParsingServer,
                    new CorePlatformClientCapabilities
                    {
                        Parsing = new ParserCapabilities
                        {
                            ParseFullDocument = new ParseFullDocument(true)
                        }
                    }))
            .RegisterCoreComponent(factory =>
                factory.Create(CoreServerComponent.EnvironmentHost,
                    new CorePlatformClientCapabilities
                    {
                        EnvironmentHost = new EnvironmentHostCapabilities
                        {
                            DefineSymbols = new DefineSymbols(true)
                        }
                    }));

        LogIfEnabled(LogLevel.Information, "✅ Registered RDCore platform components");

        try
        {
            foreach (var extension in extensionsProvider.Discover())
            {
                LogIfEnabled(LogLevel.Information, $"🧩 Validating discovered platform extension: {extension.Title}...");
                // nothing is gated on the extension handshake yet, so no expected capabilities.
                orchestration.RegisterExtension(extension, factory => factory.Create(CoreServerComponent.Extension,
                    new(), extensionInfo: extension));
            }
            LogIfEnabled(LogLevel.Information, "✅ Registered RDCore platform extensions");
        }
        catch (Exception exception)
        {
            LogIfEnabled(LogLevel.Error, $"Platform extensions could not be loaded.\n{exception}");
        }
    }

    protected override void ConfigureHandlers(IRDCoreLSPHandlerConfigurationBuilder builder)
    {
        builder.WithHandler<DocumentDiagnosticHandler>();
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        // the pull handler is built by the OmniSharp container; DocumentDiagnosticsService's
        // collaborators live in the external (host) container, so bridge the service across.
        services.AddSingleton<IDocumentDiagnosticsService>(omni => new DocumentDiagnosticsService(
            ExternalServices.GetRequiredService<IWorkspaceDocumentService>(),
            ExternalServices.GetRequiredService<IParsingClientService>(),
            ExternalServices.GetRequiredService<IPlatformOrchestrationService>(),
            omni.GetRequiredService<ILogger<DocumentDiagnosticsService>>()));
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

    protected override async Task OnLanguageServerInitializeAsync(ILanguageServer server, InitializeParams request, CancellationToken cancellationToken)
    {
        await LoadWorkspaceAsync(request);
        await base.OnLanguageServerInitializeAsync(server, request, cancellationToken);
    }

    /// <summary>
    /// Loads the project file and its documents. Runs here because the server is still
    /// <c>Initializing</c> — the window <see cref="IWorkspaceService.LoadAsync"/> requires. A load
    /// failure is logged, not fatal: the server still completes the LSP handshake.
    /// </summary>
    private async Task LoadWorkspaceAsync(InitializeParams request)
    {
        if (request.RootUri is null)
        {
            LogIfEnabled(LogLevel.Warning, "No RootUri in the initialize request; workspace will not be loaded.");
            return;
        }

        try
        {
            await workspace.LoadAsync(request.RootUri.GetFileSystemPath());
            LogIfEnabled(LogLevel.Information, "✅ Workspace loaded");
        }
        catch (Exception exception)
        {
            LogIfEnabled(LogLevel.Error, $"❌ Workspace could not be loaded:\n{exception}");
        }
    }

    protected async override Task OnLanguageServerInitializedAsync(ILanguageServer server, InitializeParams request, InitializeResult response, CancellationToken cancellationToken)
    {
        LogIfEnabled(LogLevel.Information, "🤝 LSP initialization handshake completed");
        await base.OnLanguageServerInitializedAsync(server, request, response, cancellationToken);
    }

    protected override void OnLanguageServerStarted(ILanguageServer server)
    {
        LogIfEnabled(LogLevel.Information, "🚀 Language Server app started");

        // Bring up the core child components once the client<->LS connection is live. Each runs as a
        // supervised background task (not awaited): a child that is slow or fails to attach must not
        // block or fault the language server.
        _coreComponentBringUps.Add(BringUpCoreComponentAsync("parsing server", orchestration.ParsingService, _componentsCts.Token));
        _coreComponentBringUps.Add(BringUpCoreComponentAsync("environment host", orchestration.RuntimeEnvironment, _componentsCts.Token));

        // once the parsing server is ready, parse every loaded workspace document and cache the ASTs,
        // then extract each module's symbols and define them in the environment host.
        _coreComponentBringUps.Add(ParseWorkspaceThenSyncSymbolsAsync(_componentsCts.Token));

        // extensions are non-essential: bring each discovered one up without escalating a failure
        // or a terminal exit to the platform.
        foreach (var extension in orchestration.Extensions)
        {
            _coreComponentBringUps.Add(BringUpExtensionAsync(extension, _componentsCts.Token));
        }
    }

    /// <summary>
    /// Launches and connects a discovered extension. Unlike a core child, an extension that fails to
    /// attach or later exits is logged and left — it must not fault or tear down the language server.
    /// </summary>
    private async Task BringUpExtensionAsync(IRDCoreClientApp extension, CancellationToken token)
    {
        var label = extension.ExtensionInfo?.Title ?? "extension";
        try
        {
            await extension.RunAsync(ExternalServices, []);
            await extension.WaitForReadyAsync(token);
            LogIfEnabled(LogLevel.Information, $"✅ {label} extension is ready");

            await extension.WaitForTerminalAsync().WaitAsync(token);
            LogIfEnabled(LogLevel.Warning, $"🧩 {label} extension has exited; the platform continues without it.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            LogIfEnabled(LogLevel.Information, $"Bring-up of {label} extension was cancelled; language server is shutting down.");
        }
        catch (Exception exception)
        {
            LogIfEnabled(LogLevel.Warning, $"🧩 {label} extension could not be brought up:\n{exception}");
        }
    }

    private async Task ParseWorkspaceThenSyncSymbolsAsync(CancellationToken token)
    {
        await parsing.ParseWorkspaceAsync(token);
        await symbolSync.SyncWorkspaceAsync(token);
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
            await component.WaitForReadyAsync(token);
            LogIfEnabled(LogLevel.Information, $"✅ {label} is ready");

            // a core child is essential: if it is lost for good, the language server cannot continue.
            await component.WaitForTerminalAsync().WaitAsync(token);
            LogIfEnabled(LogLevel.Critical, $"❌ {label} is unrecoverable; shutting down the language server.");
            ServerStateProvider.OnFatalError();
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

    protected override async Task OnServerStoppingAsync()
    {
        await _componentsCts.CancelAsync();
        // graceful LSP shutdown/exit of the child components before this process exits.
        await Task.WhenAll(
            new[] { orchestration.ParsingService, orchestration.RuntimeEnvironment }
                .Concat(orchestration.Extensions)
                .Where(component => component is not null)
                .Select(component => component.ShutdownAsync()));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // the bring-up tasks observe this token; graceful child shutdown already ran in OnServerStoppingAsync.
            _componentsCts.Cancel();
            _componentsCts.Dispose();
        }
    }
}
