using CommandLine;
using RDCore.SDK.ConsoleIO;
using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Workspace;
using System.IO.Abstractions;

namespace RDCore.CLI.App.Commands;

/// <summary>
/// The parsed <c>new</c> verb arguments.
/// </summary>
internal sealed class NewWorkspaceOptions
{
    [Value(0, MetaName = "path", Required = true, HelpText = "The workspace directory to scaffold. Created if it does not exist.")]
    public string Path { get; set; } = string.Empty;

    [Option("name", HelpText = "The project name. Defaults to the directory name.")]
    public string? Name { get; set; }

    [Option("empty", HelpText = "Write an empty project instead of scanning the directory for source files.")]
    public bool Empty { get; set; }

    [Option("force", HelpText = "Overwrite an existing .rdproj.")]
    public bool Force { get; set; }
}

/// <summary>
/// Scaffolds a <c>.rdproj</c> workspace at the given path. By default it walks the directory and adds
/// every VBA module file as a module and everything else as a project file.
/// </summary>
internal sealed class NewWorkspaceCommand(
    IConsoleMessageWriter writer,
    IFileSystem fileSystem,
    IProjectFileWriter projectWriter) : ICliCommand
{
    // VB module file kinds; the rest of a workspace's files are OtherFiles. Could move to the SDK
    // once another consumer needs it.
    private static readonly HashSet<string> ModuleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bas", ".cls", ".frm", ".doccls",
    };

    public string Name => CommandNames.NewWorkspaceCommand.Name;
    public IReadOnlyList<string> Aliases => [CommandNames.NewWorkspaceCommand.Alias];
    public string Summary => Resources.NewWorkspace_Summary;

    public async Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken token)
    {
        var parsed = Parser.Default.ParseArguments<NewWorkspaceOptions>(args);
        if (parsed.Errors.Any())
        {
            // CommandLine already wrote the usage/errors to stderr.
            return 2;
        }

        return await ExecuteAsync(parsed.Value, token);
    }

    private async Task<int> ExecuteAsync(NewWorkspaceOptions args, CancellationToken token)
    {
        var root = fileSystem.Path.GetFullPath(args.Path);
        if (fileSystem.File.Exists(root))
        {
            Write(MessageKind.Error, Resources.NewWorkspace_InvalidPath, args.Path);
            return 1;
        }

        fileSystem.Directory.CreateDirectory(root);

        var rdproj = fileSystem.Path.Combine(root, ProjectFile.FileName);
        if (fileSystem.File.Exists(rdproj) && !args.Force)
        {
            Write(MessageKind.Warning, Resources.NewWorkspace_AlreadyExists, root);
            return 0;
        }

        var projectName = string.IsNullOrWhiteSpace(args.Name)
            ? fileSystem.Path.GetFileName(root.TrimEnd('/', '\\'))
            : args.Name.Trim();

        var project = args.Empty ? new RDCoreProject { Name = projectName } : Scan(root, projectName);
        await projectWriter.SaveAsync(new ProjectFile(root, project), token);

        Write(MessageKind.Success,
            string.Format(Resources.NewWorkspace_Created, projectName, rdproj),
            $"{project.Modules.Length} module(s), {project.OtherFiles.Length} other file(s)");
        return 0;
    }

    private RDCoreProject Scan(string root, string projectName)
    {
        var modules = new List<RDCoreModule>();
        var otherFiles = new List<RDCoreFile>();
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in fileSystem.Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = fileSystem.Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative == ProjectFile.FileName || IsIgnored(relative))
            {
                continue;
            }

            if (ModuleExtensions.Contains(fileSystem.Path.GetExtension(file)))
            {
                modules.Add(new RDCoreModule { RelativeUri = relative });
            }
            else
            {
                otherFiles.Add(new RDCoreFile { RelativeUri = relative });
            }

            var folder = fileSystem.Path.GetDirectoryName(relative)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(folder))
            {
                folders.Add(folder);
            }
        }

        return new RDCoreProject
        {
            Name = projectName,
            Modules = [.. modules.OrderBy(module => module.RelativeUri, StringComparer.OrdinalIgnoreCase)],
            OtherFiles = [.. otherFiles.OrderBy(other => other.RelativeUri, StringComparer.OrdinalIgnoreCase)],
            Folders = [.. folders.OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase)],
        };
    }

    // skip dot-files and build output.
    private static bool IsIgnored(string relativeUri)
        => relativeUri.Split('/').Any(segment => segment.StartsWith('.') || segment is "bin" or "obj");

    private void Write(MessageKind kind, string body, string verbose)
        => writer.WriteMessage(new ConsoleMessageBuilder()
            .WithKind(kind)
            .WithTitle(Resources.NewWorkspace_Title)
            .WithMessageBody(body)
            .WithVerbose(verbose));
}
