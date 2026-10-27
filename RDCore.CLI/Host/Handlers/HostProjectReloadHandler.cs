using Microsoft.Extensions.Logging;
using RDCore.CLI.Host;
using RDCore.Runtime.Execution;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Workspace;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/project/reload</c>: composes the runtime session this host owns again, from the project file as it is now - what a client that
/// rewrote the <c>.rdproj</c> is given, the modules it lists now being the session's.
/// </summary>
/// <remarks>
/// The session is composed anew, as it is at start-up, and the language server defines the workspace in it again. A program that is running or waits keeps
/// the session it runs in: nothing is composed, and the result says why.
/// </remarks>
internal sealed class HostProjectReloadHandler(
    IEnvironmentSessionProvider sessionProvider,
    IProjectFileLoader projectFileLoader,
    ILogger<HostProjectReloadHandler> logger) : RDCoreRequestHandler<HostProjectReloadParams, HostProjectReloadResult>
{
    protected override async Task<HostProjectReloadResult> HandleAsync(HostProjectReloadParams request, CancellationToken token)
    {
        if (!sessionProvider.IsComposed)
        {
            return new HostProjectReloadResult();
        }

        if (sessionProvider.Execution.State is not ProgramState.Idle)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("📌 The project file changed while a program is {state}: the runtime session is kept.", sessionProvider.Execution.State);
            }

            return new HostProjectReloadResult { IsProgramRunning = true };
        }

        var root = request.WorkspaceRoot.IsFile ? request.WorkspaceRoot.LocalPath : request.WorkspaceRoot.OriginalString;
        var project = await projectFileLoader.LoadAsync(root);
        sessionProvider.Compose(project.ProjectInfo, new Uri(project.Uri));

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("🔄 Runtime session composed again from the project file: {modules} module(s).", project.ProjectInfo.Modules.Length);
        }

        return new HostProjectReloadResult { Composed = true, ModuleCount = project.ProjectInfo.Modules.Length };
    }
}
