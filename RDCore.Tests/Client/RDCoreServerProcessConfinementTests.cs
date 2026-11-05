using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using RDCore.SDK.Client;
using RDCore.SDK.Platform;
using RDCore.SDK.Server.Configuration;
using System.ComponentModel;
using System.IO.Abstractions;

namespace RDCore.Tests.Client;

/// <summary>
/// A platform starts only its own executables: <see cref="RDCoreServerProcess.StartAsync"/> refuses a path, of the platform manifest or of an extension that its
/// own manifest names, that is not under the platform root, before anything is started.
/// </summary>
// mutates the RDCORE_PLATFORM_ROOT process environment variable.
[TestClass]
[DoNotParallelize]
public sealed class RDCoreServerProcessConfinementTests
{
    // the platform root, and beside it a folder that is not the platform's.
    private string _folder = null!;
    private string _root = null!;
    private string _outside = null!;

    private static readonly string Executable = OperatingSystem.IsWindows() ? "Server.exe" : "Server";

    [TestInitialize]
    public void CreateRoot()
    {
        _folder = Path.Combine(Path.GetTempPath(), "RDCore", "confinement", Guid.NewGuid().ToString("N"));
        _root = Path.Combine(_folder, "platform");
        _outside = Path.Combine(_folder, "outside");
        Directory.CreateDirectory(Path.Combine(_root, "Extensions"));
        Directory.CreateDirectory(_outside);
        File.WriteAllText(Path.Combine(_outside, Executable), string.Empty);
        Environment.SetEnvironmentVariable(PlatformEnvironment.RootEnvironmentVariable, _root);
    }

    [TestCleanup]
    public void DeleteRoot()
    {
        Environment.SetEnvironmentVariable(PlatformEnvironment.RootEnvironmentVariable, null);
        // a link is taken away as a link, before the folders, which Windows does not delete through a junction.
        foreach (var link in Directory.EnumerateDirectories(Path.Combine(_root, "Extensions")).Where(folder => new DirectoryInfo(folder).LinkTarget is not null))
        {
            Directory.Delete(link);
        }

        var linkedRoot = Path.Combine(_folder, "opt", "rdcore");
        if (new DirectoryInfo(linkedRoot).LinkTarget is not null)
        {
            Directory.Delete(linkedRoot);
        }

        Directory.Delete(_folder, recursive: true);
    }

    private static RDCoreServerProcess CreateSut() => new(
        new FileSystem(),
        new PlatformEnvironment(new FileSystem()),
        Options.Create(new SdkAppOptions()),
        Substitute.For<ILogger<RDCoreServerProcess>>());

    private static async Task<Exception> StartAsync(string relativePath)
    {
        using var sut = CreateSut();
        using var tokenSource = new CancellationTokenSource();
        try
        {
            await sut.StartAsync(relativePath, RDCoreLanguageServerLaunch.NewPipeName(), tokenSource);
        }
        catch (Exception exception)
        {
            Assert.AreEqual(0, sut.ProcessId, "nothing was started");
            return exception;
        }

        Assert.Fail($"'{relativePath}' was started.");
        return null!;
    }

    [TestMethod]
    [DataRow("../outside/{0}", DisplayName = "a manifest path through ..")]
    [DataRow("Extensions/../../outside/{0}", DisplayName = "an extension path through ..")]
    [DataRow("Extensions/../../outside/../outside/{0}", DisplayName = "an extension path that climbs out and back")]
    public async Task AnExecutableOutsideTheRoot_ThroughDotDot_IsNotStarted(string path)
    {
        var refused = await StartAsync(string.Format(path, Executable));

        Assert.IsInstanceOfType<InvalidDataException>(refused, refused.Message);
        StringAssert.Contains(refused.Message, Executable);
    }

    [TestMethod]
    public async Task AnExecutableOutsideTheRoot_ByAnAbsolutePath_IsNotStarted()
    {
        var refused = await StartAsync(Path.Combine(_outside, Executable));

        Assert.IsInstanceOfType<InvalidDataException>(refused, refused.Message);
    }

    [TestMethod]
    public async Task AnExtensionInAFolderThatLinksOutsideTheRoot_IsNotStarted()
    {
        RDCoreLanguageServerLaunchTests.WithLinkedFolder(Path.Combine(_root, "Extensions", "Linked"), _outside);

        var refused = await StartAsync($"Extensions/Linked/{Executable}");

        Assert.IsInstanceOfType<InvalidDataException>(refused, refused.Message);
    }

