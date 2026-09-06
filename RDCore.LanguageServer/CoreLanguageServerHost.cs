using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Server;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.LanguageServer.Workspace.States;
using RDCore.SDK.Platform;
using System.IO;
using System.IO.Abstractions;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Services;

namespace RDCore.LanguageServer;

/// <summary>
/// The RDCore <strong>RD-VBA Language Server</strong> application host.
/// </summary>
internal sealed class CoreLanguageServerHost() : RDCorePlatformServerHost<CoreLanguageServerApp>()
{
    protected override void ConfigureAdditionalExternalServices(IServiceCollection services, IConfiguration configuration)
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
            .AddSingleton(Info.Version ?? new Version(0, 0, 0))
            .AddSingleton(ProtocolSupportedLanguage.VBA)
            .AddSingleton<IPath>(sp => sp.GetRequiredService<IFileSystem>().Path)
            .AddSingleton<IFile>(sp => sp.GetRequiredService<IFileSystem>().File)
            .AddSingleton<IDirectory>(sp => sp.GetRequiredService<IFileSystem>().Directory)
            .AddSingleton<IProjectFileService, ProjectFileService>()
            .AddSingleton<IDocumentStateProvider, DocumentStateProvider>()
            .AddSingleton<IWorkspaceDocumentService, WorkspaceDocumentService>()
            .AddSingleton<IWorkspaceService, WorkspaceService>()
            .AddSingleton<IParsingClientService, ParsingClientService>()
            // intrinsic-only type resolution until project/library symbols can be composed (Slice 4).
            .AddSingleton<RDCore.SDK.Runtime.Abstract.Execution.ISymbolResolver, IntrinsicSymbolResolver>()
            .AddSingleton<ISymbolSyncService, SymbolSyncService>();
    }

    protected override void ConfigureExternalLogging(IServiceCollection services, ILoggingBuilder builder, IConfiguration configuration)
    {
        builder.AddFile(Path.Combine(PlatformEnvironment.Default.LogsDirectory, "RDCore.LanguageServer.log"));
        base.ConfigureExternalLogging(services, builder, configuration);
    }
}
