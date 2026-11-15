using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RDCore.LanguageServer.Debugging;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Cli;

/// <summary>
/// The language server tells whoever takes them of the stops of the handlers of events that run while no program runs: a debug adapter attached to the session
/// first, the client when it said it takes them, and nobody otherwise.
/// </summary>
[TestClass]
public sealed class SessionStopRelayTests
{
    private static readonly HostDebugStoppedParams Stopped = new() { Stop = new ExecuteResult { Outcome = ExecutionOutcome.Suspended, ErrorLine = 3, ErrorModule = "Form1" } };

    private static ISessionStopClient Client(bool takesStops)
    {
        var client = Substitute.For<ISessionStopClient>();
        client.TakesStops.Returns(takesStops);
        return client;
    }

    [TestMethod]
    public async Task AStop_WithNoClient_IsNotTaken()
    {
        var relay = new SessionStopRelay();

        Assert.IsFalse((await relay.StoppedAsync(Stopped, CancellationToken.None)).Taken);
    }

    [TestMethod]
    public async Task AStop_ToAClientThatTakesStops_IsTold_AndTaken()
    {
        var relay = new SessionStopRelay();
        var client = Client(takesStops: true);
        relay.Attach(client);

        var answer = await relay.StoppedAsync(Stopped, CancellationToken.None);

        Assert.IsTrue(answer.Taken);
        await client.Received(1).NotifyAsync(Arg.Is<SessionDebugStoppedNotification>(told => told.Stop == Stopped.Stop), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AStop_ToAClientThatDoesNotTakeStops_IsNotTold_NorTaken()
    {
        var relay = new SessionStopRelay();
        var client = Client(takesStops: false);
        relay.Attach(client);

        Assert.IsFalse((await relay.StoppedAsync(Stopped, CancellationToken.None)).Taken);
        await client.DidNotReceiveWithAnyArgs().NotifyAsync(default!, default);
    }

    [TestMethod]
    public async Task AStop_ThatCannotBeTold_IsNotTaken()
    {
        var relay = new SessionStopRelay();
        var client = Client(takesStops: true);
        client.NotifyAsync(Arg.Any<SessionDebugStoppedNotification>(), Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("the pipe is gone"));
        relay.Attach(client);

        Assert.IsFalse((await relay.StoppedAsync(Stopped, CancellationToken.None)).Taken);
    }

    [TestMethod]
    public async Task AStop_IsTakenByADebugAdapterAttached_BeforeTheClient()
    {
        var relay = new SessionStopRelay();
        var client = Client(takesStops: true);
        relay.Attach(client);
        ExecuteResult? taken = null;

        using (relay.Take(stop =>
        {
            taken = stop;
            return true;
        }))
        {
            Assert.IsTrue((await relay.StoppedAsync(Stopped, CancellationToken.None)).Taken);
        }

        Assert.AreSame(Stopped.Stop, taken);
        await client.DidNotReceiveWithAnyArgs().NotifyAsync(default!, default);

        // the adapter that let go of them takes no more: the client does.
        Assert.IsTrue((await relay.StoppedAsync(Stopped, CancellationToken.None)).Taken);
        await client.Received(1).NotifyAsync(Arg.Any<SessionDebugStoppedNotification>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AStop_ThatADebugAdapterDeclines_IsNotTaken()
    {
        var relay = new SessionStopRelay();
        using var _ = relay.Take(_ => false);

        Assert.IsFalse((await relay.StoppedAsync(Stopped, CancellationToken.None)).Taken);
    }
}
