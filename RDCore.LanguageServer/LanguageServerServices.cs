using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RDCore.LanguageServer.Completion;
using RDCore.LanguageServer.Refactoring;
using RDCore.LanguageServer.Debugging;
using RDCore.LanguageServer.Diagnostics;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Runtime;
using RDCore.LanguageServer.SemanticTokens;
using RDCore.LanguageServer.Server;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.LanguageServer.Workspace.States;
using RDCore.SDK.Client;
using RDCore.SDK.Formatting;
using RDCore.SDK.Platform;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Workspace;
using System.IO.Abstractions;
using RDCore.Symbols;

namespace RDCore.LanguageServer;

/// <summary>
/// The services that make a process the platform's coordinator: the workspace, the parser and the host it talks to, and the debugging of the program that runs in it. What a
/// language server and a debug adapter have in common - they are two ways to talk to one platform.
/// </summary>
internal static class LanguageServerServices
{
    /// <summary>
    /// Registers the platform services in the <em>external</em> container.
    /// </summary>
    /// <param name="services">The container the application is built from.</param>
    /// <param name="version">The version of the application, which the workspace is checked against.</param>
    public static IServiceCollection AddPlatformCoordination(this IServiceCollection services, Version version)
    {
        services
            .AddTransient<IHealthCheckService<RDCoreServerProxy>, HealthCheckService<RDCoreServerProxy>>()
            .AddSingleton<IRDCoreServerProxyFactory, RDCoreServerProxyFactory>()
            .AddSingleton<IPlatformCompositionService, PlatformCompositionService>()
            .AddSingleton<IPlatformOrchestrationService, PlatformOrchestrationService>();

        // workspace loader: the project file, its documents, and their load-state machine.
        // IFileSystem is already registered by AppHost; the workspace services take the split
        // abstractions, so project them here.
        services
            .AddSingleton(version)
            // the language the workspace is written in, which the client says when it starts the server and again when it initializes it:
            // asked for when it is needed, never kept from before.
            .AddSingleton<Func<SupportedLanguage>>(sp => () => sp.GetRequiredService<IOptions<SdkAppOptions>>().Value.Workspace.SupportedLanguage)
            .AddSingleton<IPath>(sp => sp.GetRequiredService<IFileSystem>().Path)
            .AddSingleton<IFile>(sp => sp.GetRequiredService<IFileSystem>().File)
            .AddSingleton<IDirectory>(sp => sp.GetRequiredService<IFileSystem>().Directory)
            .AddSingleton<IProjectFileService, ProjectFileService>()
            .AddSingleton<IDocumentStateProvider, DocumentStateProvider>()
            .AddSingleton<IWorkspaceProjects, WorkspaceProjects>()
            .AddSingleton<IWorkspaceDocumentService, WorkspaceDocumentService>()
            .AddSingleton<IWorkspaceService, WorkspaceService>()
            .AddSingleton<IParsingClientService, ParsingClientService>()
            .AddSingleton<ISemanticTokensService, SemanticTokensService>()
            .AddSingleton<IClientVerbRegistry, ClientVerbRegistry>()
            .AddSingleton<RDCore.SDK.Server.Progress.IWorkProgressReporter>(_ => new RDCore.SDK.Server.Progress.WorkProgressReporter())
            // the workspace as it was composed last, which completion, signature help and rename reuse for as long as no module changed.
            .AddSingleton(_ => new RDCore.Symbols.WorkspaceCompositionCache())
            .AddSingleton<ICompletionService, CompletionService>()
            .AddSingleton<IRenameService, RenameService>()
            .AddSingleton<IDocumentDiagnosticsService, DocumentDiagnosticsService>()
            .AddSingleton<IDiagnosticsPublisher, DiagnosticsPublisher>()
            .AddSingleton<IWorkspaceRefresh>(_ => new WorkspaceRefresh(WorkspaceRefresh.Burst))
            .AddSingleton<IDocumentLifecycleService, DocumentLifecycleService>()
            // intrinsic-only type resolution until project/library symbols can be composed (Slice 4).
            .AddSingleton<RDCore.SDK.Runtime.Abstract.Execution.ISymbolResolver>(provider => new IntrinsicSymbolResolver(
                provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<RDCore.SDK.Server.Configuration.SdkAppOptions>>().Value.Environment.Is64Bit))
            .AddSingleton<IReferencedLibraryService, ReferencedLibraryService>()
            .AddSingleton<ISymbolSyncService, SymbolSyncService>()
            .AddSingleton<IWorkspaceReloadService, WorkspaceReloadService>()
            .AddSingleton<IWorkspaceFileOperationService, WorkspaceFileOperationService>()
            .AddSingleton<RDCore.LanguageServer.Server.Handlers.Workspace.IWatchedFileChanges, RDCore.LanguageServer.Server.Handlers.Workspace.WatchedFileChanges>()
            .AddSingleton<RDCore.LanguageServer.Server.Handlers.Workspace.IWorkspaceFileSystemWatcher, RDCore.LanguageServer.Server.Handlers.Workspace.WorkspaceFileSystemWatcher>()
            .AddSingleton<IProgramDebugService, ProgramDebugService>()
            .AddSingleton<IHostOutputRelay, HostOutputRelay>()
            .AddSingleton<IClientInteractionRelay, ClientInteractionRelay>()
            // the indenter settings of Rubberduck, offered once and imported on demand (rdcore.importIndenterSettings).
            .AddSingleton(provider => new IndenterSettingsOffer(
                provider.GetRequiredService<IFileSystem>(),
                IndenterSettingsOffer.MarkerIn(provider.GetRequiredService<IPlatformEnvironment>().UserDataDirectory)));
        return services;
    }

    /// <summary>
    /// Registers the two components every platform is made of: the parsing server, and the environment host that owns the runtime session.
    /// </summary>
    /// <param name="orchestration">The service that owns the platform's components.</param>
    public static IPlatformOrchestrationService RegisterCorePlatformComponents(this IPlatformOrchestrationService orchestration)
        => orchestration
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
                            DefineSymbols = new DefineSymbols(true),
                            SessionStatus = new SessionStatus(true),
                            SessionExecute = new SessionExecute(true),
                            SessionDiscard = new SessionDiscard(true),
                            ProjectReload = new ProjectReload(true),
                            ProgramDebugging = new ProgramDebugging(true),
                            SessionMemoryAccess = new SessionMemoryAccess(true),
                        }
                    }));
}
