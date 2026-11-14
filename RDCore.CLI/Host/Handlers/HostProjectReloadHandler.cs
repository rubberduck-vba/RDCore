using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RDCore.Runtime.Support.Host;
using RDCore.Runtime.Execution;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.SDK.Platform.Components;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/project/reload</c>: composes the runtime session this host owns again, from the project file as it is now - what a client that
/// rewrote the <c>.rdproj</c> is given, the modules it lists now being the session's.
/// </summary>
/// <remarks>
/// The session is composed anew, as it is at start-up, and the language server defines the workspace in it again. A program that is running or waits keeps
/// the session it runs in: nothing is composed, and the result says why. A module the project gained meanwhile is not of the program, which is not resumed
/// while the project lists it, and is part of the session once the program is over.
/// </remarks>
internal sealed class HostProjectReloadHandler(
    IEnvironmentSessionProvider sessionProvider,
    IProjectFileLoader projectFileLoader,
    IOptions<SdkAppOptions> options,
    ILogger<HostProjectReloadHandler> logger) : RDCoreRequestHandler<HostProjectReloadParams, HostProjectReloadResult>
{
    protected override async Task<HostProjectReloadResult> HandleAsync(HostProjectReloadParams request, CancellationToken token)
    {
        if (!sessionProvider.IsComposed)
        {
            return new HostProjectReloadResult();
        }

        var root = request.WorkspaceRoot.IsFile ? request.WorkspaceRoot.LocalPath : request.WorkspaceRoot.OriginalString;
        var project = await projectFileLoader.LoadOrSynthesizeAsync(root, options.Value.Workspace.SupportedLanguage);
        if (sessionProvider.Execution.State is not ProgramState.Idle)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("📌 The project file changed while a program is {state}: the runtime session is kept.", sessionProvider.Execution.State);
            }

            Gained(project);
            return new HostProjectReloadResult { IsProgramRunning = true };
        }

        sessionProvider.Compose(project.ProjectInfo, SymbolUri.Root(project.Uri));

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("🔄 Runtime session composed again from the project file: {modules} module(s).", project.ProjectInfo.Modules.Length);
        }

        return new HostProjectReloadResult { Composed = true, ModuleCount = project.ProjectInfo.Modules.Length };
    }

    // a module the project gained while the program is in progress is not of the program, which is not resumed without it, and is defined once the program is
    // over (HeldDefinitions.Added); one it gained and no longer lists is taken out again.
    private void Gained(ProjectFile project)
    {
        var session = sessionProvider.Session;
        var listed = sessionProvider.ModulesOf(project.ProjectInfo, SymbolUri.Root(project.Uri));
        foreach (var module in listed.Where(module => !session.Symbols.TryGetModule(module.Name, out _)))
        {
            HeldDefinitions.Added(session, module);
        }

        foreach (var added in HeldDefinitions.AddedWhileTheProgramIsInProgress(session)
            .Where(added => !listed.Any(module => string.Equals(module.Name, added, StringComparison.OrdinalIgnoreCase))))
        {
            HeldDefinitions.Discarded(session, added);
        }
    }
}
