using RDCore.LanguageServer.Debugging;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Platform.Components;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The lines a program printed arrive apart from the answer to the request that runs it; the relay tells who got the answer when the lines that came before it have
/// arrived.
/// </summary>
[TestClass]
public sealed class HostOutputRelayTests
{
    // the timeout of a wait, kept by a clock that never moves unless the test moves it: no wait ends at it unless the test says so.
    private static readonly TimeSpan Expiry = TimeSpan.FromSeconds(2);

    // not a timing: a wait that never ends fails the test here instead of hanging the run.
    private static readonly TimeSpan HangGuard = TimeSpan.FromMinutes(5);

    [TestMethod]
    public async Task WhatWasSaid_IsPassedOn_InTheOrderItArrived()
    {
        var relay = new HostOutputRelay(new ManualClock());
        var heard = new List<string>();
        relay.Printed += lines => heard.AddRange(lines);

        relay.Publish(new HostOutputNotification { Lines = ["a"], Total = 1 });
        relay.Publish(new HostOutputNotification { Lines = ["b", "c"], Total = 3 });

        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, heard);
        await relay.WaitForAsync(3, Expiry, CancellationToken.None).WaitAsync(HangGuard);
    }

    // the line that is here is counted when the wait starts, before it returns: the wait is not over on that one, and nothing else can end it but the second.
    [TestMethod]
    public async Task AWait_ForLinesThatHaveNotArrived_EndsWhenTheyDo()
    {
        var relay = new HostOutputRelay(new ManualClock());
        relay.Publish(new HostOutputNotification { Lines = ["a"], Total = 1 });

        var waiting = relay.WaitForAsync(2, Expiry, CancellationToken.None);
        Assert.IsFalse(waiting.IsCompleted, "one of the two lines is not here");

        relay.Publish(new HostOutputNotification { Lines = ["b"], Total = 2 });

        await waiting.WaitAsync(HangGuard);
    }

    [TestMethod]
    public async Task AWait_ForLinesThatDoNotCome_EndsAtTheTimeout_AndNotBefore()
    {
        var clock = new ManualClock();
        var relay = new HostOutputRelay(clock);
        var waiting = relay.WaitForAsync(5, Expiry, CancellationToken.None);

        clock.Advance(Expiry - TimeSpan.FromTicks(1));
        Assert.IsFalse(waiting.IsCompleted, "the timeout is not reached");

        clock.Advance(TimeSpan.FromTicks(1));
        await waiting.WaitAsync(HangGuard);
    }

    [TestMethod]
    public async Task AWait_ThatIsCancelled_IsCancelled()
    {
        var relay = new HostOutputRelay(new ManualClock());
        using var cancel = new CancellationTokenSource();
        var waiting = relay.WaitForAsync(5, Expiry, cancel.Token);

        await cancel.CancelAsync();

        _ = await Assert.ThrowsAsync<OperationCanceledException>(() => waiting.WaitAsync(HangGuard));
    }
}
