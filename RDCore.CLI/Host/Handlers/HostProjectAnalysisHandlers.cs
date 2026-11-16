using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RDCore.Runtime.Support.Host;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/project/analyze</c>: composes the analysis session of a project of the workspace that the host does not run, from its project file as
/// it is now, replacing the one it had. The language server then defines the project's modules in it.
/// </summary>
/// <remarks>
/// The runtime session is never changed. Nothing is composed before the runtime session is, nor for the root the runtime session was composed under, which is
/// the project the host runs.
/// </remarks>
internal sealed class HostProjectAnalyzeHandler(
    IEnvironmentSessionProvider sessionProvider,
    IProjectAnalysisSessions analyses,
    IProjectFileLoader projectFileLoader,
    IOptions<SdkAppOptions> options,
    ILogger<HostProjectAnalyzeHandler> logger) : RDCoreRequestHandler<HostProjectAnalyzeParams, HostProjectAnalyzeResult>
{
    protected override async Task<HostProjectAnalyzeResult> HandleAsync(HostProjectAnalyzeParams request, CancellationToken token)
    {
        var root = SymbolUri.Root(request.WorkspaceRoot.IsFile ? request.WorkspaceRoot.LocalPath : request.WorkspaceRoot.OriginalString);
        if (!sessionProvider.IsComposed || sessionProvider.WorkspaceRoot is { } runRoot && SymbolUri.SameRoot(runRoot, root))
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("🔍 {root} is not analyzed apart: {reason}.", root,
                    sessionProvider.IsComposed ? "it is the project the runtime session runs" : "the runtime session is not composed yet");
            }

            return new HostProjectAnalyzeResult();
        }

        var project = await projectFileLoader.LoadOrSynthesizeAsync(root.IsFile ? root.LocalPath : root.OriginalString, options.Value.Workspace.SupportedLanguage);
        var analysis = analyses.Analyze(project.ProjectInfo, root);
        return new HostProjectAnalyzeResult { Composed = true, ModuleCount = analysis.ModuleCount };
    }
}

/// <summary>
/// Handles <c>rdcore/host/project/release</c>: lets go of the analysis session of a project the workspace does not hold. A root that has none is not an
/// error.
/// </summary>
internal sealed class HostProjectReleaseHandler(
    IProjectAnalysisSessions analyses,
    ILogger<HostProjectReleaseHandler> logger) : RDCoreRequestHandler<HostProjectReleaseParams, HostProjectReleaseResult>
{
    protected override Task<HostProjectReleaseResult> HandleAsync(HostProjectReleaseParams request, CancellationToken token)
    {
        var root = SymbolUri.Root(request.WorkspaceRoot.IsFile ? request.WorkspaceRoot.LocalPath : request.WorkspaceRoot.OriginalString);
        var released = analyses.Release(root);
        if (released && logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("🔍 The analysis session of {root} was let go of.", root);
        }

        return Task.FromResult(new HostProjectReleaseResult { Released = released });
    }
}
