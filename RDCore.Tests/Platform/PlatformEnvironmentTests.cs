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

    [TestMethod]
    public void LogsDirectory_IsUnderTheRootAndCreated()
    {
        // act
        var logs = CreateSut().LogsDirectory;

        // assert
        Assert.AreEqual(_fileSystem.Path.Combine(_root, "Logs"), logs);
        Assert.IsTrue(_fileSystem.Directory.Exists(logs));
    }
}
