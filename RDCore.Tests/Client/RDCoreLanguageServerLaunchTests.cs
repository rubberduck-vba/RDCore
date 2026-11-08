using RDCore.SDK.Client;

namespace RDCore.Tests.Client;

/// <summary>
/// The pre-launch preparation of a client that starts the language server itself, as an IDE does before it starts a server it registered: what to start, where,
/// with what, from the manifest of the platform root.
/// </summary>
[TestClass]
public sealed class RDCoreLanguageServerLaunchTests
{
    // the platform root, and beside it a folder that is not the platform's.
    private string _folder = null!;
    private string _root = null!;
    private string _outside = null!;

    private static readonly string Executable = OperatingSystem.IsWindows() ? "RDCore.LanguageServer.exe" : "RDCore.LanguageServer";

    [TestInitialize]
    public void CreateRoot()
    {
        _folder = Path.Combine(Path.GetTempPath(), "RDCore", "launch", Guid.NewGuid().ToString("N"));
        _root = Path.Combine(_folder, "platform");
        _outside = Path.Combine(_folder, "outside");
        Directory.CreateDirectory(Path.Combine(_root, "RDCore.LanguageServer"));
        Directory.CreateDirectory(_outside);
    }

    [TestCleanup]
    public void DeleteRoot()
    {
        // a link is taken away as a link, before the folders, which Windows does not delete through a junction.
        foreach (var link in Directory.EnumerateDirectories(_root).Where(folder => new DirectoryInfo(folder).LinkTarget is not null))
        {
            Directory.Delete(link);
        }

        Directory.Delete(_folder, recursive: true);
    }

    private void WithManifest(string json) => File.WriteAllText(Path.Combine(_root, "rdcore.json"), json);

    private void WithLanguageServer() => File.WriteAllText(Path.Combine(_root, "RDCore.LanguageServer", Executable), string.Empty);

    private static string Manifest(string langService, string hostService = "RDCore.CLI/rdc.exe", string parseServer = "RDCore.Parsing/RDCore.ParseServer.exe") => $$"""
        {
            "generatedUtc":  "2026-10-29T12:41:21.3333457Z",
            "extensionsDirectory":  "Extensions",
            "langService":  "{{Json(langService)}}",
            "hostService":  "{{Json(hostService)}}",
            "platformVersion":  "0.1",
            "parseServer":  "{{Json(parseServer)}}"
        }
        """;

