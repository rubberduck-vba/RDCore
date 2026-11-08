using System.IO.Abstractions;

namespace RDCore.SDK.Platform;

/// <summary>
/// Resolves platform paths from an explicit <em>platform root</em> rather than the process working directory.
/// </summary>
/// <remarks>
/// The platform root is the directory that holds <c>rdcore.json</c> and one subfolder per component
/// (<c>RDCore.CLI/</c>, <c>RDCore.LanguageServer/</c>, …). A component's own base directory is
/// <c>&lt;Root&gt;/&lt;Component&gt;/</c>, so the root is that directory's parent: independent of the
/// working directory a process happens to be started with.
/// </remarks>
public interface IPlatformEnvironment
{
    /// <summary>
    /// The platform root directory.
    /// </summary>
    string Root { get; }

    /// <summary>
    /// Absolute path of the platform manifest, <c>&lt;Root&gt;/rdcore.json</c>.
    /// </summary>
    string ManifestPath { get; }

    /// <summary>
    /// Absolute path of the folder the platform's components write their logs to, created if missing: the user's, not the platform root's, so that a
    /// platform can be installed where its user can't write (<c>Program Files</c>, <c>/opt</c>).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>Windows: <c>%LOCALAPPDATA%\RDCore\Logs</c>. Local, not roaming: a log is of the machine it was written on.</description></item>
    /// <item><description>macOS: <c>~/Library/Logs/RDCore</c>.</description></item>
    /// <item><description>Linux and others: <c>$XDG_STATE_HOME/rdcore/logs</c>, or <c>~/.local/state/rdcore/logs</c> where <c>XDG_STATE_HOME</c> isn't set:
    /// the XDG Base Directory Specification places logs in the user's state.</description></item>
    /// </list>
    /// <c>RDCORE_LOGS_DIRECTORY</c> (<see cref="PlatformEnvironment.LogsEnvironmentVariable"/>) overrides it everywhere.
    /// </remarks>
    string LogsDirectory { get; }

    /// <summary>
    /// Resolves a root-relative path (either slash style) to an absolute path under <see cref="Root"/>.
    /// </summary>
    /// <param name="rootRelativePath">A path relative to <see cref="Root"/>, using either <c>/</c> or <c>\</c>.</param>
    string Resolve(string rootRelativePath);

    /// <summary>
    /// Absolute path of the folder of the platform's user: <c>%USERAPPDATA%/RDCore</c>, where what the user sets for themselves is kept - their settings
    /// (<see cref="Server.Configuration.UserSettingsStore"/>). Not created: nothing is kept there until something is.
    /// </summary>
    /// <remarks>
    /// It is not under <see cref="Root"/>: a platform that is deployed again replaces what is there, and what the user set is theirs to keep.
    /// </remarks>
    string UserDataDirectory { get; }
}

/// <inheritdoc/>
public sealed class PlatformEnvironment : IPlatformEnvironment
{
    /// <summary>
    /// Name of the environment variable that overrides the derived root. Set on the entry process;
    /// inherited by spawned children.
    /// </summary>
    public const string RootEnvironmentVariable = Client.RDCoreLanguageServerLaunch.PlatformRootEnvironmentVariable;

    /// <summary>
    /// Name of the folder of the platform root that holds the descriptions of the libraries a project can reference
    /// (<see cref="Runtime.Libraries.DirectoryLibrarySource"/>).
    /// </summary>
    public const string SymbolsDirectory = "Symbols";

    /// <summary>
    /// Name of the environment variable that overrides the folder of the platform's user (<see cref="UserDataDirectory"/>): what a test sets, so that it never reads
    /// nor writes the settings of whoever runs it. Inherited by spawned children.
    /// </summary>
    public const string UserDataEnvironmentVariable = "RDCORE_USER_DATA";

    /// <summary>
    /// Name of the environment variable that overrides the folder the platform logs to (<see cref="LogsDirectory"/>): what a test or a release check sets, to
    /// read the logs of the platform it starts and leave the user's alone. Inherited by spawned children.
    /// </summary>
    public const string LogsEnvironmentVariable = "RDCORE_LOGS_DIRECTORY";

    private static readonly Lazy<IPlatformEnvironment> _default = new(() => new PlatformEnvironment(new FileSystem()));

    /// <summary>
    /// A DI-free instance for bootstrap contexts (e.g. logging configuration).
    /// </summary>
    public static IPlatformEnvironment Default => _default.Value;

    private readonly IFileSystem _fileSystem;

    /// <summary>
    /// Creates a platform environment, deriving <see cref="Root"/> from
    /// <see cref="RootEnvironmentVariable"/> when set, otherwise from the process base directory.
    /// </summary>
    /// <param name="fileSystem">The file system abstraction used to resolve and create paths.</param>
    public PlatformEnvironment(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;

        var overridden = Environment.GetEnvironmentVariable(RootEnvironmentVariable);
        Root = !string.IsNullOrWhiteSpace(overridden)
            ? _fileSystem.Path.GetFullPath(overridden)
            : _fileSystem.DirectoryInfo.New(AppContext.BaseDirectory).Parent!.FullName;
    }

    public string Root { get; }

    // the roaming application data of the user: %APPDATA% on Windows, ~/.config elsewhere.
    public string UserDataDirectory
        => Environment.GetEnvironmentVariable(UserDataEnvironmentVariable) is { Length: > 0 } overridden
            ? _fileSystem.Path.GetFullPath(overridden)
            : _fileSystem.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RDCore");

    public string ManifestPath => _fileSystem.Path.Combine(Root, PlatformManifest.FileName);

    public string LogsDirectory
    {
        get
        {
            var directory = Environment.GetEnvironmentVariable(LogsEnvironmentVariable) is { Length: > 0 } overridden
                ? _fileSystem.Path.GetFullPath(overridden)
                : UsersLogsDirectory();
            _fileSystem.Directory.CreateDirectory(directory);
            return directory;
        }
    }

    private string UsersLogsDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return _fileSystem.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RDCore", "Logs");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS())
        {
            return _fileSystem.Path.Combine(home, "Library", "Logs", "RDCore");
        }

        // the specification ignores a relative XDG_STATE_HOME, as if it weren't set.
        var state = Environment.GetEnvironmentVariable("XDG_STATE_HOME") is { Length: > 0 } xdg && _fileSystem.Path.IsPathRooted(xdg)
            ? xdg
            : _fileSystem.Path.Combine(home, ".local", "state");
        return _fileSystem.Path.Combine(state, "rdcore", "logs");
    }

    public string Resolve(string rootRelativePath)
        => _fileSystem.Path.Combine(Root, rootRelativePath.Replace('/', _fileSystem.Path.DirectorySeparatorChar));
}
