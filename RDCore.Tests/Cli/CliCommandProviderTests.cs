using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.App.Commands;
using RDCore.SDK.Client;
using RDCore.SDK.Extensibility;

namespace RDCore.Tests.Cli;

[TestClass]
public sealed class CliCommandProviderTests
{
    private sealed class FakeCommand : ICliCommand
    {
        public string Name => "fake";
        public IReadOnlyList<string> Aliases => [];
        public string Summary => "fake";
        public Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken token) => Task.FromResult(0);
    }

    private static ExtensionInfo Extension(params PlatformExtensionServerCapability[] capabilities)
        => new("Ext.exe", "Ext", new Version(1, 0), "pub", "", "desc", "sig", capabilities);

    [TestMethod]
    public void Native_ReturnsInjectedCommands()
    {
        ICliCommand[] commands = [new FakeCommand()];
        var sut = new NativeCliCommandProvider(commands);

        CollectionAssert.AreEquivalent(commands, sut.GetCommands().ToArray());
    }

    [TestMethod]
    public void Extension_Probes_Discover_AndSurfacesNoVerbsYet()
    {
        var extensions = Substitute.For<IExtensionsProvider>();
        extensions.Discover().Returns(
        [
            Extension(new PlatformExtensionServerCapability(nameof(CliCommand))),
            Extension(new PlatformExtensionServerCapability("SomethingElse")),
        ]);
        var sut = new ExtensionCliCommandProvider(extensions, NullLogger<ExtensionCliCommandProvider>.Instance);

        Assert.AreEqual(0, sut.GetCommands().Count());
        extensions.Received(1).Discover();
    }
}