    // links that lead to each other lead nowhere: where the executable is can't be told, and it is refused as one outside the root is.
    [TestMethod]
    public async Task AnExtensionThroughACycleOfLinks_IsNotStarted()
    {
        var extensions = Path.Combine(_root, "Extensions");
        RDCoreLanguageServerLaunchTests.WithLinkedFolder(Path.Combine(extensions, "A"), Path.Combine(extensions, "B"));
        RDCoreLanguageServerLaunchTests.WithLinkedFolder(Path.Combine(extensions, "B"), Path.Combine(extensions, "A"));

        var refused = await StartAsync($"Extensions/A/{Executable}");

        Assert.IsInstanceOfType<InvalidDataException>(refused, refused.Message);
        Assert.IsInstanceOfType<IOException>(refused.InnerException, refused.InnerException?.Message);
    }

    // a root that is a link, as /opt/rdcore -> /srv/rdcore-1.2 is (and macOS's /var -> /private/var): the kernel takes ".." where the path is so far, in the
    // folder the link leads to, so a path that climbs out of an extension's folder as far as Path.GetFullPath stops at the root is outside it.
    [TestMethod]
    [OSCondition(OperatingSystems.Linux | OperatingSystems.OSX)]
    public async Task AnExtensionThatClimbsOutOfALinkedRoot_IsNotStarted()
    {
        var real = Directory.CreateDirectory(Path.Combine(_folder, "srv", "rdcore-1.2", "Extensions", "Title")).Parent!.Parent!.FullName;
        Directory.CreateDirectory(Path.Combine(_folder, "srv", "rdcore"));
        File.WriteAllText(Path.Combine(_folder, "srv", "rdcore", Executable), string.Empty);
        var linked = Path.Combine(Directory.CreateDirectory(Path.Combine(_folder, "opt")).FullName, "rdcore");
        Directory.CreateSymbolicLink(linked, real);
        Environment.SetEnvironmentVariable(PlatformEnvironment.RootEnvironmentVariable, linked);

        var refused = await StartAsync($"Extensions/Title/../../../rdcore/{Executable}");

        Assert.IsInstanceOfType<InvalidDataException>(refused, refused.Message);
    }

    // what is checked is what is started: the real path, with ".." taken as the system takes it - folded first on Windows, where the path is so far elsewhere.
    [TestMethod]
    public void TheExecutableStarted_IsTheRealPathOfTheOneChecked()
    {
        var inner = Directory.CreateDirectory(Path.Combine(_root, "deep", "a", "b")).FullName;
        RDCoreLanguageServerLaunchTests.WithLinkedFolder(Path.Combine(_root, "Extensions", "Inner"), inner);

        var started = PlatformRootConfinement.Confined(_root, Path.Combine(_root, "Extensions", "Inner", "..", Executable));

        var expected = OperatingSystem.IsWindows() ? Path.Combine(_root, "Extensions", Executable) : Path.Combine(_root, "deep", "a", Executable);
        Assert.AreEqual(PlatformRootConfinement.RealPath(expected), started);
    }

    // the external host is rdc, in the folder of the rdc that starts it: that is this process's own, and it is started whatever the platform root is said to be.
    [TestMethod]
    public async Task AnExecutableOfThisProcesssOwnFolder_IsStarted_UnderAnotherRoot()
    {
        var failed = await StartAsync(Path.Combine(AppContext.BaseDirectory, $"not-there-{Executable}"));

        Assert.IsInstanceOfType<Win32Exception>(failed, failed.Message);
    }

    // one under the root is started: it isn't there, and the system says so, as it does for any executable it can't find.
    [TestMethod]
    [DataRow("Extensions/Title/{0}", DisplayName = "an extension")]
    [DataRow("Extensions/../RDCore.Parsing/{0}", DisplayName = "a path through .. that stays under the root")]
    public async Task AnExecutableUnderTheRoot_IsStarted(string path)
    {
        var failed = await StartAsync(string.Format(path, Executable));

        Assert.IsInstanceOfType<Win32Exception>(failed, failed.Message);
    }
}
