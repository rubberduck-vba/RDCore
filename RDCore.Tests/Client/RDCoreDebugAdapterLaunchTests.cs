using RDCore.SDK.Client;

namespace RDCore.Tests.Client;

/// <summary>
/// The preparation of a client that starts the debug adapter itself, as an IDE does before it debugs a workspace: what to start, where, with what, from the
/// manifest of the platform root, and what to say in the <c>launch</c> request.
/// </summary>
[TestClass]
public sealed class RDCoreDebugAdapterLaunchTests
{
    // the platform root, and beside it a folder that is not the platform's.
    private string _folder = null!;
    private string _root = null!;
    private string _outside = null!;

    private static readonly string Executable = OperatingSystem.IsWindows() ? "RDCore.LanguageServer.exe" : "RDCore.LanguageServer";

    [TestInitialize]
    public void CreateRoot()
    {
        _folder = Path.Combine(Path.GetTempPath(), "RDCore", "dap-launch", Guid.NewGuid().ToString("N"));
        _root = Path.Combine(_folder, "platform");
        _outside = Path.Combine(_folder, "outside");
        Directory.CreateDirectory(Path.Combine(_root, "RDCore.LanguageServer"));
        Directory.CreateDirectory(_outside);
    }

    [TestCleanup]
    public void DeleteRoot() => Directory.Delete(_folder, recursive: true);

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
    }

    // the adapter is given the workspace as a URI, escaped: a folder with a space or an accent in its name is one argument it parses.
    [TestMethod]
    public void TheWorkspace_IsGivenAsAnEscapedUri()
    {
        WithManifest($"RDCore.LanguageServer/{Executable}");
        WithLanguageServer();

        var launch = RDCoreDebugAdapterLaunch.Prepare(Options() with { Workspace = "file:///c:/Mes Données" });

        CollectionAssert.AreEqual(new[] { "--dap", "--workspace", "file:///c:/Mes%20Donn%C3%A9es" }, launch.Arguments.ToArray());
    }

    // the debug adapter is the language server's executable, and is confined to the platform root as the language server is.
    [TestMethod]
    public void ALanguageServerNamedOutsideTheRoot_IsRefused()
    {
        File.WriteAllText(Path.Combine(_outside, Executable), string.Empty);
        WithManifest($"../outside/{Executable}");

        var thrown = Assert.ThrowsExactly<InvalidDataException>(() => RDCoreDebugAdapterLaunch.Prepare(Options()));
        StringAssert.Contains(thrown.Message, "langService");
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
