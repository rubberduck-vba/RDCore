using RDCore.SDK.Platform.Channels;
using System.IO.Pipes;

namespace RDCore.Tests.Platform;

/// <summary>
/// A channel of calls between two processes of the platform: each end calls methods of the other by name, waits for the answer, and is told when there is none.
/// </summary>
[TestClass]
[TestCategory("Platform channels")]
public sealed class CallChannelTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    // two ends of a pipe, each with a channel over it: the methods are registered before the channels start.
    private sealed class Ends : IDisposable
    {
        private readonly NamedPipeServerStream _server;
        private readonly NamedPipeClientStream _client;

        private Ends(NamedPipeServerStream server, NamedPipeClientStream client)
        {
            _server = server;
            _client = client;
            Serving = new CallChannel(server);
            Calling = new CallChannel(client);
        }

        public CallChannel Serving { get; }

        public CallChannel Calling { get; }

        public static async Task<Ends> ConnectAsync()
        {
            var name = $"rdcore-channel-test-{Guid.NewGuid():N}";
            var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var accepted = server.WaitForConnectionAsync();
            await client.ConnectAsync(10_000);
            await accepted;
            return new Ends(server, client);
        }

        public void Start()
        {
            Serving.Start();
            Calling.Start();
        }

        public void Dispose()
        {
            Calling.Dispose();
            Serving.Dispose();
            _client.Dispose();
            _server.Dispose();
        }
    }

    private sealed record class Greeting(string Name, int Times);

    // a caller that waits blocks its thread, as a program does: it gets a thread of its own, rather than one the pool shares with the tests that run alongside.
    private static Task<T> OnAThreadOfItsOwn<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        new Thread(() =>
        {
            try
            {
                done.SetResult(work());
            }
            catch (Exception exception)
            {
                done.SetException(exception);
            }
        }) { IsBackground = true }.Start();
        return done.Task;
    }

    [TestMethod]
    public async Task ACall_IsAnsweredWithWhatItsHandlerReturns()
    {
        using var ends = await Ends.ConnectAsync();
        ends.Serving.Handle<Greeting, string>("greet", greeting => string.Concat(Enumerable.Repeat($"quack {greeting.Name}! ", greeting.Times)).Trim());
        ends.Start();

        Assert.AreEqual("quack Rick! quack Rick!", ends.Calling.Call<Greeting, string>("greet", new Greeting("Rick", 2)));
        Assert.AreEqual("quack Duck!", await ends.Calling.CallAsync<Greeting, string>("greet", new Greeting("Duck", 1)).WaitAsync(Patience));
    }

    [TestMethod]
    public async Task WhatAHandlerThrows_IsTheCallersFailure()
    {
        using var ends = await Ends.ConnectAsync();
        ends.Serving.Handle<int, int>("divide", divisor => 42 / divisor);
        ends.Start();

        var failure = Assert.ThrowsExactly<CallChannelException>(() => ends.Calling.Call<int, int>("divide", 0));

        Assert.AreEqual(new DivideByZeroException().Message, failure.Message);
        Assert.AreEqual(21, ends.Calling.Call<int, int>("divide", 2), "a failure is the one call's, and the channel goes on");
    }

    [TestMethod]
    public async Task AMethodTheOtherEndHasNot_IsAFailureThatSaysWhich()
    {
        using var ends = await Ends.ConnectAsync();
        ends.Start();

        var failure = Assert.ThrowsExactly<CallChannelException>(() => ends.Calling.Call<int, int>("nothing", 0));

        StringAssert.Contains(failure.Message, "nothing");
    }

    // what an event of a server is: a call is being answered, and its handler calls the end that made it - which answers while it waits for its own answer.
    [TestMethod]
    public async Task AHandlerThatCallsTheOtherEnd_IsAnsweredWhileTheCallItAnswersWaits()
    {
        using var ends = await Ends.ConnectAsync();
        ends.Serving.Handle<int, int>("outer", value => ends.Serving.Call<int, int>("inner", value) + 1);
        ends.Calling.Handle<int, int>("inner", value => value * 10);
        ends.Start();

        var answer = await OnAThreadOfItsOwn(() => ends.Calling.Call<int, int>("outer", 4)).WaitAsync(Patience);

        Assert.AreEqual(41, answer);
    }

    [TestMethod]
    public async Task CallsMadeAtOnce_EachGetTheirOwnAnswer()
    {
        using var ends = await Ends.ConnectAsync();
        ends.Serving.Handle<int, int>("square", value => value * value);
        ends.Start();

        var answers = await Task.WhenAll(Enumerable.Range(1, 20).Select(value => OnAThreadOfItsOwn(() => ends.Calling.Call<int, int>("square", value)))).WaitAsync(Patience);

        CollectionAssert.AreEqual(Enumerable.Range(1, 20).Select(value => value * value).ToArray(), answers);
    }

    [TestMethod]
    public async Task WhenTheOtherEndGoesAway_ACallThatWaitsFails_AndTheChannelIsClosed()
    {
        using var ends = await Ends.ConnectAsync();
        using var answering = new ManualResetEventSlim();
        ends.Serving.Handle<int, int>("stall", value =>
        {
            answering.Set();
            Thread.Sleep(Timeout.Infinite);
            return value;
        });
        ends.Start();

        var stalled = ends.Calling.CallAsync<int, int>("stall", 1);
        Assert.IsTrue(answering.Wait(Patience));
        ends.Serving.Dispose();

        await Assert.ThrowsExactlyAsync<CallChannelException>(() => stalled.WaitAsync(Patience));
        await ends.Calling.Closed.WaitAsync(Patience);
        Assert.ThrowsExactly<CallChannelException>(() => ends.Calling.Call<int, int>("stall", 2));
    }
}
