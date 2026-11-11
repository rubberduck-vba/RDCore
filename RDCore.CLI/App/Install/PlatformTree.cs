using RDCore.SDK.Client;
using RDCore.SDK.Platform;

namespace RDCore.CLI.App.Install;

/// <summary>
/// The platform tree <c>rdc install</c> deploys: a folder that holds a whole platform, as a release archive unpacks one, copied into the
/// shared root by the rules of the SDK's <see cref="SharedPlatformRoot"/>, which the IDE add-in's installer deploys by too.
/// </summary>
/// <remarks>
/// Versions are not removed here. The add-in's installer keeps the current one and the one before it, and removes the others that no
/// lease holds (<see cref="SharedPlatformRoot.RemoveOldVersions"/>, <see cref="SharedPlatformRoot.Hold"/>).
/// </remarks>
internal static class PlatformTree
{
    /// <summary>
    /// Why <paramref name="folder"/> is not a whole platform, or null when it is: the reason
    /// <see cref="RDCoreLanguageServerLaunch.FindLanguageServer"/> gives.
    /// </summary>
    /// <param name="folder">The folder of a platform's <c>rdcore.json</c>.</param>
    public static string? Flaw(string folder)
    {
        try
        {
            _ = RDCoreLanguageServerLaunch.FindLanguageServer(folder);
            return null;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            return exception.Message;
        }
    }

    /// <summary>
    /// Copies the platform tree at <paramref name="tree"/> into <paramref name="root"/> as <paramref name="version"/>, unless that version is
    /// deployed already: true when it copied it. It is copied into the version's <see cref="SharedPlatformRoot.PartialRoot"/>, then moved
    /// into place. Call it with the root held.
    /// </summary>
    /// <param name="root">The shared root.</param>
    /// <param name="tree">The platform tree to copy: the folder of its <c>rdcore.json</c>.</param>
    /// <param name="version">The version it is deployed as.</param>
    public static bool Deploy(SharedPlatformRoot root, string tree, PlatformVersion version)
    {
        if (SharedPlatformRoot.IsWhole(root.VersionRoot(version)))
        {
            return false;
        }

        var partial = root.PartialRoot(version);
        try
        {
            DeleteFolder(partial);
            CopyFolder(tree, partial);
            root.MoveIntoPlace(version);
        }
        finally
        {
            DeleteFolder(partial);
        }

        return true;
    }

    /// <summary>
    /// Holds the version of the platform at <paramref name="folder"/>, the one <c>rdc</c> runs from, so that no deployment into the shared
    /// root removes it while the lease is held (<see cref="SharedPlatformRoot.Hold"/>). A folder outside the shared root is held the same
    /// way, and no deployment removes it either. Null when the folder has no manifest, or one that is not a manifest or names no version,
    /// such as a build tree that is not published.
    /// </summary>
    /// <param name="folder">The platform root: the folder of its <c>rdcore.json</c>.</param>
    /// <returns>The lease, to dispose of at exit; or null.</returns>
    public static IDisposable? Hold(string folder)
    {
        try
        {
            return PlatformVersion.TryParse(RDCoreLanguageServerLaunch.ReadManifest(folder).PlatformVersion, out var version)
                ? SharedPlatformRoot.Hold(new DeployedPlatform(version, folder))
                : null;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// How paths compare on this system: without case on Windows.
    /// </summary>
    public static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

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
}
