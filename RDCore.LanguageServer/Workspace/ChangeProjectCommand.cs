using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.LanguageServer.Server.Handlers.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server.Commands;
using RDCore.SDK.Workspace;

namespace RDCore.LanguageServer.Workspace;

/// <summary>
/// <c>rdcore.changeProject</c> (<see cref="ServerCommands.ChangeProject"/>): makes the changes of a diagnostic's project fix (<see cref="ProjectFix"/>), writes
/// the project file, and makes the workspace what it then lists.
/// </summary>
/// <remarks>
/// <para>
/// The arguments are the URI of the document the diagnostic is in, and the fix. The project file is written as the server writes it
/// (<see cref="IWorkspaceReloadService.ChangeAsync"/>), and the client is told of the change, as of every change of the project
/// (<see cref="WorkspaceChangeReport"/>): asked to pull its diagnostics again. A fix that changes nothing of the project writes nothing.
/// </para>
/// <para>
/// A change that cannot be made of the project as it is now (it changed since the fix was offered) is a refusal (<c>RequestFailed</c>, -32803) that says
/// why, and nothing is changed; so is a project file the file system will not let be written.
/// </para>
/// </remarks>
internal sealed class ChangeProjectCommand(
    IWorkspaceReloadService reload,
    ILibrarySource libraries,
    ILanguageServerFacade server,
    ILogger<ChangeProjectCommand> logger) : IServerCommand
{
    public string Name => ServerCommands.ChangeProject;

    public async Task<JToken?> ExecuteAsync(JArray? arguments, IServerCommandContext context, CancellationToken token)
    {
        // a JArray is indexed by its int indexer: a list pattern binds its indexer of an object key, which refuses an int.
        if (arguments is not { Count: >= 2 } || arguments[0] is not JValue { Type: JTokenType.String } uri || arguments[1] is not JObject data
            || data.ToObject<ProjectFix>() is not { } fix)
        {
            throw new ArgumentException($"{Name} takes the URI of a document and a project fix.", nameof(arguments));
        }

        var module = DocumentUri.From(uri.Value<string>()!).ToUri();
        ProjectChange? refused = null;
        string? projectFolder = null;
        WorkspaceReloadResult? reloaded;
        try
        {
            // a change that cannot be made leaves the project as it is, which writes nothing.
            reloaded = await reload.ChangeAsync(project =>
            {
                projectFolder = project.Uri;
                return fix.ApplyTo(project, module, libraries, out refused) ?? project;
            }, token);
        }
        catch (ProjectFileNotWrittenException exception)
        {
            throw Refused(VbpImportText.NotWritten(ProjectFileOf(projectFolder), exception.InnerException!), exception);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            throw Refused(VbpImportText.NotReloaded(ProjectFileOf(projectFolder), exception), exception);
        }

        if (refused is not null)
        {
            var path = projectFolder is { Length: > 0 } folder ? Path.GetRelativePath(folder, module.LocalPath) : module.LocalPath;
            throw new ServerCommandRefusedException(ProjectFixText.Refused(refused, path));
        }

        if (reloaded is not null)
        {
            WorkspaceChangeReport.Send(server, reloaded);
        }

        return null;
    }

    private static string ProjectFileOf(string? folder) => folder is { Length: > 0 } ? Path.Combine(folder, ProjectFile.FileName) : ProjectFile.FileName;

    // a refusal the client shows its user on one line, and the whole of what caused it in the server's log.
    private ServerCommandRefusedException Refused(string message, Exception cause)
    {
        logger.LogWarning(cause, "⚠️ rdcore.changeProject was refused: {message}", message);
        return new ServerCommandRefusedException(message);
    }
}
