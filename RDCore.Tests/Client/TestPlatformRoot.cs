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

    private static readonly Lazy<string> _directory = new(Create, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The root, made the first time a test asks for it.</summary>
    public static string Directory => _directory.Value;

    private static string AppHost(string name) => $"{Components}/{(OperatingSystem.IsWindows() ? $"{name}.exe" : name)}";

    private static string Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "RDCore", $"platform-{Environment.ProcessId}-{Guid.NewGuid():N}");
        Copy(new DirectoryInfo(AppContext.BaseDirectory), System.IO.Directory.CreateDirectory(Path.Combine(root, Components)));
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

    [AssemblyCleanup]
    public static void Cleanup()
    {
        if (!_directory.IsValueCreated)
        {
            return;
        }

        try
        {
            System.IO.Directory.Delete(_directory.Value, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // a process of a test that timed out may still hold a file: the folder is a temporary one.
        }
    }
}
