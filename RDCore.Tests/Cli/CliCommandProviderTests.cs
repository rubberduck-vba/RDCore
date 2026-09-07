using NSubstitute;
using RDCore.CLI.App.Commands;
using RDCore.CLI.App.Messages;
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
    public void Extension_WithCliCommandCapability_IsReported()
    {
        var writer = Substitute.For<IConsoleMessageWriter>();
        var extensions = Substitute.For<IExtensionsProvider>();
        extensions.Discover().Returns([Extension(new PlatformExtensionServerCapability(nameof(CliCommand)))]);
        var sut = new ExtensionCliCommandProvider(extensions, writer);

        // no verbs surfaced yet (seam), but the advertising extension is reported.
        Assert.AreEqual(0, sut.GetCommands().Count());
        writer.ReceivedWithAnyArgs().WriteMessage(default!);
    }

    [TestMethod]
    public void Extension_WithoutCliCommandCapability_IsIgnored()
    {
        var writer = Substitute.For<IConsoleMessageWriter>();
        var extensions = Substitute.For<IExtensionsProvider>();
        extensions.Discover().Returns([Extension(new PlatformExtensionServerCapability("SomethingElse"))]);
        var sut = new ExtensionCliCommandProvider(extensions, writer);

        Assert.AreEqual(0, sut.GetCommands().Count());
        writer.DidNotReceiveWithAnyArgs().WriteMessage(default!);
    }
}