    // a path as a JSON string has it.
    private static string Json(string path) => path.Replace(@"\", @"\\");

    private RDCoreLanguageServerLaunchOptions Options(string? pipe = null, int? clientProcessId = null) => new()
    {
        PlatformRoot = _root,
        Workspace = "file:///c:/workspace",
        PipeName = pipe,
        ClientProcessId = clientProcessId,
    };

    [TestMethod]
    public void TheLanguageServer_IsTheManifests_StartedInItsFolder_WithThePipeAndTheWorkspace_AndTheRootNamed()
    {
        WithManifest(Manifest($"RDCore.LanguageServer/{Executable}"));
        WithLanguageServer();

        var launch = RDCoreLanguageServerLaunch.Prepare(Options(pipe: "RDCore.Test.Pipe"));

        Assert.AreEqual(Path.Combine(_root, "RDCore.LanguageServer", Executable), launch.ExecutablePath);
        Assert.AreEqual(Path.Combine(_root, "RDCore.LanguageServer"), launch.WorkingDirectory);
        CollectionAssert.AreEqual(new[] { "--pipe-name", "RDCore.Test.Pipe", "--workspace", "file:///c:/workspace" }, launch.Arguments.ToArray());
        Assert.AreEqual("RDCore.Test.Pipe", launch.PipeName);
        Assert.AreEqual(_root, launch.Environment[RDCoreLanguageServerLaunch.PlatformRootEnvironmentVariable]);
        Assert.AreEqual("0.1", launch.Manifest.PlatformVersion);
        Assert.AreEqual("RDCore.CLI/rdc.exe", launch.Manifest.HostService);
    }

    [TestMethod]
    public void AManifestPath_WithBackslashes_IsFoundAsWell()
    {
        // a backslash, which the manifest escapes in its JSON as PlatformPublish.ps1 would write one.
        WithManifest(Manifest($@"RDCore.LanguageServer\{Executable}"));
        WithLanguageServer();

        var launch = RDCoreLanguageServerLaunch.Prepare(Options());

        Assert.AreEqual(Path.Combine(_root, "RDCore.LanguageServer", Executable), launch.ExecutablePath);
    }

    [TestMethod]
    public void WithNoPipeName_ANewOneIsMade_EachTime()
    {
        WithManifest(Manifest($"RDCore.LanguageServer/{Executable}"));
        WithLanguageServer();

        var first = RDCoreLanguageServerLaunch.Prepare(Options());
        var second = RDCoreLanguageServerLaunch.Prepare(Options());

        StringAssert.StartsWith(first.PipeName, "RDCore.LanguageServer.Pipe.");
        Assert.AreNotEqual(first.PipeName, second.PipeName);
        Assert.AreEqual(first.PipeName, first.Arguments[1]);
    }

    [TestMethod]
    public void TheClientsProcess_IsPassedWhenThereIsOne()
    {
        WithManifest(Manifest($"RDCore.LanguageServer/{Executable}"));
        WithLanguageServer();

        var launch = RDCoreLanguageServerLaunch.Prepare(Options(pipe: "p", clientProcessId: 4242));

        CollectionAssert.AreEqual(new[] { "--pipe-name", "p", "--workspace", "file:///c:/workspace", "--client-process-id", "4242" }, launch.Arguments.ToArray());
    }

    [TestMethod]
    public void TheProcessStartInfo_IsTheLaunch()
    {
        WithManifest(Manifest($"RDCore.LanguageServer/{Executable}"));
        WithLanguageServer();
        var launch = RDCoreLanguageServerLaunch.Prepare(Options(pipe: "p"));

        var info = launch.ToProcessStartInfo();

        Assert.AreEqual(launch.ExecutablePath, info.FileName);
        Assert.AreEqual(launch.WorkingDirectory, info.WorkingDirectory);
        CollectionAssert.AreEqual(launch.Arguments.ToArray(), info.ArgumentList.ToArray());
        Assert.AreEqual(_root, info.Environment[RDCoreLanguageServerLaunch.PlatformRootEnvironmentVariable]);
        Assert.IsFalse(info.UseShellExecute);
    }

    [TestMethod]
    [DataRow("RDCore.LanguageServer.exe", "RDCore.LanguageServer", DisplayName = "named for Windows, deployed elsewhere")]
    [DataRow("RDCore.LanguageServer", "RDCore.LanguageServer.exe", DisplayName = "named without the extension, deployed on Windows")]
    public void TheLanguageServer_IsFound_WithOrWithoutItsExtension(string named, string deployed)
    {
        WithManifest(Manifest($"RDCore.LanguageServer/{named}"));
        File.WriteAllText(Path.Combine(_root, "RDCore.LanguageServer", deployed), string.Empty);

        var launch = RDCoreLanguageServerLaunch.Prepare(Options());

        Assert.AreEqual(Path.Combine(_root, "RDCore.LanguageServer", deployed), launch.ExecutablePath);
    }

    // the server is given the workspace as a URI, escaped, as LSP has it: a folder with a space or an accent in its name is still one argument the server parses.
    [TestMethod]
    [DataRow("file:///c:/workspace", "file:///c:/workspace", DisplayName = "a URI")]
    [DataRow("file:///c:/My Folder", "file:///c:/My%20Folder", DisplayName = "a space")]
    [DataRow("file:///c:/Données", "file:///c:/Donn%C3%A9es", DisplayName = "an accent")]
    [DataRow("file:///c:/My%20Folder", "file:///c:/My%20Folder", DisplayName = "escaped already")]
    public void TheWorkspace_IsGivenAsAnEscapedUri(string workspace, string given)
    {
        WithManifest(Manifest($"RDCore.LanguageServer/{Executable}"));
        WithLanguageServer();

        var launch = RDCoreLanguageServerLaunch.Prepare(Options(pipe: "p") with { Workspace = workspace });

        CollectionAssert.AreEqual(new[] { "--pipe-name", "p", "--workspace", given }, launch.Arguments.ToArray());
    }

    // an absolute path is the URI of that folder on the platform it is a path of.
    [TestMethod]
    public void AWorkspaceThatIsAnAbsolutePath_IsGivenAsItsUri()
    {
        WithManifest(Manifest($"RDCore.LanguageServer/{Executable}"));
        WithLanguageServer();
        var (path, given) = OperatingSystem.IsWindows()
            ? (@"C:\My Folder\Données", "file:///C:/My%20Folder/Donn%C3%A9es")
            : ("/home/u/My Folder/Données", "file:///home/u/My%20Folder/Donn%C3%A9es");

        var launch = RDCoreLanguageServerLaunch.Prepare(Options(pipe: "p") with { Workspace = path });

        Assert.AreEqual(given, launch.Arguments[3]);
    }

    [TestMethod]
    [DataRow("workspace", DisplayName = "a relative path")]
    [DataRow("https://example.com/workspace", DisplayName = "not a file: URI")]
    [DataRow("", DisplayName = "nothing")]
    public void AWorkspaceThatIsNotAnAbsoluteFileUri_IsRefused(string workspace)
    {
        WithManifest(Manifest($"RDCore.LanguageServer/{Executable}"));
        WithLanguageServer();

        Assert.ThrowsExactly<ArgumentException>(() => RDCoreLanguageServerLaunch.Prepare(Options() with { Workspace = workspace }));
    }

    [TestMethod]
    public void ARootThatIsNotThere_IsSaid() => Assert.ThrowsExactly<DirectoryNotFoundException>(
        () => RDCoreLanguageServerLaunch.Prepare(Options() with { PlatformRoot = Path.Combine(_root, "nowhere") }));

    [TestMethod]
    public void ARootWithNoManifest_IsSaid() => Assert.ThrowsExactly<FileNotFoundException>(() => RDCoreLanguageServerLaunch.Prepare(Options()));

    [TestMethod]
    public void AManifestThatIsNotOne_IsSaid()
    {
        WithManifest("{ not json");

        Assert.ThrowsExactly<InvalidDataException>(() => RDCoreLanguageServerLaunch.Prepare(Options()));
    }

    [TestMethod]
    public void AManifestThatNamesNoLanguageServer_IsSaid()
    {
        WithManifest(Manifest(string.Empty));

        Assert.ThrowsExactly<InvalidDataException>(() => RDCoreLanguageServerLaunch.Prepare(Options()));
    }

    [TestMethod]
    public void ALanguageServerThatIsNotThere_IsSaid()
    {
        WithManifest(Manifest($"RDCore.LanguageServer/{Executable}"));

        var thrown = Assert.ThrowsExactly<FileNotFoundException>(() => RDCoreLanguageServerLaunch.Prepare(Options()));
        Assert.AreEqual(Path.Combine(_root, "RDCore.LanguageServer", Executable), thrown.FileName);
    }

    // a platform starts only its own executables: one the manifest names elsewhere is refused, there or not, and so is the launch.
    [TestMethod]
    [DataRow("langService", DisplayName = "the language server")]
    [DataRow("hostService", DisplayName = "the environment host")]
    [DataRow("parseServer", DisplayName = "the parse server")]
    public void AnExecutableNamedOutsideTheRoot_ThroughDotDot_IsRefused(string property)
    {
        File.WriteAllText(Path.Combine(_outside, Executable), string.Empty);
        WithLanguageServer();
        WithManifest(ManifestNaming(property, $"../outside/{Executable}"));

        var thrown = Assert.ThrowsExactly<InvalidDataException>(() => RDCoreLanguageServerLaunch.Prepare(Options()));
        StringAssert.Contains(thrown.Message, property);
    }

    [TestMethod]
    [DataRow("langService", DisplayName = "the language server")]
    [DataRow("hostService", DisplayName = "the environment host")]
    [DataRow("parseServer", DisplayName = "the parse server")]
    public void AnExecutableNamedOutsideTheRoot_ByAnAbsolutePath_IsRefused(string property)
    {
        var elsewhere = Path.Combine(_outside, Executable);
        File.WriteAllText(elsewhere, string.Empty);
        WithLanguageServer();
        WithManifest(ManifestNaming(property, elsewhere));

        var thrown = Assert.ThrowsExactly<InvalidDataException>(() => RDCoreLanguageServerLaunch.Prepare(Options()));
        StringAssert.Contains(thrown.Message, property);
    }

    // Path.GetFullPath folds "..", but goes through a link as if it were the folder: the folder a link leads to is where the executable is.
    [TestMethod]
    public void AnExecutableInAFolderThatLinksOutsideTheRoot_IsRefused()
    {
        File.WriteAllText(Path.Combine(_outside, Executable), string.Empty);
        WithLinkedFolder(Path.Combine(_root, "Linked"), _outside);
        WithManifest(Manifest($"Linked/{Executable}"));

        var thrown = Assert.ThrowsExactly<InvalidDataException>(() => RDCoreLanguageServerLaunch.Prepare(Options()));
        StringAssert.Contains(thrown.Message, "langService");
    }

    [TestMethod]
    public void AnExecutableNamedThroughDotDot_ThatStaysUnderTheRoot_IsFound()
    {
        WithManifest(Manifest($"RDCore.CLI/../RDCore.LanguageServer/{Executable}"));
        WithLanguageServer();

        var launch = RDCoreLanguageServerLaunch.Prepare(Options());

        Assert.AreEqual(Path.Combine(_root, "RDCore.LanguageServer", Executable), launch.ExecutablePath);
    }

    private static string ManifestNaming(string property, string path) => property switch
    {
        "langService" => Manifest(path),
        "hostService" => Manifest($"RDCore.LanguageServer/{Executable}", hostService: path),
        _ => Manifest($"RDCore.LanguageServer/{Executable}", parseServer: path),
    };

    // a symbolic link where the system lets this process make one. Windows asks for Developer Mode or elevation for that, and makes a junction without either:
    // a folder that leads elsewhere all the same.
    internal static void WithLinkedFolder(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }
        catch (Exception exception) when (OperatingSystem.IsWindows() && exception is IOException or UnauthorizedAccessException)
        {
        }

        using var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            ArgumentList = { "/d", "/c", "mklink", "/J", link, target },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        mklink.WaitForExit();
        if (mklink.ExitCode != 0 || !Directory.Exists(link))
        {
            Assert.Inconclusive($"this process may make neither a symbolic link nor a junction here: {mklink.StandardError.ReadToEnd()}");
        }
    }
}
