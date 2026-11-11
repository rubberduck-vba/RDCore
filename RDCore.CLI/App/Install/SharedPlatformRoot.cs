using System.Text.Json;
using System.Text.Json.Nodes;

namespace RDCore.CLI.App.Install;

/// <summary>
/// A platform deployed in the shared root: its version and its folder.
/// </summary>
/// <param name="Version">The platform's version.</param>
/// <param name="Root">The folder it is deployed in: <c>versions/&lt;version&gt;</c> under the shared root.</param>
internal sealed record DeployedPlatform(PlatformVersion Version, string Root)
{
    /// <summary>
    /// The platform manifest's path.
    /// </summary>
    public string Manifest => Path.Combine(Root, "rdcore.json");
}

/// <summary>
/// The folder every client of this user installs the platform into, whichever comes first: <c>rdc install</c>, and the IDE add-in's
/// platform installer. Both deploy it the same way, so that each finds what the other deployed.
/// </summary>
/// <remarks>
/// <para>
/// <b>A deployed version is never changed.</b> Each one is copied beside the others, under <c>versions/&lt;version&gt;</c>, first into a
/// <c>.partial</c> folder that is moved into place once it is whole. A version is named the current one in <c>installer.json</c>
/// (<c>{"Current":"0.2.0","Previous":"0.1.0"}</c>), and an older one deployed beside it isn't made current.
/// </para>
/// <para>
/// <b>One installation at a time</b>, across processes: <c>install.lock</c> is held open, unshared, for the whole of one.
/// </para>
/// <para>
/// Versions are not removed here: the add-in's installer keeps the current one and the one before it, and removes the others when nothing
/// runs from them.
/// </para>
/// </remarks>
/// <param name="root">The root's folder.</param>
internal sealed class SharedPlatformRoot(string root)
{
    private const string StateFile = "installer.json";
    private const string VersionsFolder = "versions";
    private const string LockFile = "install.lock";
    private const string PartialSuffix = ".partial";
    private static readonly TimeSpan LockRetry = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// The shared root of this user: <c>&lt;local application data&gt;/RDCore/Platform</c>.
    /// </summary>
    public static string ForThisUser =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RDCore", "Platform");

    /// <summary>
    /// The root's folder.
    /// </summary>
    public string Root { get; } = root;

    /// <summary>
    /// The folder deployed versions are in.
    /// </summary>
    public string Versions => Path.Combine(Root, VersionsFolder);

    /// <summary>
    /// The platform in use, or null when none is deployed, or the one named is gone or incomplete.
    /// </summary>
    public DeployedPlatform? Current
    {
        get
        {
            if (ReadState().Current is not { } current || !PlatformVersion.TryParse(current, out var version))
            {
                return null;
            }

            var folder = VersionRoot(version);
            return IsPlatform(folder) ? new DeployedPlatform(version, folder) : null;
        }
    }

    /// <summary>
    /// Where <paramref name="version"/> is, or would be, deployed.
    /// </summary>
    /// <param name="version">The version.</param>
    public string VersionRoot(PlatformVersion version) => Path.Combine(Versions, version.Text);

    /// <summary>
    /// Holds the root for one installation, across processes: an exclusive handle on <c>install.lock</c>, released with the handle, and by
    /// the system if the process dies. <paramref name="waiting"/> is told once if another installation holds it.
    /// </summary>
    /// <param name="waiting">Told once, when another installation holds the root.</param>
    /// <param name="token">Stops the wait.</param>
    public async Task<IDisposable> LockAsync(Action waiting, CancellationToken token)
    {
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, LockFile);
        var told = false;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                if (!told)
                {
                    waiting();
                    told = true;
                }

