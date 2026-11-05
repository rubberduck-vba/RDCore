using RDCore.SDK.Platform;
using System.Text.Json;

namespace RDCore.Tests.Client;

/// <summary>
/// A platform root as a deployment has it, for the tests that start the language server as an IDE does (<see cref="RDCore.SDK.Client.RDCoreLanguageServerLaunch"/>):
/// the components this test run built, under the root, and a manifest that names them relative to it. A launch runs only the executables under its root, so the
/// test run's own folder is copied in, once for the run; nothing the platform runs writes to its root, and every test that starts it shares this one.
/// </summary>
[TestClass]
public static class TestPlatformRoot
{
    // the folder under the root the build output is copied to.
    private const string Components = "Platform";

    // what a root's folder is named, under the folder the roots are made in.
    private const string Prefix = "platform-";

    // a root left by a run that was stopped before its cleanup (a killed test host) is taken away by a later run, once it is this old: a run that is still
    // going is never as old as that.
    internal static readonly TimeSpan Abandoned = TimeSpan.FromDays(1);

    private static readonly Lock Gate = new();
    private static string? _directory;

    /// <summary>The root, made the first time a test asks for it. A copy that failed is not kept: the next test to ask makes the root again.</summary>
    public static string Directory
    {
        get
        {
            lock (Gate)
            {
                return _directory ??= Create(Path.Combine(Path.GetTempPath(), "RDCore"), new DirectoryInfo(AppContext.BaseDirectory), DateTime.UtcNow);
            }
        }
    }

    private static string AppHost(string name) => $"{Components}/{(OperatingSystem.IsWindows() ? $"{name}.exe" : name)}";

    /// <summary>
    /// Makes a root in <paramref name="folder"/>, with a copy of <paramref name="components"/>, after taking away the roots there that were abandoned at
    /// <paramref name="now"/>. A copy that fails takes away the root it was making.
    /// </summary>
    internal static string Create(string folder, DirectoryInfo components, DateTime now)
    {
        Sweep(folder, now);

        var root = Path.Combine(folder, $"{Prefix}{Environment.ProcessId}-{Guid.NewGuid():N}");
        try
        {
            Copy(components, System.IO.Directory.CreateDirectory(Path.Combine(root, Components)));
            File.WriteAllText(Path.Combine(root, PlatformManifest.FileName), JsonSerializer.Serialize(new PlatformManifest
            {
                PlatformVersion = "0.1",
                GeneratedUtc = DateTime.UtcNow,
                ExtensionsDirectory = "Extensions",
                LangService = AppHost("RDCore.LanguageServer"),
                ParseServer = AppHost("RDCore.ParseServer"),
                HostService = AppHost("rdc"),
            }));
            return root;
        }
        catch
        {
            Delete(root);
            throw;
        }
    }

    /// <summary>
    /// Takes away the roots in <paramref name="folder"/> that were last written <see cref="Abandoned"/> or longer before <paramref name="now"/>, of a run whose
    /// process is not running, and no other folder.
    /// </summary>
    /// <remarks>
    /// A root's age is when it was made, not how long its run has to go: a run paused in a debugger, or another's long run on the same machine, is still using
    /// one older than that. Deleting a folder deletes every file no process holds before it fails on one that is held, so a root is taken away only once the
    /// process its name gives (<c>platform-&lt;pid&gt;-&lt;guid&gt;</c>) is not running.
    /// </remarks>
    internal static void Sweep(string folder, DateTime now)
    {
        if (!System.IO.Directory.Exists(folder))
        {
            return;
        }

        foreach (var root in new DirectoryInfo(folder).EnumerateDirectories($"{Prefix}*"))
        {
            if (now - root.LastWriteTimeUtc >= Abandoned && !IsRunning(root.Name))
            {
                Delete(root.FullName);
            }
        }
    }

    // whether the process that made a root is running. A process that has since taken its id keeps the root until it ends, which errs on the side of keeping it.
    private static bool IsRunning(string root)
    {
        var id = root[Prefix.Length..].Split('-')[0];
        if (!int.TryParse(id, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var processId))
        {
            return false;
        }

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static void Copy(DirectoryInfo from, DirectoryInfo to)
    {
        foreach (var file in from.EnumerateFiles())
        {
            // the copy keeps the mode of the file it copies, so an app host is still an executable elsewhere than on Windows.
            file.CopyTo(Path.Combine(to.FullName, file.Name));
        }

        foreach (var directory in from.EnumerateDirectories())
        {
            Copy(directory, to.CreateSubdirectory(directory.Name));
        }
    }

    private static void Delete(string root)
    {
        try
        {
            if (System.IO.Directory.Exists(root))
            {
                System.IO.Directory.Delete(root, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // a process of a test that timed out may still hold a file: the folder is a temporary one, and a later run's sweep takes it away.
        }
    }

    [AssemblyCleanup]
    public static void Cleanup()
    {
        lock (Gate)
        {
            if (_directory is { } root)
            {
                Delete(root);
            }
        }
    }
}
