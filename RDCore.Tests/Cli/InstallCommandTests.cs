using NSubstitute;
using RDCore.CLI.App.Commands;
using RDCore.CLI.App.Install;
using RDCore.CLI.ConsoleIO;
using RDCore.CLI.ConsoleIO.Model;
using RDCore.SDK.Client;
using RDCore.SDK.Platform;
using RDCore.Tests.Client;
using System.Diagnostics;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>rdc install</c> deploys the platform tree it runs from into the shared root, as the IDE add-in's installer does, and puts the current
/// version's <c>rdc</c> on the user's PATH. Every row works in a folder of its own: the shared root, the home folder and the user's
/// Windows <c>Path</c> are the test's, never the user's.
/// </summary>
[TestClass]
public sealed class InstallCommandTests
{
    // a cap on a hang, not a budget: each wait is on the event itself, which a loaded machine brings as late as it does.
    private static readonly TimeSpan SafetyCap = TimeSpan.FromMinutes(5);

    private string _folder = null!;
    private string _root = null!;
    private string _home = null!;
    private MemoryUserPath _windowsPath = null!;

    [TestInitialize]
    public void Initialize()
    {
        _folder = Path.Combine(Path.GetTempPath(), "RDCore", "install", Guid.NewGuid().ToString("N"));
        _root = Path.Combine(_folder, "Platform");
        _home = Path.Combine(_folder, "home");
        Directory.CreateDirectory(_home);
        _windowsPath = new MemoryUserPath();
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // a folder of the temporary folder; left for the system to clean.
        }
    }

    // a platform tree as a release archive unpacks: rdcore.json, the language server it names, and rdc.
    private string Tree(string version, string name = "tree")
    {
        var tree = Path.Combine(_folder, name + "-" + version);
        Directory.CreateDirectory(Path.Combine(tree, "RDCore.LanguageServer"));
        Directory.CreateDirectory(Path.Combine(tree, "RDCore.CLI"));
        var exe = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        File.WriteAllText(Path.Combine(tree, "RDCore.LanguageServer", "RDCore.LanguageServer" + exe), "server");
        File.WriteAllText(Path.Combine(tree, "RDCore.CLI", "rdc" + exe), "rdc " + version);
        File.WriteAllText(Path.Combine(tree, "rdcore.json"), $$"""
            {
              "platformVersion": "{{version}}",
              "rid": "{{(OperatingSystem.IsWindows() ? "win-x64" : "linux-x64")}}",
              "hostService": "RDCore.CLI/rdc{{exe}}",
              "langService": "RDCore.LanguageServer/RDCore.LanguageServer{{exe}}"
            }
            """);
        return tree;
    }

    private InstallCommand Command(string tree, IConsoleMessageWriter? writer = null, string? processPath = null)
        => new(writer ?? Substitute.For<IConsoleMessageWriter>(),
            new InstallLocations(tree, _root, _home, processPath ?? string.Empty, OperatingSystem.IsWindows() ? _windowsPath : null));

    private Task<int> InstallAsync(string tree, params string[] args) => Command(tree).ExecuteAsync(args, CancellationToken.None);

    private string State => File.ReadAllText(Path.Combine(_root, "installer.json"));

    private string Version(string version) => Path.Combine(_root, "versions", version);

    private string RdcOf(string version) => Path.Combine(Version(version), "RDCore.CLI", OperatingSystem.IsWindows() ? "rdc.exe" : "rdc");

    // what the user's PATH leads to: the folder in the Windows Path, or what ~/.local/bin/rdc links to.
    private string? OnPath()
    {
        if (OperatingSystem.IsWindows())
        {
            var ours = (_windowsPath.Value ?? string.Empty).Split(';').Where(entry => entry.StartsWith(Path.Combine(_root, "versions"), StringComparison.OrdinalIgnoreCase)).ToArray();
            return ours.Length == 1 ? Path.Combine(ours[0], "rdc.exe") : ours.Length == 0 ? null : string.Join(";", ours);
        }

        var link = new FileInfo(Path.Combine(_home, ".local", "bin", "rdc"));
        return link.LinkTarget;
    }

    [TestMethod]
    public async Task ATree_IsDeployedUnderItsVersion_AndMadeCurrent()
    {
        var tree = Tree("0.2.0");

        Assert.AreEqual(0, await InstallAsync(tree));

        Assert.AreEqual("rdc 0.2.0", File.ReadAllText(RdcOf("0.2.0")));
        Assert.IsTrue(File.Exists(Path.Combine(Version("0.2.0"), "rdcore.json")));
        // as the add-in's installer writes it: System.Text.Json's defaults.
        Assert.AreEqual("""{"Current":"0.2.0","Previous":null}""", State);
        CollectionAssert.AreEquivalent(new[] { "0.2.0" }, Directory.GetDirectories(Path.Combine(_root, "versions")).Select(Path.GetFileName).ToArray(),
            "no .partial folder is left");
        Assert.AreEqual(RdcOf("0.2.0"), OnPath());
    }

    [TestMethod]
    public async Task ANewerVersion_BecomesCurrent_AndTheOneBeforeIsPrevious()
    {
        await InstallAsync(Tree("0.2.0"));

        Assert.AreEqual(0, await InstallAsync(Tree("0.3.0")));

        Assert.AreEqual("""{"Current":"0.3.0","Previous":"0.2.0"}""", State);
        Assert.IsTrue(Directory.Exists(Version("0.2.0")), "a version is not removed");
        Assert.AreEqual(RdcOf("0.3.0"), OnPath(), "the PATH entry for the version before is replaced");
    }

    [TestMethod]
    public async Task AnOlderVersion_IsDeployedBeside_AndTheCurrentOneStays()
    {
        // the state the add-in's installer leaves: it deployed 0.3.0 after 0.2.0.
        await InstallAsync(Tree("0.3.0"), "--no-modify-path");
        File.WriteAllText(Path.Combine(_root, "installer.json"), """{"Current":"0.3.0","Previous":"0.2.0"}""");

        Assert.AreEqual(0, await InstallAsync(Tree("0.2.1")));

        Assert.IsTrue(File.Exists(RdcOf("0.2.1")));
        Assert.AreEqual("""{"Current":"0.3.0","Previous":"0.2.0"}""", State);
        Assert.AreEqual(RdcOf("0.3.0"), OnPath(), "the current version's rdc is the one on PATH");
    }

    [TestMethod]
    [DataRow("0.2.0-alpha.2", "0.2.0", "0.2.0")]
    [DataRow("0.2.0-alpha.2", "0.2.0-alpha.10", "0.2.0-alpha.10")]
    [DataRow("0.2.0-beta", "0.2.0-alpha.10", "0.2.0-beta")]
    [DataRow("0.2.0", "0.10.0", "0.10.0")]
    public async Task Versions_AreOrderedAsSemanticVersioningOrdersThem(string first, string second, string current)
    {
        await InstallAsync(Tree(first, "first"));
        await InstallAsync(Tree(second, "second"));

        StringAssert.StartsWith(State, $$"""{"Current":"{{current}}",""");
    }

    [TestMethod]
    public async Task AVersion_IsDeployedInLowercase_WithoutItsBuildMetadata()
    {
        Assert.AreEqual(0, await InstallAsync(Tree("0.2.0-RC.1+build.5")));

        Assert.IsTrue(File.Exists(RdcOf("0.2.0-rc.1")));
        Assert.AreEqual("""{"Current":"0.2.0-rc.1","Previous":null}""", State);
    }

    [TestMethod]
    public async Task AVersionDeployedAlready_IsNotChanged()
    {
        await InstallAsync(Tree("0.2.0"));
        var second = Tree("0.2.0", "other");
        File.WriteAllText(Path.Combine(second, "RDCore.CLI", OperatingSystem.IsWindows() ? "rdc.exe" : "rdc"), "another build");

        Assert.AreEqual(0, await InstallAsync(second));

        Assert.AreEqual("rdc 0.2.0", File.ReadAllText(RdcOf("0.2.0")));
        Assert.AreEqual(RdcOf("0.2.0"), OnPath());
    }

    [TestMethod]
    public async Task AVersionFolderThatIsNoWholePlatform_IsDeployedAgain()
    {
        // what is left of a deployment someone broke: a folder of the version, without its language server.
        Directory.CreateDirectory(Path.Combine(Version("0.2.0"), "RDCore.CLI"));
        File.WriteAllText(Path.Combine(Version("0.2.0"), "rdcore.json"), """{"platformVersion":"0.2.0","langService":"RDCore.LanguageServer/RDCore.LanguageServer"}""");

        Assert.AreEqual(0, await InstallAsync(Tree("0.2.0")));

        Assert.AreEqual("rdc 0.2.0", File.ReadAllText(RdcOf("0.2.0")));
    }

    [TestMethod]
    public async Task RunFromTheVersionItDeployed_InstallPutsItOnPathOnly()
    {
        await InstallAsync(Tree("0.2.0"), "--no-modify-path");

        Assert.AreEqual(0, await InstallAsync(Version("0.2.0")));

        Assert.AreEqual("""{"Current":"0.2.0","Previous":null}""", State);
        Assert.AreEqual(RdcOf("0.2.0"), OnPath());
    }

    [TestMethod]
    public async Task NoModifyPath_DeploysWithoutChangingPath()
    {
        Assert.AreEqual(0, await InstallAsync(Tree("0.2.0"), "--no-modify-path"));

        Assert.IsTrue(File.Exists(RdcOf("0.2.0")));
        Assert.IsNull(OnPath());
        Assert.IsFalse(File.Exists(Path.Combine(_home, ".profile")));
        Assert.AreEqual(0, _windowsPath.Writes);
    }

    [TestMethod]
    public async Task PathOnly_PutsTheCurrentVersionOnPath_FromAnywhere()
    {
        await InstallAsync(Tree("0.2.0"), "--no-modify-path");

        // as the add-in runs it, once its own installer has made a version current: the tree it runs from does not matter.
        Assert.AreEqual(0, await InstallAsync(Path.Combine(_folder, "nowhere"), "--path-only"));

        Assert.AreEqual(RdcOf("0.2.0"), OnPath());
    }

    [TestMethod]
    public async Task PathOnly_WithNothingInstalled_Fails()
    {
        Assert.AreEqual(1, await InstallAsync(Path.Combine(_folder, "nowhere"), "--path-only"));

        Assert.IsNull(OnPath());
    }

    [TestMethod]
    public async Task PathOnlyAndNoModifyPath_AreRefusedTogether()
        => Assert.AreEqual(2, await InstallAsync(Tree("0.2.0"), "--path-only", "--no-modify-path"));

    // the body of every error the command writes.
    private static IConsoleMessageWriter Recording(List<string> errors)
    {
        var writer = Substitute.For<IConsoleMessageWriter>();
        writer.When(w => w.WriteMessage(Arg.Is<ConsoleMessageBuilder>(message => message.Kind == MessageKind.Error)))
            .Do(call => errors.Add(string.Concat(call.Arg<ConsoleMessageBuilder>().Parts.OfType<ConsoleMessageBodyPart>().Select(part => part.Body))));
        return writer;
    }

    [TestMethod]
    [DataRow("no language server")]
    [DataRow("no manifest")]
    [DataRow("a language server outside")]
    public async Task AFolderThatIsNoPlatformTree_IsRefused_WithTheReasonTheSdkGives(string flaw)
    {
        var tree = Tree("0.2.0");
        var exe = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        switch (flaw)
        {
            case "no language server":
                File.Delete(Path.Combine(tree, "RDCore.LanguageServer", "RDCore.LanguageServer" + exe));
                break;
            case "no manifest":
                File.Delete(Path.Combine(tree, "rdcore.json"));
                break;
            default:
                Directory.CreateDirectory(Path.Combine(_folder, "outside"));
                File.WriteAllText(Path.Combine(_folder, "outside", "RDCore.LanguageServer" + exe), "server");
                File.WriteAllText(Path.Combine(tree, "rdcore.json"), $$"""{ "platformVersion": "0.2.0", "langService": "../outside/RDCore.LanguageServer{{exe}}" }""");
                break;
        }

        // the SDK's own reason (its LaunchMessages), as it gives it for the tree.
        var reason = Assert.Throws<Exception>(() => RDCoreLanguageServerLaunch.FindLanguageServer(tree)).Message;
        var errors = new List<string>();

        Assert.AreEqual(1, await Command(tree, Recording(errors)).ExecuteAsync([], CancellationToken.None));

        CollectionAssert.AreEqual(new[] { string.Format(RDCore.CLI.Resources.Install_NotATree, reason) }, errors);
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "versions")));
    }

    [TestMethod]
    public async Task ATreeWithNoVersion_IsRefused()
    {
        var tree = Tree("0.2.0");
        File.WriteAllText(Path.Combine(tree, "rdcore.json"), File.ReadAllText(Path.Combine(tree, "rdcore.json")).Replace("0.2.0", "latest"));

        Assert.AreEqual(1, await InstallAsync(tree));
    }

    [TestMethod]
    public async Task AnInstallation_WaitsForTheOneThatHoldsTheRoot()
    {
        var tree = Tree("0.2.0");
        Directory.CreateDirectory(_root);
        var waiting = new TaskCompletionSource();
        var writer = Substitute.For<IConsoleMessageWriter>();
        writer.When(w => w.WriteMessage(Arg.Is<ConsoleMessageBuilder>(message => message.Kind == MessageKind.Information))).Do(_ => waiting.TrySetResult());

        Task<int> installing;
        // as the add-in's installer holds it: open, unshared.
        using (new FileStream(Path.Combine(_root, "install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            installing = Command(tree, writer).ExecuteAsync([], CancellationToken.None);
            await waiting.Task.WaitAsync(SafetyCap);
            Assert.IsFalse(Directory.Exists(Path.Combine(_root, "versions")), "nothing is deployed while another installation holds the root");
        }

        Assert.AreEqual(0, await installing.WaitAsync(SafetyCap));
        Assert.IsTrue(File.Exists(RdcOf("0.2.0")));
    }

    // rdc holds the version it runs from until it exits: the add-in's installer, which keeps the current version and the one before it,
    // removes an older one only once rdc has released it.
    [TestMethod]
    public async Task AVersionRdcRunsFrom_IsRemovedOnlyOnceRdcReleasesIt()
    {
        foreach (var version in new[] { "0.1.0", "0.2.0", "0.3.0" })
        {
            Assert.AreEqual(0, await InstallAsync(Tree(version), "--no-modify-path"));
        }

        var root = new SharedPlatformRoot(_root);
        using (var lease = PlatformTree.Hold(Version("0.1.0")))
        {
            Assert.IsNotNull(lease);
            await RemoveOldVersionsAsync(root);
            Assert.IsTrue(File.Exists(RdcOf("0.1.0")), "a version rdc holds is not removed");
        }

        await RemoveOldVersionsAsync(root);
        Assert.IsFalse(Directory.Exists(Version("0.1.0")), "a version rdc has released is removed");
        Assert.IsTrue(File.Exists(RdcOf("0.2.0")), "the version before the current one is kept");
    }

    // as the add-in's installer removes them, with the root held.
    private static async Task RemoveOldVersionsAsync(SharedPlatformRoot root)
    {
        using var _ = await root.LockAsync(null, CancellationToken.None);
        root.RemoveOldVersions(PlatformVersion.Parse("0.3.0"));
    }

    // a platform no deployment put there, such as a build tree that is not published, has no version to hold.
    [TestMethod]
    [DataRow(null, DisplayName = "no manifest")]
    [DataRow("""{ "langService": "RDCore.LanguageServer/RDCore.LanguageServer" }""", DisplayName = "no version")]
    [DataRow("not a manifest", DisplayName = "not a manifest")]
    public void APlatformWithNoVersion_HoldsNothing(string? manifest)
    {
        var folder = Path.Combine(_folder, "build");
        Directory.CreateDirectory(folder);
        if (manifest is not null)
        {
            File.WriteAllText(Path.Combine(folder, "rdcore.json"), manifest);
        }

        Assert.IsNull(PlatformTree.Hold(folder));
    }

    [TestMethod]
    public async Task OnWindows_ThePathKeepsTheUsersEntriesAsTheyAre()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("the user's Path is Windows'");
        }

        _windowsPath.Value = @"C:\Tools;%USERPROFILE%\bin;" + Path.Combine(_root, "versions", "0.1.0", "RDCore.CLI");

        await InstallAsync(Tree("0.2.0"));
        Assert.AreEqual(@"C:\Tools;%USERPROFILE%\bin;" + Path.GetDirectoryName(RdcOf("0.2.0")), _windowsPath.Value);
        Assert.AreEqual(1, _windowsPath.Writes);

        await InstallAsync(Version("0.2.0"), "--path-only");
        Assert.AreEqual(1, _windowsPath.Writes, "a Path that has the current version already is not written again");
    }

    [TestMethod]
    public async Task OffWindows_ShellsThatDoNotHaveLocalBin_AreGivenItInTheLoginProfile_Once()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("~/.local/bin is Linux's and macOS's");
        }

        var profile = Path.Combine(_home, OperatingSystem.IsMacOS() ? ".zprofile" : ".profile");
        File.WriteAllText(profile, "export EDITOR=vi");

        await InstallAsync(Tree("0.2.0"));
        await InstallAsync(Version("0.2.0"), "--path-only");

        var text = File.ReadAllText(profile);
        StringAssert.StartsWith(text, "export EDITOR=vi\n# >>> rdc install >>>\n");
        Assert.AreEqual(1, text.Split("# >>> rdc install >>>").Length - 1);
        StringAssert.Contains(text, "export PATH=\"$HOME/.local/bin:$PATH\"");
    }

    [TestMethod]
    public async Task OffWindows_AShellThatHasLocalBin_IsLeftAsItIs()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("~/.local/bin is Linux's and macOS's");
        }

        var local = Path.Combine(_home, ".local", "bin");
        Assert.AreEqual(0, await Command(Tree("0.2.0"), processPath: "/usr/bin:" + local + "/").ExecuteAsync([], CancellationToken.None));

        Assert.AreEqual(RdcOf("0.2.0"), OnPath());
        Assert.IsFalse(File.Exists(Path.Combine(_home, ".profile")));
        Assert.IsFalse(File.Exists(Path.Combine(_home, ".zprofile")));
    }

    [TestMethod]
    public async Task OffWindows_AnRdcOfTheUsersOwn_IsNotReplaced()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("~/.local/bin is Linux's and macOS's");
        }

        var link = Path.Combine(_home, ".local", "bin", "rdc");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        File.WriteAllText(link, "#!/bin/sh");

        Assert.AreEqual(1, await InstallAsync(Tree("0.2.0")));

        Assert.AreEqual("#!/bin/sh", File.ReadAllText(link));
        Assert.IsTrue(File.Exists(RdcOf("0.2.0")), "the platform is deployed all the same");
    }

    // the apphost finds the application beside it, rdc.dll, through the link: it resolves its own path to the file the link names.
    [TestMethod]
    public async Task OffWindows_TheLinkedRdc_Starts()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("~/.local/bin is Linux's and macOS's");
        }

        var link = Path.Combine(_home, ".local", "bin", "rdc");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        File.CreateSymbolicLink(link, Path.Combine(TestPlatformRoot.Directory, "Platform", "rdc"));

        var start = new ProcessStartInfo(link, ["no-such-command"]) { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = _home };
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(SafetyCap);

        // 2 is rdc's own answer to a command it does not have: the apphost found rdc.dll and the runtime ran it.
        Assert.AreEqual(2, process.ExitCode, $"{await output}\n{await error}");
    }

    private sealed class MemoryUserPath : IUserPathVariable
    {
        public string? Value { get; set; }

        public int Writes { get; private set; }

        public string? Read() => Value;

        public void Write(string value)
        {
            Value = value;
            Writes++;
        }
    }
}
