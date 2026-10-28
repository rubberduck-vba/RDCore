using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using RDCore.LanguageServer.Completion;
using RDCore.LanguageServer.Refactoring;
using RDCore.LanguageServer.Debugging;
using RDCore.LanguageServer.Diagnostics;
using RDCore.LanguageServer.Folding;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Progress;
using RDCore.LanguageServer.Runtime;
using RDCore.LanguageServer.SemanticTokens;
using RDCore.LanguageServer.Server;
using RDCore.LanguageServer.Server.Handlers.Document;
using RDCore.LanguageServer.Server.Handlers.Workspace;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Workspace;
using RDCore.SDK.Client;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Formatting;
using RDCore.SDK.Platform;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Progress;
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
    IWorkspaceDocumentService workspaceDocuments,
    IWorkspaceProjects projects,
    IParsingClientService parsing,
    ISymbolSyncService symbolSync,
    IDocumentDiagnosticsService diagnostics,
    IDiagnosticsPublisher publisher,
    IClientInteractionRelay interaction,
    IWorkProgressReporter progress,
    IWorkspaceRefresh refresh,
    ILogger<CoreLanguageServerApp> logger)
    : RDCoreServerApp(options, serverStateProvider, healthCheckService, transportLayer, logger)
{
    public override CoreServerComponent PlatformComponent => CoreServerComponent.LanguageServer;

    protected override TextDocumentSelector DocumentSelector => SupportedLanguages.DocumentSelector;

    /// <summary>
    /// Cancels in-flight core-component bring-up when the server is shutting down.
    /// </summary>
    private readonly CancellationTokenSource _componentsCts = new();
    private readonly List<Task> _coreComponentBringUps = [];

    protected override async Task BeforeRunAsync(string[] args)
    {
        var platform = composition.GetManifest();
        LogIfEnabled(LogLevel.Information, "✅ Acquired platform manifest");

        orchestration.RegisterCorePlatformComponents();

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
        builder.WithHandler<WorkspaceDiagnosticHandler>();
        builder.WithHandler<DocumentSymbolHandler>();
        builder.WithHandler<FoldingRangeHandler>();
        builder.WithHandler<SelectionRangeHandler>();
        builder.WithHandler<SemanticTokensHandler>();
        builder.WithHandler<CompletionHandler>();
        builder.WithHandler<SignatureHelpHandler>();
        builder.WithHandler<RenameHandler>();
        builder.WithHandler<PrepareRenameHandler>();
        builder.WithHandler<Navigation.DefinitionHandler>();
        builder.WithHandler<Navigation.DeclarationHandler>();
        builder.WithHandler<Navigation.ReferencesHandler>();
        builder.WithHandler<Navigation.DocumentHighlightHandler>();
        builder.WithHandler<Navigation.TypeDefinitionHandler>();
        builder.WithHandler<Navigation.ImplementationHandler>();
        builder.WithHandler<Navigation.CallHierarchyPrepareHandler>();
        builder.WithHandler<Navigation.CallHierarchyIncomingHandler>();
        builder.WithHandler<Navigation.CallHierarchyOutgoingHandler>();
        builder.WithHandler<Navigation.TypeHierarchyPrepareHandler>();
        builder.WithHandler<Navigation.TypeHierarchySupertypesHandler>();
        builder.WithHandler<Navigation.TypeHierarchySubtypesHandler>();
        builder.WithHandler<WorkspaceSymbolHandler>();
        builder.WithHandler<Navigation.HoverHandler>();
        builder.WithHandler<CodeActionHandler>();
        builder.WithHandler<Server.Handlers.Workspace.ExecuteCommandHandler>();
        builder.WithHandler<InlayHintHandler>();
        builder.WithHandler<CodeLensHandler>();
        builder.WithHandler<SessionStatusHandler>();
        builder.WithHandler<SessionExecuteHandler>();
        builder.WithHandler<SessionDiscardHandler>();
        builder.WithHandler<SessionDebugResumeHandler>();
        builder.WithHandler<SessionDebugPauseHandler>();
        builder.WithHandler<SessionDebugGotoHandler>();
        builder.WithHandler<SessionDebugTerminateHandler>();
        builder.WithHandler<SessionDebugBreakpointsHandler>();
        builder.WithHandler<SessionDebugStackHandler>();
        builder.WithHandler<SessionDebugVariablesHandler>();
        builder.WithHandler<SessionDebugEvaluateHandler>();
        builder.WithHandler<SessionDebugExecuteHandler>();
        builder.WithHandler<SessionAnalyzeHandler>();
        builder.WithHandler<SessionCompletionHandler>();
        builder.WithHandler<SessionSignatureHelpHandler>();
        builder.WithHandler<SessionSemanticTokensHandler>();
        builder.WithHandler<SessionCommandsHandler>();
        builder.WithHandler<SessionPeekHandler>();
        builder.WithHandler<SessionPokeHandler>();

        // the lifecycle of the documents a client has open.
        builder.WithHandler<DidOpenTextDocumentHandler>();
        builder.WithHandler<DidChangeTextDocumentHandler>();
        builder.WithHandler<WillSaveTextDocumentHandler>();
        builder.WithHandler<WillSaveWaitUntilTextDocumentHandler>();
        builder.WithHandler<DidSaveTextDocumentHandler>();
        builder.WithHandler<DidCloseTextDocumentHandler>();

        // and the project file, which the client says it rewrote: the workspace is made what it lists now.
        builder.WithHandler<ProjectFileWatchHandler>();

        // and the files it created, renamed or deleted: the server writes the project file, of which it is the only writer.
        builder.WithHandler<DidCreateWorkspaceFilesHandler>();
        builder.WithHandler<DidRenameWorkspaceFilesHandler>();
        builder.WithHandler<DidDeleteWorkspaceFilesHandler>();
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        // the handlers are built by the OmniSharp container; the services they need live in the
        // external (host) container, so bridge the same singletons across.
        services.AddSingleton(_ => ExternalServices.GetRequiredService<IDocumentDiagnosticsService>());
        services.AddSingleton(_ => ExternalServices.GetRequiredService<IParsingClientService>());
        services.AddSingleton(_ => ExternalServices.GetRequiredService<ISemanticTokensService>());
        services.AddSingleton(_ => ExternalServices.GetRequiredService<ICompletionService>());
        services.AddSingleton(_ => ExternalServices.GetRequiredService<IRenameService>());
        services.AddSingleton(_ => ExternalServices.GetRequiredService<IClientVerbRegistry>());
        services.AddSingleton(_ => ExternalServices.GetRequiredService<IWorkProgressReporter>());
        services.AddSingleton(_ => ExternalServices.GetRequiredService<IWorkspaceDocumentService>());
        services.AddSingleton(_ => ExternalServices.GetRequiredService<IWorkspaceProjects>());
        services.AddSingleton(_ => ExternalServices.GetRequiredService<Workspace.States.IDocumentStateProvider>());
        services.AddSingleton(_ => ExternalServices.GetRequiredService<ISymbolResolver>());
        services.AddSingleton(_ => ExternalServices.GetRequiredService<IPlatformOrchestrationService>());
        services.AddSingleton(_ => ExternalServices.GetRequiredService<IProgramDebugService>());
        services.AddSingleton(_ => ExternalServices.GetRequiredService<ISymbolSyncService>());
        services.AddSingleton(_ => ExternalServices.GetRequiredService<IWorkspaceReloadService>());
        services.AddSingleton(_ => ExternalServices.GetRequiredService<IWorkspaceFileOperationService>());
        services.AddSingleton(_ => ExternalServices.GetRequiredService<IWatchedFileChanges>());
        services.AddSingleton(_ => ExternalServices.GetRequiredService<IOptions<SdkAppOptions>>());
        services.AddSingleton(_ => ExternalServices.GetRequiredService<IDocumentLifecycleService>());
        services.AddSingleton(_ => ExternalServices.GetRequiredService<IProjectFileWriter>());
        services.AddSingleton(_ => ExternalServices.GetRequiredService<IProjectFileLoader>());
        services.AddSingleton(_ => ExternalServices.GetRequiredService<RDCore.SDK.Runtime.Libraries.ILibrarySource>());
        services.AddSingleton(_ => ExternalServices.GetRequiredService<System.IO.Abstractions.IFileSystem>());

        // the commands a client asks the server to run (workspace/executeCommand).
        services.AddSingleton<RDCore.SDK.Server.Commands.IServerCommand, Workspace.ImportProjectCommand>();
        services.AddSingleton<RDCore.SDK.Server.Commands.IServerCommand, Workspace.ImportIndenterSettingsCommand>();
    }

    protected override void RegisterServerCapabilities(ILanguageServer server, ClientCapabilities clientCapabilities)
        => ClientCapabilityDefaults.Complete(clientCapabilities);

    protected override async Task OnLanguageServerInitializeAsync(ILanguageServer server, InitializeParams request, CancellationToken cancellationToken)
    {
        // a client that pulls diagnostics is not pushed them as well: it would show each finding twice.
        publisher.Attach(server, enabled: request.Capabilities?.TextDocument?.Diagnostic is null);
        // what the client shows of each document is refreshed once the workspace changed under it, as the client says it is asked to.
        refresh.Attach(server);
        await LoadWorkspaceAsync(server, request);
        await base.OnLanguageServerInitializeAsync(server, request, cancellationToken);
    }

    /// <summary>
    /// Loads the project file and its documents. Runs here because the server is still
    /// <c>Initializing</c> — the window <see cref="IWorkspaceService.LoadAsync"/> requires. A load
    /// failure is not fatal: the server still completes the LSP handshake, and tells the client why
    /// (<c>window/showMessage</c>, which LSP allows before <c>initialize</c> is answered).
    /// </summary>
    private async Task LoadWorkspaceAsync(ILanguageServer server, InitializeParams request)
    {
        // the folders a client gives (LSP 3.17 workspaceFolders); the server is started on the first, or on rootUri when it gives none.
        var folders = (request.WorkspaceFolders ?? []).Select(folder => folder.Uri.GetFileSystemPath()).ToList();
        var root = request.RootUri?.GetFileSystemPath() ?? folders.FirstOrDefault();
        if (root is null)
        {
            LogIfEnabled(LogLevel.Warning, "No RootUri in the initialize request; workspace will not be loaded.");
            return;
        }

        try
        {
            await workspace.LoadAsync(root, folders);
            LogIfEnabled(LogLevel.Information, "✅ Workspace loaded");
        }
        catch (Exception exception)
        {
            LogIfEnabled(LogLevel.Error, $"❌ Workspace could not be loaded:\n{exception}");

            // a project file that is not JSON, or is missing, otherwise only showed later, as a didOpen error that did not say what was wrong (HexIDE's
            // findings desk #26).
            server.Window.ShowMessage(new ShowMessageParams
            {
                Type = MessageType.Error,
                Message = string.Format(DebuggerMessages.WorkspaceCouldNotBeLoaded, root, exception.Message),
            });
        }

        // a project that lists a file no workspace folder holds does not have it: the client is told which, and why.
        foreach (var (project, relativeUri) in projects.Refused)
        {
            server.Window.ShowMessage(new ShowMessageParams
            {
                Type = MessageType.Warning,
                Message = string.Format(DebuggerMessages.FileOutsideTheWorkspaceFolders, relativeUri, project),
            });
        }
    }

    protected async override Task OnLanguageServerInitializedAsync(ILanguageServer server, InitializeParams request, InitializeResult response, CancellationToken cancellationToken)
    {
        LogIfEnabled(LogLevel.Information, "🤝 LSP initialization handshake completed");

        // workspace/didRenameFiles is read by a handler of the platform's own, which OmniSharp does not state the server has.
        response.Capabilities.Workspace ??= new();
        response.Capabilities.Workspace.FileOperations ??= new();
        response.Capabilities.Workspace.FileOperations.DidRename = WorkspaceFileOperationFilters.DidRename;

        // the client owns the file system: one that does not say it watches files is watched for, unless the setting says never.
        if (workspaceDocuments.WorkspaceRoot is { Length: > 0 } root)
        {
            ExternalServices.GetRequiredService<IWorkspaceFileSystemWatcher>()
                .Start(root, clientWatches: request.Capabilities?.Workspace?.DidChangeWatchedFiles.IsSupported == true, server);
        }

        await base.OnLanguageServerInitializedAsync(server, request, response, cancellationToken);
    }

    protected override void OnLanguageServerStarted(ILanguageServer server)
    {
        LogIfEnabled(LogLevel.Information, "🚀 Language Server app started");

        // the dialogs of the program that runs in the environment host are relayed to this client, if it answers them.
        interaction.Attach(new LanguageServerInteractionClient(server));

        // and the progress of the work the server does is reported to it, if it shows it.
        progress.Attach(server.WorkDoneManager);

        // the indenter settings the user had in Rubberduck are offered once, to a client that can ask the question.
        _ = OfferIndenterSettingsAsync(server);

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

    // the indenter settings the user had in Rubberduck are offered once (IndenterSettingsOffering); the offering listens for the client's settings from now on.
    private async Task OfferIndenterSettingsAsync(ILanguageServer server)
    {
        try
        {
            using var offering = new Workspace.IndenterSettingsOffering(
                server, server.Services.GetRequiredService<IPlatformClientCapabilitiesService>(), ExternalServices.GetRequiredService<IndenterSettingsOffer>(),
                ExternalServices.GetRequiredService<ILiveSettings>(), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(10), message => LogIfEnabled(LogLevel.Information, message));
            if (await offering.RunAsync(_componentsCts.Token) is { } outcome)
            {
                LogIfEnabled(LogLevel.Information, $"⚙️ {outcome.Message}");
                server.Window.ShowInfo(outcome.Message);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogIfEnabled(LogLevel.Warning, $"The indenter settings of Rubberduck could not be offered: {exception.Message}");
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
        // the work the server does of its own as it brings the workspace up is reported to the client, each part of it.
        using (var parsingWork = progress.Begin(ProgressMessages.ParsingWorkspace))
        {
            await parsing.ParseWorkspaceAsync(token, parsingWork);
        }

        using (var composingWork = progress.Begin(ProgressMessages.ComposingProject))
        {
            await symbolSync.SyncWorkspaceAsync(token, composingWork);
        }

        using var analyzingWork = progress.Begin(ProgressMessages.AnalyzingWorkspace);
        await RunDiagnosticsBringUpAsync(token, analyzingWork);
    }

    /// <summary>
    /// Pulls diagnostics for every loaded document once, exercising the provider fan-out without an
    /// editor. Not a push — nothing is sent to the client; it is the seam the future push hangs off.
    /// </summary>
    private async Task RunDiagnosticsBringUpAsync(CancellationToken token, IProgress<WorkProgressStep>? progress = null)
    {
        try
        {
            var total = 0;
            var all = workspaceDocuments.GetAllDocuments().ToList();
            for (var index = 0; index < all.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var uri = all[index].Id.Uri.ToUri();
                progress?.Report(new WorkProgressStep(string.Format(ProgressMessages.ItemOf, all[index].Name, index + 1, all.Count), 100 * index / all.Count));
                var result = await diagnostics.GetAsync(uri, previousResultId: null, token);
                total += result.Diagnostics.Count;
                LogIfEnabled(LogLevel.Information, $"🔎 {uri}: {result.Diagnostics.Count} diagnostic(s)");
            }
            LogIfEnabled(LogLevel.Information, $"✅ Diagnostics bring-up completed ({total} across the workspace)");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            LogIfEnabled(LogLevel.Information, "Diagnostics bring-up was cancelled; language server is shutting down.");
        }
        catch (Exception exception)
        {
            LogIfEnabled(LogLevel.Error, $"❌ Diagnostics bring-up failed.\n{exception}");
        }
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

    // what the client said is said to every component: each asks the language server, which answers with what the client said. A client that pulls its
    // diagnostics is asked to pull them again - a diagnostic's settings are among the live ones - and what else it shows of each document refreshed.
    protected override async Task OnLiveSettingsChangedAsync(ILanguageServer server)
    {
        var components = new[] { orchestration.ParsingService, orchestration.RuntimeEnvironment }
            .Concat(orchestration.Extensions)
            .Where(component => component is not null)
            .ToList();
        foreach (var component in components)
        {
            try
            {
                await component.NotifyLiveSettingsChangedAsync(_componentsCts.Token);
            }
            catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException)
            {
                // a component that is not up yet asks for the settings when it is.
                LogIfEnabled(LogLevel.Debug, $"⚙️ A component was not told the live settings changed: {exception.Message}");
            }
        }

        refresh.Changed();
    }

    protected override async Task OnServerStoppingAsync()
    {
        await _componentsCts.CancelAsync();

        // what a change of the project left to do (composing the host's session, defining the symbols) waits for components that are going away.
        ExternalServices.GetService<IWorkspaceReloadService>()?.Stop();
        ExternalServices.GetService<IWorkspaceFileSystemWatcher>()?.Dispose();
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
