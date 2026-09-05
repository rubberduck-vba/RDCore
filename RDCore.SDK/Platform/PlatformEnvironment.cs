using System.IO.Abstractions;

namespace RDCore.SDK.Platform;

/// <summary>
/// Resolves platform paths from an explicit <em>platform root</em> rather than the process working directory.
/// </summary>
/// <remarks>
/// The platform root is the directory that holds <c>rdcore.json</c> and one subfolder per component
/// (<c>RDCore.CLI/</c>, <c>RDCore.LanguageServer/</c>, …). A component's own base directory is
/// <c>&lt;Root&gt;/&lt;Component&gt;/</c>, so the root is that directory's parent — independent of the
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
    /// Absolute path of the platform logs directory, <c>&lt;Root&gt;/Logs</c>, created if missing.
    /// </summary>
    string LogsDirectory { get; }

    /// <summary>
    /// Resolves a root-relative path (either slash style) to an absolute path under <see cref="Root"/>.
    /// </summary>
    /// <param name="rootRelativePath">A path relative to <see cref="Root"/>, using either <c>/</c> or <c>\</c>.</param>
    string Resolve(string rootRelativePath);
}

/// <inheritdoc/>
public sealed class PlatformEnvironment : IPlatformEnvironment
{
    /// <summary>
    /// Name of the environment variable that overrides the derived root. Set on the entry process;
    /// inherited by spawned children.
    /// </summary>
    public const string RootEnvironmentVariable = "RDCORE_PLATFORM_ROOT";

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

    public string ManifestPath => _fileSystem.Path.Combine(Root, "rdcore.json");

    public string LogsDirectory
    {
        get
        {
            var directory = _fileSystem.Path.Combine(Root, "Logs");
            _fileSystem.Directory.CreateDirectory(directory);
            return directory;
        }
    }

    public string Resolve(string rootRelativePath)
        => _fileSystem.Path.Combine(Root, rootRelativePath.Replace('/', _fileSystem.Path.DirectorySeparatorChar));
}
