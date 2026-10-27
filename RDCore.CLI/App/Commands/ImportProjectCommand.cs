using CommandLine;
using RDCore.SDK.ConsoleIO;
using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Platform;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Workspace;
using System.Globalization;
using System.IO.Abstractions;
using System.Text;

namespace RDCore.CLI.App.Commands;

/// <summary>
/// The parsed <c>import</c> verb arguments.
/// </summary>
internal sealed class ImportProjectOptions
{
    [Value(0, MetaName = "path", Required = true, HelpText = "The VB6 project (.vbp) to import, or a folder that has one.")]
    public string Path { get; set; } = string.Empty;

    [Option("force", HelpText = "Overwrite an existing .rdproj.")]
    public bool Force { get; set; }
}

/// <summary>
/// Makes the <c>.rdproj</c> of a VB6 project (<see cref="VbpProjectImporter"/>), in the folder of its <c>.vbp</c>: what <c>rdcore.importProject</c> does
/// for a client of the language server.
/// </summary>
internal sealed class ImportProjectCommand(
    IConsoleMessageWriter writer,
    IFileSystem fileSystem,
    IProjectFileWriter projectWriter,
    IPlatformEnvironment environment) : ICliCommand
{
    public string Name => CommandNames.ImportProjectCommand.Name;
    public IReadOnlyList<string> Aliases => [CommandNames.ImportProjectCommand.Alias];
    public string Summary => Resources.ImportProject_Summary;

    public async Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken token)
    {
        var parsed = Parser.Default.ParseArguments<ImportProjectOptions>(args);
        if (parsed.Errors.Any())
        {
            // CommandLine already wrote the usage/errors to stderr.
            return 2;
        }

        return await ExecuteAsync(parsed.Value, token);
    }

    private async Task<int> ExecuteAsync(ImportProjectOptions args, CancellationToken token)
    {
        var path = fileSystem.Path.GetFullPath(args.Path);
        if (fileSystem.Directory.Exists(path))
        {
            var projects = fileSystem.Directory.EnumerateFiles(path, "*.vbp").Order(StringComparer.OrdinalIgnoreCase).ToList();
            if (projects.Count != 1)
            {
                Write(MessageKind.Error, projects.Count == 0
                    ? string.Format(Resources.ImportProject_NoProject, path)
                    : string.Format(Resources.ImportProject_SeveralProjects, path), string.Join(", ", projects.Select(fileSystem.Path.GetFileName)));
                return 1;
            }

            path = projects[0];
        }

        if (!fileSystem.File.Exists(path))
        {
            Write(MessageKind.Error, string.Format(Resources.ImportProject_NotFound, path), string.Empty);
            return 1;
        }

        var folder = fileSystem.Path.GetDirectoryName(path)!;
        var rdproj = fileSystem.Path.Combine(folder, ProjectFile.FileName);
        if (fileSystem.File.Exists(rdproj) && !args.Force)
        {
            Write(MessageKind.Warning, Resources.ImportProject_AlreadyExists, folder);
            return 0;
        }

        // VB6 writes its project files in the ANSI code page of the machine.
        var ansi = CodePagesEncodingProvider.Instance.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage) ?? Encoding.Latin1;
        var libraries = new DirectoryLibrarySource(fileSystem, environment.Resolve(PlatformEnvironment.SymbolsDirectory));
        var import = VbpProjectImporter.Import(VbpProjectImporter.Decode(await fileSystem.File.ReadAllBytesAsync(path, token), ansi),
            fileSystem.Path.GetFileName(path), libraries);

        await projectWriter.SaveAsync(new ProjectFile(folder, import.Project), token);

        foreach (var note in import.Notes)
        {
            Write(MessageKind.Warning, note.ToMessage(), string.Empty);
        }

        Write(MessageKind.Success, string.Format(Resources.ImportProject_Created, import.Project.Name, rdproj),
            string.Format(Resources.ImportProject_Counts, import.Project.Modules.Length, import.Project.References.Length, import.Notes.Count));
        return 0;
    }

    private void Write(MessageKind kind, string body, string verbose)
        => writer.WriteMessage(new ConsoleMessageBuilder()
            .WithKind(kind)
            .WithTitle(Resources.ImportProject_Title)
            .WithMessageBody(body)
            .WithVerbose(verbose));
}
