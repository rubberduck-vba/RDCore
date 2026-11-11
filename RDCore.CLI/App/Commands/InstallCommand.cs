using CommandLine;
using RDCore.CLI.App.Install;
using RDCore.CLI.ConsoleIO;
using RDCore.CLI.ConsoleIO.Model;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RDCore.CLI.App.Commands;

/// <summary>
/// The parsed <c>install</c> verb arguments.
/// </summary>
internal sealed class InstallOptions
{
    [Option("path-only", HelpText = nameof(Resources.Install_PathOnlyHelp), ResourceType = typeof(Resources), SetName = "path-only")]
    public bool PathOnly { get; set; }

    [Option("no-modify-path", HelpText = nameof(Resources.Install_NoModifyPathHelp), ResourceType = typeof(Resources), SetName = "deploy")]
    public bool NoModifyPath { get; set; }
}

/// <summary>
/// Where <c>rdc install</c> installs from and to.
/// </summary>
/// <param name="Tree">The platform tree <c>rdc</c> runs from: the folder of its <c>rdcore.json</c>.</param>
/// <param name="Root">The shared root the platform is deployed into, for every client of the user.</param>
/// <param name="Home">The user's home folder: <c>~/.local/bin</c> and the login profile are in it, on Linux and macOS.</param>
/// <param name="ProcessPath">The PATH <c>rdc</c> runs with: a shell that starts after has <c>~/.local/bin</c> on it if this has.</param>
/// <param name="WindowsPath">The user's <c>Path</c> on Windows; null elsewhere.</param>
internal sealed record InstallLocations(string Tree, string Root, string Home, string? ProcessPath, IUserPathVariable? WindowsPath)
{
    /// <summary>
    /// This user's: the tree <c>rdc</c> runs from, the shared root, the home folder, and the user's own <c>Path</c>.
    /// </summary>
    public static InstallLocations ForThisUser()
        => new(
            Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory))!,
            SharedPlatformRoot.ForThisUser,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable("PATH"),
            OperatingSystem.IsWindows() ? new WindowsUserPathVariable() : null);
}

/// <summary>
/// Installs the platform tree <c>rdc</c> runs from for the current user: deploys it into the shared root, which the IDE add-in installs
/// into too, makes it current unless a newer version is, and puts the current version's <c>rdc</c> on the user's PATH.
/// </summary>
/// <param name="writer">Where the messages are written.</param>
/// <param name="locations">Where it installs from and to; this user's by default.</param>
internal sealed class InstallCommand(IConsoleMessageWriter writer, InstallLocations? locations = null) : ICliCommand
{
    private InstallLocations Locations { get; } = locations ?? InstallLocations.ForThisUser();

    public string Name => CommandNames.InstallCommand.Name;
    public IReadOnlyList<string> Aliases => [CommandNames.InstallCommand.Alias];
    public string Summary => Resources.Install_Summary;

    public async Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken token)
    {
        var parsed = Parser.Default.ParseArguments<InstallOptions>(args);
        if (parsed.Errors.Any())
        {
            // CommandLine already wrote the usage/errors to stderr.
            return 2;
        }

        var options = parsed.Value;
        var root = new SharedPlatformRoot(Locations.Root);
        PlatformVersion? version = null;
        if (!options.PathOnly && !TryReadTree(out version))
        {
            return 1;
        }

        using var held = await root.LockAsync(() => Write(MessageKind.Information, string.Format(Resources.Install_Waiting, root.Root)), token);
        if (version is not null)
        {
            Deploy(root, version);
        }

        if (options.NoModifyPath)
        {
            return 0;
        }

        if (root.Current is not { } current)
        {
            Write(MessageKind.Error, string.Format(Resources.Install_NothingCurrent, root.Root));
            return 1;
        }

        return PutOnPath(root, current) ? 0 : 1;
    }

    // the tree rdc runs from: a whole platform, whose rdcore.json names its version.
    private bool TryReadTree(out PlatformVersion? version)
    {
        version = null;
        if (SharedPlatformRoot.Flaw(Locations.Tree) is { } flaw)
        {
            Write(MessageKind.Error, string.Format(Resources.Install_NotATree, flaw));
            return false;
        }

        var manifest = Path.Combine(Locations.Tree, "rdcore.json");
        string? text;
        try
        {
            text = JsonNode.Parse(File.ReadAllText(manifest))?["platformVersion"]?.GetValue<string>();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            Write(MessageKind.Error, string.Format(Resources.Install_UnreadableManifest, manifest, exception.Message));
            return false;
        }

        if (!PlatformVersion.TryParse(text, out version))
        {
            Write(MessageKind.Error, string.Format(Resources.Install_NoVersion, manifest, text));
            return false;
        }

        return true;
    }

    private void Deploy(SharedPlatformRoot root, PlatformVersion version)
    {
        var folder = root.VersionRoot(version);
        var fromTheRoot = string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Locations.Tree)), Path.GetFullPath(folder),
            SharedPlatformRoot.PathComparison);
        if (fromTheRoot || !root.Deploy(Locations.Tree, version))
        {
            Write(MessageKind.Information, string.Format(Resources.Install_AlreadyDeployed, version, folder));
        }
        else
        {
            Write(MessageKind.Success, string.Format(Resources.Install_Deployed, version, folder));
        }

        var before = root.Current;
        if (root.MakeCurrentUnlessOlder(version))
        {
            if (before?.Version != version)
            {
                Write(MessageKind.Success, string.Format(Resources.Install_MadeCurrent, version));
            }
        }
        else
        {
            Write(MessageKind.Warning, string.Format(Resources.Install_NewerIsCurrent, version, before!.Version));
        }
    }

    private bool PutOnPath(SharedPlatformRoot root, DeployedPlatform current)
    {
        var rdc = RdcOf(current);
        var paths = new UserPath(root, Locations.Home, Locations.ProcessPath, Locations.WindowsPath);
        if (OperatingSystem.IsWindows())
        {
            var folder = Path.GetDirectoryName(rdc)!;
            Write(MessageKind.Success, string.Format(
                paths.OnWindowsPath(rdc) == PathChange.Changed ? Resources.Install_AddedToPath : Resources.Install_AlreadyOnPath, folder));
            return true;
        }

        var report = new List<string>();
        if (paths.OnUnixPath(rdc, report) is null)
        {
            Write(MessageKind.Error, string.Format(Resources.Install_LinkTaken, paths.Link));
            return false;
        }

        foreach (var line in report)
        {
            Write(MessageKind.Success, line);
        }

        return true;
    }

    // the current version's rdc, where its rdcore.json names it: the environment host is rdc.
    private static string RdcOf(DeployedPlatform platform)
    {
        var host = JsonNode.Parse(File.ReadAllText(platform.Manifest))?["hostService"]?.GetValue<string>() ?? "RDCore.CLI/rdc";
        var path = Path.GetFullPath(Path.Combine(platform.Root, host.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar)));
        return OperatingSystem.IsWindows() || File.Exists(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? path : path[..^4];
    }

    private void Write(MessageKind kind, string body)
        => writer.WriteMessage(new ConsoleMessageBuilder()
            .WithKind(kind)
            .WithTitle(Resources.Install_Title)
            .WithMessageBody(body));
}
