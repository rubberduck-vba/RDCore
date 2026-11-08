using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Options;
using NSubstitute;
using RDCore.CLI.App.Commands;
using RDCore.SDK.Client;
using RDCore.SDK.ConsoleIO;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Server.Configuration;

namespace RDCore.Tests.Cli;

[TestClass]
public sealed class DescribeExtensionCommandTests
{
    private const string Manifest = "extension.manifest.json";

    private static ExtensionInfo SampleInfo()
        => new()
        {
            Name = "RDCore.Diagnostics.exe",
            Title = "RDCore.Diagnostics",
            Version = new Version(1, 0),
            Publisher = "pub",
            PublisherWebUrl = "",
            Description = "desc",
            FileHash = "hash",
            Capabilities = [new PlatformExtensionServerCapability(nameof(CliCommand))],
            SdkVersion = new Version(0, 1, 2, 3),
        };

    private static (DescribeExtensionCommand Sut, MockFileSystem Fs, IExtensionsProvider Provider) NewSut(bool unsafeDevMode = false)
    {
        var options = new SdkAppOptions();
        options.Server.UnsafeDevMode = unsafeDevMode;

        var fs = new MockFileSystem();
        var provider = Substitute.For<IExtensionsProvider>();
        var sut = new DescribeExtensionCommand(Substitute.For<IConsoleMessageWriter>(), fs, Options.Create(options), provider);
        return (sut, fs, provider);
    }

    [TestMethod]
    public async Task WithoutDevMode_DoesNotDescribe_ReturnsOne()
    {
        var (sut, _, provider) = NewSut(unsafeDevMode: false);

        var exit = await sut.ExecuteAsync(["RDCore.Diagnostics.exe"], CancellationToken.None);

        Assert.AreEqual(1, exit);
        provider.DidNotReceiveWithAnyArgs().Describe(default!, default!);
    }

    [TestMethod]
    public async Task DevModeSwitch_Alone_Suffices()
    {
        var (sut, fs, provider) = NewSut(unsafeDevMode: false);
        provider.Describe("RDCore.Diagnostics.exe", Arg.Any<string>()).Returns(SampleInfo());

        var exit = await sut.ExecuteAsync(["RDCore.Diagnostics.exe", "--unsafe-dev-mode"], CancellationToken.None);

        Assert.AreEqual(0, exit);
        Assert.IsTrue(fs.File.Exists(Manifest));
        StringAssert.Contains(fs.File.ReadAllText(Manifest), nameof(CliCommand));
    }

    [TestMethod]
    public async Task TheManifest_ReadsBack_WithItsId_FileHash_AndSdkVersion()
    {
        var (sut, fs, provider) = NewSut(unsafeDevMode: true);
        provider.Describe("RDCore.Diagnostics.exe", Arg.Any<string>()).Returns(SampleInfo());

        await sut.ExecuteAsync(["RDCore.Diagnostics.exe"], CancellationToken.None);

        var written = System.Text.Json.JsonSerializer.Deserialize<ExtensionInfo>(fs.File.ReadAllText(Manifest))!;
        Assert.AreEqual("RDCore.Diagnostics", written.Id, "an extension that declares no id is known by its title");
        Assert.AreEqual("hash", written.FileHash);
        Assert.AreEqual(new Version(0, 1, 2, 3), written.SdkVersion);
        Assert.IsNull(written.DiagnosticPrefix);
    }

    [TestMethod]
    public async Task NonFilenameArgument_ReturnsOne()
    {
        var (sut, _, provider) = NewSut(unsafeDevMode: true);

        var exit = await sut.ExecuteAsync(["sub/dir/RDCore.Diagnostics.exe"], CancellationToken.None);

        Assert.AreEqual(1, exit);
        provider.DidNotReceiveWithAnyArgs().Describe(default!, default!);
    }

    [TestMethod]
    public async Task ExistingManifest_WithoutOverwrite_IsLeftAlone()
    {
        var (sut, fs, provider) = NewSut(unsafeDevMode: true);
        fs.AddFile(Manifest, new MockFileData("{}"));

        var exit = await sut.ExecuteAsync(["RDCore.Diagnostics.exe"], CancellationToken.None);

        Assert.AreEqual(0, exit);
        Assert.AreEqual("{}", fs.File.ReadAllText(Manifest));
        provider.DidNotReceiveWithAnyArgs().Describe(default!, default!);
    }

    [TestMethod]
    public async Task DescribeReturnsNull_ReturnsOne()
    {
        var (sut, fs, provider) = NewSut(unsafeDevMode: true);
        provider.Describe(Arg.Any<string>(), Arg.Any<string>()).Returns((ExtensionInfo?)null);

        var exit = await sut.ExecuteAsync(["RDCore.Diagnostics.exe"], CancellationToken.None);

        Assert.AreEqual(1, exit);
        Assert.IsFalse(fs.File.Exists(Manifest));
    }
}
