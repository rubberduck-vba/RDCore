using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.LanguageServer.Server.Handlers.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Server.Commands;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using System.IO.Abstractions;
using System.Text;

namespace RDCore.LanguageServer.Workspace;

/// <summary>
/// <c>rdcore.importProject</c>: makes the <c>.rdproj</c> of a VB6 project (<see cref="VbpProjectImporter"/>), in the folder of its <c>.vbp</c>.
/// </summary>
/// <remarks>
/// <para>
/// The argument is the <c>.vbp</c>: its URI as a string, or an object - <c>{ "uri": "...", "text": "...", "overwrite": true }</c> - whose <c>text</c> is the
/// <c>.vbp</c>'s when the client holds it, and whose <c>overwrite</c> replaces a project file without asking - nothing is asked of the user, which a client
/// that imports its <c>.vbp</c> again after each change needs. A folder, or no argument at all for the
/// folder of the workspace, is the one VB6 project in it; the user is asked which, when there are several.
/// </para>
/// <para>
/// A folder that has a project file already is given the new one only once the user says to replace it. The project file of the folder of the workspace is
/// written as the server writes it (<see cref="IWorkspaceReloadService.ChangeAsync"/>), and the workspace becomes what it lists - the client is then told, as
/// of every change of the project (<see cref="WorkspaceChangeReport"/>): asked to pull its diagnostics again; another folder's is written.
/// Either way, what the <c>.vbp</c> does not describe is kept (<see cref="VbpImport.Into"/>).
/// The answer is the project file, and what the import could not carry over, each as the user is told it.
/// </para>
/// </remarks>
internal sealed class ImportProjectCommand(
    IWorkspaceDocumentService documents,
    IWorkspaceReloadService reload,
    IProjectFileWriter writer,
    IProjectFileLoader loader,
    ILibrarySource libraries,
    IFileSystem fileSystem,
    IOptions<SdkAppOptions> options,
    ILanguageServerFacade server) : IServerCommand
{
    /// <summary>The name a client asks for the command by.</summary>
    public const string CommandName = "rdcore.importProject";

    private const string VbpExtension = ".vbp";

    public string Name => CommandName;

    public async Task<JToken?> ExecuteAsync(JArray? arguments, IServerCommandContext context, CancellationToken token)
    {
        var (uri, text, overwrite) = Read(arguments?.FirstOrDefault());
        var vbp = await ProjectFileOfAsync(uri, context, token);

        if (text is null)
        {
            if (!fileSystem.File.Exists(vbp))
            {
                throw new ServerCommandRefusedException(VbpImportText.NotFound(vbp));
            }

            text = VbpProjectImporter.Decode(await fileSystem.File.ReadAllBytesAsync(vbp, token), AnsiEncoding());
        }

        var folder = fileSystem.Path.GetDirectoryName(vbp)!;
        var import = VbpProjectImporter.Import(text, fileSystem.Path.GetFileName(vbp), libraries, fileSystem.Path.GetFullPath(folder));
        var rdproj = fileSystem.Path.Combine(folder, ProjectFile.FileName);

        if (fileSystem.File.Exists(rdproj) && !overwrite
            && await context.AskAsync(VbpImportText.AskReplace(folder, fileSystem.Path.GetFileName(vbp)), [VbpImportText.Replace(), VbpImportText.Cancel()], token)
                != VbpImportText.Replace())
        {
            throw new ServerCommandRefusedException(VbpImportText.Cancelled(fileSystem.Path.GetFileName(vbp)));
        }

        // the workspace's own project file is the server's to write, and the workspace becomes what it lists - which the client is told, as of any change of
        // the project: its diagnostics are to be pulled again. Another folder's is written as it is. Either way, what the .vbp does not describe is kept.
        var reloaded = IsWorkspaceFolder(folder) ? await reload.ChangeAsync(project => import.Into(project, folder), token) : null;
        if (reloaded is not null)
        {
            WorkspaceChangeReport.Send(server, reloaded);
        }
        else
        {
            // a project file that cannot be read is replaced whole, as the user said to replace it.
            await writer.SaveAsync(import.Into(await loader.LoadIfReadableAsync(folder, token), folder), token);
        }

        return new JObject
        {
            ["projectFile"] = DocumentUri.FromFileSystemPath(rdproj).ToString(),
            ["name"] = import.Project.Name,
            ["modules"] = import.Project.Modules.Length,
            ["notes"] = new JArray(import.Notes.Select(note => new JObject
            {
                ["kind"] = note.Kind.ToString(),
                ["subject"] = note.Subject,
                ["message"] = note.ToMessage(),
            })),
        };
    }

    // a URI, or { uri, text, overwrite }.
    private static (string? Uri, string? Text, bool Overwrite) Read(JToken? argument) => argument switch
    {
        JValue { Type: JTokenType.String } value => (value.Value<string>(), null, false),
        JObject arguments => (arguments.Value<string>("uri"), arguments.Value<string>("text"), arguments.Value<bool?>("overwrite") ?? false),
        _ => (null, null, false),
    };

    // the .vbp the argument names: itself, or the one VB6 project of a folder - the user chooses among several.
    private async Task<string> ProjectFileOfAsync(string? uri, IServerCommandContext context, CancellationToken token)
    {
        var path = uri is { Length: > 0 } ? DocumentUri.From(uri).GetFileSystemPath() : documents.WorkspaceRoot;
        if (string.IsNullOrEmpty(path))
        {
            throw new ServerCommandRefusedException(VbpImportText.NoProject(string.Empty));
        }

        if (!fileSystem.Directory.Exists(path))
        {
            return path;
        }

        var projects = fileSystem.Directory.EnumerateFiles(path, "*" + VbpExtension)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        switch (projects.Count)
        {
            case 0:
                throw new ServerCommandRefusedException(VbpImportText.NoProject(path));
            case 1:
                return projects[0];
        }

        var names = projects.Select(project => fileSystem.Path.GetFileName(project)).ToList();
        var chosen = await context.AskAsync(VbpImportText.AskChoose(path), names, token);
        return chosen is not null && names.IndexOf(chosen) is var index and >= 0
            ? projects[index]
            : throw new ServerCommandRefusedException(VbpImportText.Cancelled(path));
    }

    private bool IsWorkspaceFolder(string folder)
        => !string.IsNullOrEmpty(documents.WorkspaceRoot)
            && string.Equals(fileSystem.Path.GetFullPath(folder).TrimEnd('/', '\\'), fileSystem.Path.GetFullPath(documents.WorkspaceRoot).TrimEnd('/', '\\'),
                StringComparison.OrdinalIgnoreCase);

    // VB6 writes its project files in the ANSI code page of the machine.
    private Encoding AnsiEncoding()
        => CodePagesEncodingProvider.Instance.GetEncoding(options.Value.Environment.AnsiCodePage) ?? Encoding.Latin1;
}
