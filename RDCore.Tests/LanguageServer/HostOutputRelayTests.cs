using RDCore.LanguageServer.Debugging;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The lines a program printed arrive apart from the answer to the request that runs it; the relay tells who got the answer when the lines that came before it have
/// arrived.
/// </summary>
[TestClass]
public sealed class HostOutputRelayTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    [TestMethod]
    public async Task WhatWasSaid_IsPassedOn_InTheOrderItArrived()
    {
        var relay = new HostOutputRelay();
        var heard = new List<string>();
        relay.Printed += lines => heard.AddRange(lines);

        relay.Publish(new HostOutputNotification { Lines = ["a"], Total = 1 });
        relay.Publish(new HostOutputNotification { Lines = ["b", "c"], Total = 3 });

        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, heard);
        await relay.WaitForAsync(3, Patience, CancellationToken.None);
    }

    [TestMethod]
    public async Task AWait_ForLinesThatHaveNotArrived_EndsWhenTheyDo()
    {
        var relay = new HostOutputRelay();
        var waiting = relay.WaitForAsync(2, Patience, CancellationToken.None);
        relay.Publish(new HostOutputNotification { Lines = ["a"], Total = 1 });
        await Task.Delay(50);
        Assert.IsFalse(waiting.IsCompleted, "one of the two lines is not here");

        relay.Publish(new HostOutputNotification { Lines = ["b"], Total = 2 });

        await waiting.WaitAsync(Patience);
    }

    [TestMethod]
    public async Task AWait_ForLinesThatDoNotCome_EndsAtTheTimeout()
    {
        var relay = new HostOutputRelay();

        await relay.WaitForAsync(5, TimeSpan.FromMilliseconds(100), CancellationToken.None).WaitAsync(Patience);
    }

    [TestMethod]
    public async Task AWait_ThatIsCancelled_IsCancelled()
    {
        var relay = new HostOutputRelay();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        _ = await Assert.ThrowsAsync<OperationCanceledException>(() => relay.WaitForAsync(5, Patience, cancel.Token));
    }
}
