namespace RDCore.Tests.Client;

/// <summary>
/// The platform root the tests that start the language server share (<see cref="TestPlatformRoot"/>): a copy that fails leaves nothing behind, and a root a
/// stopped run left is taken away by a later one.
/// </summary>
[TestClass]
public sealed class TestPlatformRootTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 8, 0, 0, DateTimeKind.Utc);

    // an id no process has: Windows' and Linux's are far below it.
    private const int NotRunning = 2147483644;

    // the folder the roots are made in, and a build output to copy.
    private string _folder = null!;
    private string _components = null!;

    [TestInitialize]
    public void CreateFolder()
    {
        var test = Path.Combine(Path.GetTempPath(), "RDCore", "rootclean", Guid.NewGuid().ToString("N"));
        _folder = Path.Combine(test, "roots");
        _components = Path.Combine(test, "bin");
        Directory.CreateDirectory(Path.Combine(_components, "runtimes"));
        File.WriteAllText(Path.Combine(_components, "RDCore.Tests.dll"), string.Empty);
        File.WriteAllText(Path.Combine(_components, "runtimes", "native.dll"), string.Empty);
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void DeleteFolder() => Directory.Delete(Path.GetDirectoryName(_folder)!, recursive: true);

    private string Folder(string name, TimeSpan age)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_folder, name)).FullName;
        Directory.SetLastWriteTimeUtc(folder, Now - age);
        return folder;
    }

    [TestMethod]
    public void ARoot_HasTheComponentsAndAManifestThatNamesThem()
    {
        var root = TestPlatformRoot.Create(_folder, new DirectoryInfo(_components), Now);

        Assert.IsTrue(File.Exists(Path.Combine(root, "Platform", "runtimes", "native.dll")));
        var manifest = SDK.Client.RDCoreLanguageServerLaunch.ReadManifest(root);
        StringAssert.StartsWith(manifest.LangService, "Platform/");
    }

    [TestMethod]
    public void ACopyThatFails_TakesAwayTheRootItWasMaking()
    {
        var missing = new DirectoryInfo(Path.Combine(_components, "not-there"));

        Assert.ThrowsExactly<DirectoryNotFoundException>(() => TestPlatformRoot.Create(_folder, missing, Now));

        Assert.IsEmpty(Directory.GetDirectories(_folder));
    }

    [TestMethod]
    public void ARootLeftADayAgo_IsTakenAway_AndANewerOne_AndAnotherFolder_AreKept()
    {
        var abandoned = Folder($"platform-{NotRunning}-abandoned", TestPlatformRoot.Abandoned);
        var older = Folder($"platform-{NotRunning}-older", TimeSpan.FromDays(3));
        var running = Folder($"platform-{NotRunning}-running", TimeSpan.FromHours(23));
        var other = Folder("launch", TimeSpan.FromDays(3));
        File.WriteAllText(Path.Combine(older, "rdcore.json"), "{}");
        Directory.SetLastWriteTimeUtc(older, Now - TimeSpan.FromDays(3));

        TestPlatformRoot.Sweep(_folder, Now);

        Assert.IsFalse(Directory.Exists(abandoned), "a day old");
        Assert.IsFalse(Directory.Exists(older), "older, with what it holds");
        Assert.IsTrue(Directory.Exists(running), "less than a day old");
        Assert.IsTrue(Directory.Exists(other), "not a root");
    }

    // a run paused in a debugger, or another's long run on this machine: its root is a day old, and its process is still running.
    [TestMethod]
    public void ARootADayOld_OfARunThatIsStillGoing_IsKept_WithWhatItHolds()
    {
        var going = Folder($"platform-{Environment.ProcessId}-going", TimeSpan.FromDays(3));
        File.WriteAllText(Path.Combine(going, "rdcore.json"), "{}");
        Directory.SetLastWriteTimeUtc(going, Now - TimeSpan.FromDays(3));

        TestPlatformRoot.Sweep(_folder, Now);

        Assert.IsTrue(File.Exists(Path.Combine(going, "rdcore.json")));
    }

    [TestMethod]
    public void ARoot_IsMadeAfterTheSweep()
    {
        var abandoned = Folder($"platform-{NotRunning}-abandoned", TimeSpan.FromDays(2));

        var root = TestPlatformRoot.Create(_folder, new DirectoryInfo(_components), Now);

        Assert.IsFalse(Directory.Exists(abandoned));
        Assert.IsTrue(Directory.Exists(root));
    }

    [TestMethod]
    public void AFolderThatIsNotThere_IsNoRootToSweep()
        => TestPlatformRoot.Sweep(Path.Combine(_folder, "not-there"), Now);
}
