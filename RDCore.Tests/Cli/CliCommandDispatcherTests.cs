using NSubstitute;
using RDCore.CLI.App.Commands;
using RDCore.SDK.ConsoleIO;

namespace RDCore.Tests.Cli;

[TestClass]
public sealed class CliCommandDispatcherTests
{
    private sealed class FakeCommand(string name, string[] aliases, Func<IReadOnlyList<string>, int> run) : ICliCommand
    {
        public string Name => name;
        public IReadOnlyList<string> Aliases => aliases;
        public string Summary => "fake";
        public Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken token) => Task.FromResult(run(args));
    }

    private static CliCommandDispatcher NewDispatcher(IConsoleMessageWriter writer, params ICliCommandProvider[] providers)
        => new(providers, writer);

    private static ICliCommandProvider Provider(params ICliCommand[] commands)
    {
        var provider = Substitute.For<ICliCommandProvider>();
        provider.GetCommands().Returns(commands);
        return provider;
    }

    [TestMethod]
    public async Task ResolvesByName_RunsCommand_ReturnsItsExitCode()
    {
        var command = new FakeCommand("describe-ext", [], _ => 7);
        var sut = NewDispatcher(Substitute.For<IConsoleMessageWriter>(), Provider(command));

        var exit = await sut.DispatchAsync("describe-ext", [], CancellationToken.None);

        Assert.AreEqual(7, exit);
    }

    [TestMethod]
    public async Task ResolvesByAlias()
    {
        var command = new FakeCommand("describe-ext", ["x"], _ => 0);
        var sut = NewDispatcher(Substitute.For<IConsoleMessageWriter>(), Provider(command));

        Assert.AreEqual(0, await sut.DispatchAsync("x", [], CancellationToken.None));
    }

    [TestMethod]
    public async Task UnknownVerb_WritesUsage_ReturnsTwo()
    {
        var writer = Substitute.For<IConsoleMessageWriter>();
        var sut = NewDispatcher(writer, Provider(new FakeCommand("describe-ext", [], _ => 0)));

        var exit = await sut.DispatchAsync("bogus", [], CancellationToken.None);

        Assert.AreEqual(2, exit);
        writer.ReceivedWithAnyArgs().WriteMessage(default!);
    }

    [TestMethod]
    public async Task CommandThrows_IsCaught_ReturnsOne()
    {
        var writer = Substitute.For<IConsoleMessageWriter>();
        var command = new FakeCommand("boom", [], _ => throw new InvalidOperationException("kaboom"));
        var sut = NewDispatcher(writer, Provider(command));

        var exit = await sut.DispatchAsync("boom", [], CancellationToken.None);

        Assert.AreEqual(1, exit);
        writer.Received().WriteException(Arg.Any<InvalidOperationException>());
    }

    [TestMethod]
    public async Task NativeProviderWinsNameCollision()
    {
        var native = new FakeCommand("diagnose", [], _ => 1);
        var extension = new FakeCommand("diagnose", [], _ => 2);
        // native provider is passed first, matching the host registration order.
        var sut = NewDispatcher(Substitute.For<IConsoleMessageWriter>(), Provider(native), Provider(extension));

        Assert.AreEqual(1, await sut.DispatchAsync("diagnose", [], CancellationToken.None));
    }
}
