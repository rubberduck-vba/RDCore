using System.IO.Abstractions.TestingHelpers;
using RDCore.SDK.Platform;

namespace RDCore.Tests.Platform;

// mutates the RDCORE_PLATFORM_ROOT process environment variable.
[TestClass]
[DoNotParallelize]
public class PlatformEnvironmentTests
{
    private readonly MockFileSystem _fileSystem = new();
    private string _root = null!;

    [TestInitialize]
    public void SetRootOverride()
    {
        _root = _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine(_fileSystem.Path.GetTempPath(), "rdcore-platform"));
        Environment.SetEnvironmentVariable(PlatformEnvironment.RootEnvironmentVariable, _root);
    }

    [TestCleanup]
    public void ClearRootOverride()
        => Environment.SetEnvironmentVariable(PlatformEnvironment.RootEnvironmentVariable, null);

    private IPlatformEnvironment CreateSut() => new PlatformEnvironment(_fileSystem);

    [TestMethod]
    public void Root_UsesTheEnvironmentOverrideWhenSet()
        => Assert.AreEqual(_root, CreateSut().Root);

    [TestMethod]
    public void ManifestPath_IsRdcoreJsonUnderTheRoot()
        => Assert.AreEqual(_fileSystem.Path.Combine(_root, "rdcore.json"), CreateSut().ManifestPath);

    [TestMethod]
    public void Resolve_NormalisesForwardSlashesAndJoinsUnderTheRoot()
        => Assert.AreEqual(
            _fileSystem.Path.Combine(_root, "RDCore.Parsing", "RDCore.ParseServer.exe"),
            CreateSut().Resolve("RDCore.Parsing/RDCore.ParseServer.exe"));

    // a platform is installed where its user may not write (Program Files, /opt): what it writes goes to the user's folders.
    [TestMethod]
    public void LogsDirectory_IsTheUsersAndCreated_NotUnderTheRoot()
    {
        using var _ = Override(PlatformEnvironment.LogsEnvironmentVariable, null);

        var logs = CreateSut().LogsDirectory;

        Assert.AreEqual(UsersLogsDirectory(), logs);
        Assert.IsFalse(logs.StartsWith(_root, StringComparison.OrdinalIgnoreCase), logs);
        Assert.IsTrue(_fileSystem.Directory.Exists(logs));
    }

    [TestMethod]
    public void LogsDirectory_IsTheOverrideWhenSet()
    {
        var elsewhere = _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine(_fileSystem.Path.GetTempPath(), "rdcore-logs-elsewhere"));
        using var _ = Override(PlatformEnvironment.LogsEnvironmentVariable, elsewhere);

        var logs = CreateSut().LogsDirectory;

        Assert.AreEqual(elsewhere, logs);
        Assert.IsTrue(_fileSystem.Directory.Exists(logs));
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    [DataRow("/var/xdg-state", "/var/xdg-state/rdcore/logs", DisplayName = "XDG_STATE_HOME set")]
    [DataRow("relative/state", null, DisplayName = "a relative XDG_STATE_HOME is ignored")]
    [DataRow(null, null, DisplayName = "XDG_STATE_HOME not set: ~/.local/state")]
    public void LogsDirectory_OnLinux_IsInTheUsersXdgState(string? xdgStateHome, string? expected)
    {
        using var logs = Override(PlatformEnvironment.LogsEnvironmentVariable, null);
        using var state = Override("XDG_STATE_HOME", xdgStateHome);

        Assert.AreEqual(
            expected ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state", "rdcore", "logs"),
            CreateSut().LogsDirectory);
    }

    private static string UsersLogsDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RDCore", "Logs");
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(home, "Library", "Logs", "RDCore");
        }

        return Environment.GetEnvironmentVariable("XDG_STATE_HOME") is { Length: > 0 } xdg && Path.IsPathRooted(xdg)
            ? Path.Combine(xdg, "rdcore", "logs")
            : Path.Combine(home, ".local", "state", "rdcore", "logs");
    }

    // sets a process environment variable for the length of a test, and puts it back as it was.
    private static Restore Override(string variable, string? value)
    {
        var was = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, value);
        return new Restore(variable, was);
    }

    private sealed record Restore(string Variable, string? Was) : IDisposable
    {
        public void Dispose() => Environment.SetEnvironmentVariable(Variable, Was);
    }
}