                await Task.Delay(LockRetry, token);
            }
        }
    }

    /// <summary>
    /// Copies the platform tree at <paramref name="tree"/> in as <paramref name="version"/>, unless that version is deployed already: true
    /// when it copied it. Call it with the root held.
    /// </summary>
    /// <param name="tree">The platform tree to copy: the folder of its <c>rdcore.json</c>.</param>
    /// <param name="version">The version it is deployed as.</param>
    public bool Deploy(string tree, PlatformVersion version)
    {
        var folder = VersionRoot(version);
        if (IsPlatform(folder))
        {
            return false;
        }

        var partial = folder + PartialSuffix;
        try
        {
            DeleteFolder(partial);
            CopyFolder(tree, partial);
            if (Flaw(partial) is { } flaw)
            {
                throw new InvalidOperationException(flaw);
            }

            // a folder of this version that isn't a whole platform is what's left of a deployment someone broke: it is replaced.
            DeleteFolder(folder);
            Directory.Move(partial, folder);
        }
        finally
        {
            DeleteFolder(partial);
        }

        return true;
    }

    /// <summary>
    /// Makes <paramref name="version"/>, deployed, the platform in use, unless a newer one is: true when it is the current one after. Call it
    /// with the root held.
    /// </summary>
    /// <param name="version">A deployed version.</param>
    public bool MakeCurrentUnlessOlder(PlatformVersion version)
    {
        if (Current is { } current && current.Version.CompareTo(version) > 0)
        {
            return false;
        }

        var state = ReadState();
        if (state.Current != version.Text)
        {
            state = state with { Current = version.Text, Previous = state.Current };
        }

        // written whole, then moved over the old file, so a reader never sees half of it.
        var path = Path.Combine(Root, StateFile);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state));
        File.Move(temporary, path, overwrite: true);
        return true;
    }

    /// <summary>
    /// Whether <paramref name="folder"/> is a whole platform: one whose <c>rdcore.json</c> names a language server it has.
    /// </summary>
    /// <param name="folder">The folder of a platform's <c>rdcore.json</c>.</param>
    public static bool IsPlatform(string folder) => Flaw(folder) is null;

    /// <summary>
    /// Why <paramref name="folder"/> isn't a whole platform, or null when it is.
    /// </summary>
    /// <param name="folder">The folder of a platform's <c>rdcore.json</c>.</param>
    public static string? Flaw(string folder)
    {
        var manifestPath = Path.Combine(folder, "rdcore.json");
        if (!File.Exists(manifestPath))
        {
            return string.Format(Resources.Install_NoManifest, folder);
        }

        string? languageServer;
        try
        {
            languageServer = JsonNode.Parse(File.ReadAllText(manifestPath))?["langService"]?.GetValue<string>();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return string.Format(Resources.Install_UnreadableManifest, manifestPath, exception.Message);
        }

        if (string.IsNullOrWhiteSpace(languageServer))
        {
            return string.Format(Resources.Install_NoLanguageServer, manifestPath);
        }

        var full = Path.GetFullPath(Path.Combine(folder, languageServer.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        if (Path.IsPathRooted(languageServer) || !full.StartsWith(prefix, PathComparison))
        {
            return string.Format(Resources.Install_LanguageServerOutside, manifestPath, languageServer);
        }

        return File.Exists(full) || File.Exists(full + ".exe") ? null : string.Format(Resources.Install_LanguageServerMissing, manifestPath, languageServer);
    }

    /// <summary>
    /// How paths compare on this system: without case on Windows.
    /// </summary>
    public static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private InstallerState ReadState()
    {
        var path = Path.Combine(Root, StateFile);
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<InstallerState>(File.ReadAllText(path)) ?? new() : new();
        }
        catch (JsonException)
        {
            // a state file nobody can read names no platform; the next installation writes a good one.
            return new();
        }
    }

    // every folder and file, and off Windows each one's permissions, so an executable stays one.
    private static void CopyFolder(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var folder in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, folder)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(target, Path.GetRelativePath(source, file));
            File.Copy(file, copy);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(copy, File.GetUnixFileMode(file));
            }
        }
    }

    private static void DeleteFolder(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    // the add-in's installer reads and writes it with System.Text.Json's defaults: {"Current":"0.2.0","Previous":"0.1.0"}.
    private sealed record InstallerState
    {
        public string? Current { get; init; }

        public string? Previous { get; init; }
    }
}
