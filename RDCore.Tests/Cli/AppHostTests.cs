using RDCore.CLI.App.Build;

namespace RDCore.Tests.Cli;

/// <summary>
/// The .NET application host <c>rdc build</c> makes a program's executable of: found in a folder laid out as the packs of a .NET installation, which is
/// how the platform ships its own beside rdc, and named after the pack it is out of.
/// </summary>
[TestClass]
public sealed class AppHostTests
{
    private static readonly int Major = Environment.Version.Major;

    private static string Pack(string root, string runtime, string version) => Path.Combine(root, "packs", $"Microsoft.NETCore.App.Host.{runtime}", version, "runtimes", runtime, "native");

    private static string WriteHost(string root, string runtime, string version)
    {
        var folder = Pack(root, runtime, version);
        Directory.CreateDirectory(folder);
        var host = Path.Combine(folder, runtime.StartsWith("win", StringComparison.Ordinal) ? "apphost.exe" : "apphost");
        File.WriteAllBytes(host, "MZ"u8.ToArray());
        return host;
    }

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "rdcore-apphost-" + Guid.NewGuid().ToString("N"));

    [TestMethod]
    [DataRow("win-x64")]
    [DataRow("linux-x64")]
    [DataRow("osx-arm64")]
    public void InARootLaidOutAsThePacksOfADotnetInstallation_TheHostOfTheRuntime_IsTheLatestOfTheMajorVersionRdcRunsOn(string runtime)
    {
        var root = NewRoot();
        try
        {
            WriteHost(root, runtime, $"{Major}.0.1");
            var latest = WriteHost(root, runtime, $"{Major}.0.3");
            WriteHost(root, runtime, $"{Major + 1}.0.0");
            WriteHost(root, runtime, $"{Major - 1}.0.9");
            WriteHost(root, "win-arm64", $"{Major}.0.4");

            Assert.AreEqual(latest, AppHost.Find(runtime, root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void InARootWithNoPackForTheRuntime_ThereIsNoHost()
    {
        var root = NewRoot();
        try
        {
            WriteHost(root, "linux-x64", $"{Major}.0.0");

            Assert.IsNull(AppHost.Find("win-x64", root));
            Assert.IsNull(AppHost.Find("win-x64", Path.Combine(root, "missing")));
            Assert.IsNull(AppHost.Find("win-x64", null));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void AVersionFolderWithNoHostInIt_IsPassedOver_ForTheNextLatest()
    {
        var root = NewRoot();
        try
        {
            var host = WriteHost(root, "win-x64", $"{Major}.0.1");
            Directory.CreateDirectory(Pack(root, "win-x64", $"{Major}.0.2"));

            Assert.AreEqual(host, AppHost.Find("win-x64", root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void TheHostThePlatformShipsBesideRdc_IsNamedAfterThePackItIsOut()
    {
        var shipped = Path.Combine(Path.GetTempPath(), "platform", "RDCore.CLI", AppHost.ShippedFolder);

        var (pack, version) = AppHost.PackOf(Path.Combine(Pack(shipped, "linux-x64", "11.0.2"), "apphost"), "linux-x64");

        Assert.AreEqual("Microsoft.NETCore.App.Host.linux-x64", pack);
        Assert.AreEqual("11.0.2", version);
    }
}
