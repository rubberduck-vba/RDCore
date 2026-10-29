using System.IO.Pipelines;
using MediatR;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Client;
using RDCore.SDK.Server;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace RDCore.Tests.Server;

/// <summary>
/// A request an RDCore server is running is answered, whatever notification arrives meanwhile (<see cref="ServerRequestScheduling.WithEveryRequestAnswered"/>).
/// OmniSharp's default abandons the requests in flight when a notification it runs in order arrives - a <c>didOpen</c> - and answers them
/// <c>ContentModified</c>, or, when the request's handling starts just as that happens, nothing at all: HexIDE's client waited forever for the diagnostics it
/// pulled after each of a quick run of <c>didOpen</c>s (findings desk #31).
/// </summary>
[TestClass]
public sealed class EveryRequestAnsweredTests
{
    [Method("test/slow", Direction.ClientToServer)]
    public sealed record class SlowParams : IRequest<int>;

    [Method("test/inOrder", Direction.ClientToServer)]
    public sealed record class InOrderParams : IRequest;

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ARequestRunning_WhenANotificationInOrderArrives_IsAnsweredItsResult()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var toServer = new Pipe();
        var toClient = new Pipe();

        // a request the server runs in parallel, which goes on until the notification is sent; and a notification it runs in order, as didOpen is, which
        // arrives meanwhile - and which runs once the request is done, since one in order waits for what runs in parallel.
        var serving = OmniSharpLanguageServer.From(server => server
            .WithInput(toServer.Reader)
            .WithOutput(toClient.Writer)
            .WithEveryRequestAnswered()
            .OnRequest<SlowParams, int>("test/slow", async (_, token) =>
            {
                started.TrySetResult();
                await sent.Task.WaitAsync(TimeSpan.FromSeconds(20), token);
                return 42;
            }, new JsonRpcHandlerOptions { RequestProcessType = RequestProcessType.Parallel })
            .OnNotification<InOrderParams>("test/inOrder", _ => notified.TrySetResult(), new JsonRpcHandlerOptions { RequestProcessType = RequestProcessType.Serial }),
            timeout.Token);
        var connecting = LanguageClient.From(client => client.WithInput(toClient.Reader).WithOutput(toServer.Writer), timeout.Token);
        await Task.WhenAll(serving, connecting).WaitAsync(TimeSpan.FromSeconds(20));
        using var server = await serving;
        using var client = await connecting;

        var answer = client.SendRequest(new SlowParams(), timeout.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        client.SendNotification(new InOrderParams());
        sent.SetResult();

        Assert.AreEqual(42, await answer.WaitAsync(TimeSpan.FromSeconds(10)));
        await notified.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
