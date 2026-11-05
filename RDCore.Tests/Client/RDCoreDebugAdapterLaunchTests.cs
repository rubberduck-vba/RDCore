using RDCore.SDK.Client;

namespace RDCore.Tests.Client;

/// <summary>
/// The preparation of a client that starts the debug adapter itself, as an IDE does before it debugs a workspace: what to start, where, with what, from the
/// manifest of the platform root, and what to say in the <c>launch</c> request.
/// </summary>
[TestClass]
public sealed class RDCoreDebugAdapterLaunchTests
{
    private string _root = null!;

    private static readonly string Executable = OperatingSystem.IsWindows() ? "RDCore.LanguageServer.exe" : "RDCore.LanguageServer";

    [TestInitialize]
    public void CreateRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), "RDCore", "dap-launch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "RDCore.LanguageServer"));
    }

    [TestCleanup]
    public void DeleteRoot() => Directory.Delete(_root, recursive: true);

    private void WithManifest(string langService) => File.WriteAllText(Path.Combine(_root, "rdcore.json"), $$"""
        {
            "langService":  "{{langService}}",
            "hostService":  "RDCore.CLI/rdc.exe",
            "platformVersion":  "0.1",
            "parseServer":  "RDCore.Parsing/RDCore.ParseServer.exe"
        }
        """);

    private void WithLanguageServer() => File.WriteAllText(Path.Combine(_root, "RDCore.LanguageServer", Executable), string.Empty);

    private RDCoreDebugAdapterLaunchOptions Options(string? module = null, string? entryPoint = null) => new()
    {
        PlatformRoot = _root,
        Workspace = "file:///c:/workspace",
        Module = module,
        EntryPoint = entryPoint,
    };

    [TestMethod]
    public void TheDebugAdapter_IsTheManifestsLanguageServer_StartedInItsFolder_AsADebugAdapterOfTheWorkspace_AndTheRootNamed()
    {
        WithManifest($"RDCore.LanguageServer/{Executable}");
        WithLanguageServer();

        var launch = RDCoreDebugAdapterLaunch.Prepare(Options());

        Assert.AreEqual(Path.Combine(_root, "RDCore.LanguageServer", Executable), launch.ExecutablePath);
        Assert.AreEqual(Path.Combine(_root, "RDCore.LanguageServer"), launch.WorkingDirectory);
        CollectionAssert.AreEqual(new[] { "--dap", "--workspace", "file:///c:/workspace" }, launch.Arguments.ToArray());
        Assert.AreEqual(_root, launch.Environment[RDCoreLanguageServerLaunch.PlatformRootEnvironmentVariable]);
        Assert.AreEqual("0.1", launch.Manifest.PlatformVersion);
        Assert.AreEqual("rdcore", RDCoreDebugAdapterLaunch.AdapterId);
    }

    [TestMethod]
    public void TheLaunch_SaysTheModuleAndTheEntryPoint_TheClientGave()
    {
        WithManifest($"RDCore.LanguageServer/{Executable}");
        WithLanguageServer();

        var launch = RDCoreDebugAdapterLaunch.Prepare(Options(module: "Program", entryPoint: "Start"));

        Assert.AreEqual("Program", launch.LaunchArguments["module"]);
        Assert.AreEqual("Start", launch.LaunchArguments["entryPoint"]);
        Assert.HasCount(2, launch.LaunchArguments);
    }

    // a project names no startup object: what the client does not say, the adapter decides.
    [TestMethod]
    [DataRow(null, DisplayName = "nothing given")]
    [DataRow(" ", DisplayName = "blank")]
    public void TheLaunch_SaysNothing_TheClientDidNotGive(string? given)
    {
        WithManifest($"RDCore.LanguageServer/{Executable}");
        WithLanguageServer();

        var launch = RDCoreDebugAdapterLaunch.Prepare(Options(module: given, entryPoint: given));

        Assert.IsEmpty(launch.LaunchArguments);
    }

    [TestMethod]
    public void TheProcessStartInfo_IsTheLaunch_SpokenToOverItsStandardInputAndOutput()
    {
        WithManifest($"RDCore.LanguageServer/{Executable}");
        WithLanguageServer();
        var launch = RDCoreDebugAdapterLaunch.Prepare(Options());

        var info = launch.ToProcessStartInfo();

        Assert.AreEqual(launch.ExecutablePath, info.FileName);
        Assert.AreEqual(launch.WorkingDirectory, info.WorkingDirectory);
        CollectionAssert.AreEqual(launch.Arguments.ToArray(), info.ArgumentList.ToArray());
        Assert.AreEqual(_root, info.Environment[RDCoreLanguageServerLaunch.PlatformRootEnvironmentVariable]);
        Assert.IsFalse(info.UseShellExecute);
        Assert.IsTrue(info.RedirectStandardInput);
        Assert.IsTrue(info.RedirectStandardOutput);
    }

    // the debug adapter is the language server's executable, and is refused for what the language server is.
    [TestMethod]
    public void AWorkspaceThatIsNotAnAbsoluteFileUri_IsRefused()
    {
        WithManifest($"RDCore.LanguageServer/{Executable}");
        WithLanguageServer();

        Assert.ThrowsExactly<ArgumentException>(() => RDCoreDebugAdapterLaunch.Prepare(Options() with { Workspace = "workspace" }));
    }

    [TestMethod]
    public void ARootWithNoManifest_IsSaid() => Assert.ThrowsExactly<FileNotFoundException>(() => RDCoreDebugAdapterLaunch.Prepare(Options()));

    [TestMethod]
    public void ALanguageServerThatIsNotThere_IsSaid()
    {
        WithManifest($"RDCore.LanguageServer/{Executable}");

        var thrown = Assert.ThrowsExactly<FileNotFoundException>(() => RDCoreDebugAdapterLaunch.Prepare(Options()));
        Assert.AreEqual(Path.Combine(_root, "RDCore.LanguageServer", Executable), thrown.FileName);
    }
}
