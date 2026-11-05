using RDCore.SDK.Client;

namespace RDCore.SDK.Platform;

/// <summary>
/// Keeps what a platform starts under its platform root: a platform starts only its own executables, whatever path its manifest, or an extension's, names.
/// </summary>
/// <remarks>
/// A path is where the system finds it, its <see cref="RealPath">real path</see>: each folder on the way, and the file, that is a link (a symbolic link, a
/// junction) is taken where it leads. A path is checked, and started, as that real path, so what is started is what was checked.
/// </remarks>
public static class PlatformRootConfinement
{
    // the most links followed on the way to one path, as the system has it (MAXSYMLINKS is 40 on Linux): a cycle of links leads nowhere.
    private const int MostLinks = 40;

    /// <summary>
    /// Whether <paramref name="path"/> is under <paramref name="platformRoot"/>, both as their <see cref="RealPath">real paths</see>.
    /// </summary>
    /// <param name="platformRoot">The platform root.</param>
    /// <param name="path">A full path, or one relative to the working directory.</param>
    /// <exception cref="IOException">A link on the way to either leads nowhere the system can follow: a cycle, or more than 40 links.</exception>
    /// <exception cref="UnauthorizedAccessException">A link on the way to either can't be read.</exception>
    public static bool IsUnder(string platformRoot, string path) => IsUnderRealRoot(RealPath(platformRoot), path);

    /// <summary>
    /// The <see cref="RealPath">real path</see> of <paramref name="executable"/>, when it is under <paramref name="platformRoot"/>, or under
    /// <paramref name="ownFolder"/>.
    /// </summary>
    /// <param name="platformRoot">The platform root.</param>
    /// <param name="executable">The full path of the executable the platform is to start.</param>
    /// <param name="ownFolder">
    /// The folder of the starting process's own assemblies (<see cref="AppContext.BaseDirectory"/>), when what it starts there is its own: the process runs that
    /// folder's code already, and its executables are its own whatever the platform root is said to be (<c>rdc</c> starting <c>rdc</c>, its external host).
    /// </param>
    /// <exception cref="InvalidDataException">
    /// <paramref name="executable"/> is not under <paramref name="platformRoot"/> (nor <paramref name="ownFolder"/>), or a link on the way to it leads nowhere the
    /// system can follow.
    /// </exception>
    public static string Confined(string platformRoot, string executable, string? ownFolder = null)
    {
        string real;
        bool confined;
        try
        {
            real = RealPath(executable);
            confined = IsUnderRealRoot(RealPath(platformRoot), real) || ownFolder is not null && IsUnderRealRoot(RealPath(ownFolder), real);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException(string.Format(LaunchMessages.ExecutableStartedThroughALinkThatCannotBeFollowed, platformRoot, executable), exception);
        }

        return confined
            ? real
            : throw new InvalidDataException(string.Format(LaunchMessages.ExecutableStartedOutsideThePlatformRoot, platformRoot, executable));
    }

    internal static bool IsUnderRealRoot(string realRoot, string path)
    {
        var relative = Path.GetRelativePath(realRoot, RealPath(path));
        return !(relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative));
    }

    /// <summary>
    /// The full path of <paramref name="path"/> where the system finds it, with no link on the way: each folder, and the file, that is a link is where it leads.
    /// </summary>
    /// <remarks>
    /// <c>..</c> is taken as the system takes it. Windows folds it into the path first (<see cref="Path.GetFullPath(string)"/>), and <c>Linked\..</c> is the
    /// folder <c>Linked</c> is in. Elsewhere the kernel takes it where the path is so far, and <c>Linked/..</c> is the folder <c>Linked</c>'s target is in.
    /// </remarks>
    /// <param name="path">A full path, or one relative to the working directory.</param>
    /// <exception cref="IOException">A link on the way leads nowhere the system can follow: a cycle, or more than 40 links.</exception>
    /// <exception cref="UnauthorizedAccessException">A link on the way can't be read.</exception>
    public static string RealPath(string path)
    {
        var links = 0;
        return OperatingSystem.IsWindows()
            ? Folded(Path.GetFullPath(path), ref links)
            : Walked(Path.IsPathRooted(path) ? path : Path.Combine(Directory.GetCurrentDirectory(), path), ref links);
    }

    // Windows: the path with ".." folded, then each link on the way where it leads.
    private static string Folded(string full, ref int links)
    {
        var real = Path.GetPathRoot(full)!;
        foreach (var part in full[real.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            real = Path.Combine(real, part);
            FileSystemInfo entry = Directory.Exists(real) ? new DirectoryInfo(real) : new FileInfo(real);
            if (entry.LinkTarget is not null && entry.ResolveLinkTarget(returnFinalTarget: false) is { } target)
            {
                Follow(ref links, full);
                real = Folded(Path.GetFullPath(target.FullName), ref links);
            }
        }

        return real;
    }

    // elsewhere: the path taken a part at a time from the root, a link where it leads as soon as it is met, and ".." the folder of the path so far.
    private static string Walked(string path, ref int links)
    {
        var real = Path.GetPathRoot(path)!;
        foreach (var part in path[real.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                real = Path.GetDirectoryName(real) ?? real;
                continue;
            }

            var next = Path.Combine(real, part);
            if (new FileInfo(next).LinkTarget is { } target)
            {
                Follow(ref links, path);
                real = Walked(Path.IsPathRooted(target) ? target : Path.Combine(real, target), ref links);
            }
            else
            {
                real = next;
            }
        }

        return real;
    }

    private static void Follow(ref int links, string path)
    {
        if (++links > MostLinks)
        {
            throw new IOException(string.Format(LaunchMessages.TooManyLinks, path, MostLinks));
        }
    }
}
