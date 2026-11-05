using RDCore.SDK.Client;

namespace RDCore.SDK.Platform;

/// <summary>
/// Keeps what a platform starts under its platform root: a platform starts only its own executables, whatever path its manifest, or an extension's, names.
/// </summary>
/// <remarks>
/// A path is under the root as the file system resolves it: <see cref="Path.GetFullPath(string)"/> folds <c>..</c>, but follows no link, so each folder on the
/// way, and the file, that is a link (a symbolic link, a junction) is taken where it leads.
/// </remarks>
public static class PlatformRootConfinement
{
    /// <summary>
    /// Whether <paramref name="path"/> is under <paramref name="platformRoot"/>, through every link on the way to either.
    /// </summary>
    /// <param name="platformRoot">The platform root.</param>
    /// <param name="path">A full path, or one relative to the working directory.</param>
    public static bool IsUnder(string platformRoot, string path) => IsUnderRealRoot(RealPath(platformRoot), path);

    /// <summary>
    /// <paramref name="executable"/>, when it is under <paramref name="platformRoot"/> (<see cref="IsUnder"/>).
    /// </summary>
    /// <param name="platformRoot">The platform root.</param>
    /// <param name="executable">The full path of the executable the platform is to start.</param>
    /// <exception cref="InvalidDataException"><paramref name="executable"/> is not under <paramref name="platformRoot"/>.</exception>
    public static string Confined(string platformRoot, string executable)
        => IsUnder(platformRoot, executable)
            ? executable
            : throw new InvalidDataException(string.Format(LaunchMessages.ExecutableStartedOutsideThePlatformRoot, platformRoot, executable));

    internal static bool IsUnderRealRoot(string realRoot, string path)
    {
        var relative = Path.GetRelativePath(realRoot, RealPath(path));
        return !(relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative));
    }

    // a full path through every link on the way: each folder, and the file, that is a link is where it leads.
    internal static string RealPath(string path)
    {
        var full = Path.GetFullPath(path);
        var real = Path.GetPathRoot(full)!;
        foreach (var part in full[real.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            real = Path.Combine(real, part);
            FileSystemInfo entry = Directory.Exists(real) ? new DirectoryInfo(real) : new FileInfo(real);
            if (entry.Exists && entry.LinkTarget is not null && entry.ResolveLinkTarget(returnFinalTarget: true) is { } target)
            {
                real = RealPath(target.FullName);
            }
        }

        return real;
    }
}
