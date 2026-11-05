using System.Buffers;
using System.IO.Pipelines;
using MediatR;
using NSubstitute;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.WorkDone;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using RDCore.SDK.Client.Progress;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Progress;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace RDCore.Tests.Platform;

/// <summary>
/// The progress of a server's work, reported to its client over an actual LSP connection (<strong>LSP 3.17</strong> §Work Done Progress): to the token a
/// request gave, and to one the server creates for work of its own; nothing of work done sooner than it is worth reporting; and an end to work that is begun,
/// whatever comes of it.
/// </summary>
[TestClass]
public sealed class WorkProgressIntegrationTests
{
    [Method("test/work", Direction.ClientToServer)]
    public sealed record class WorkParams : IRequest<int>, IWorkDoneProgressParams
    {
        public ProgressToken? WorkDoneToken { get; init; }

        public bool Throws { get; init; }

        // the work completes, and is not answered until the client has sent its cancel of the token, which it sends as it sees the end.
        public bool AnswersAfterTheClientCancels { get; init; }
    }

    // a request the server answers at once: once it is answered, whatever the server wrote before it is on the wire.
    [Method("test/ping", Direction.ClientToServer)]
    public sealed record class PingParams : IRequest<int>;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private sealed class Connected : IAsyncDisposable
    {
        public required ILanguageServer Server { get; init; }
        public required ILanguageClient Client { get; init; }
        public required WorkProgressTracker Tracker { get; init; }
        public required List<WorkProgressState> Seen { get; init; }

        // the request's work reported its first step; the client saw work begin, and saw it end.
        public required TaskCompletionSource Working { get; init; }
        public required TaskCompletionSource Begun { get; init; }
        public required TaskCompletionSource Ended { get; init; }

        // the client sent the server its cancel of a token (window/workDoneProgress/cancel).
        public required TaskCompletionSource Cancelled { get; init; }

        // everything the server sent the client, as it was sent
        public required System.Text.StringBuilder Wire { get; init; }

        public ValueTask DisposeAsync()
        {
            Server.Dispose();
            Client.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    // the server's input: it notices when the server, having read past a point, asks for more - what it read up to that point was handed on.
    private sealed class NoticingReader(PipeReader inner) : PipeReader
    {
        private readonly object _gate = new();
        private long _consumed;
        private long? _at;
        private ReadOnlySequence<byte> _last;

        public TaskCompletionSource Noticed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void NoticeAt(long position)
        {
            lock (_gate)
            {
                _at ??= position;
            }
        }

        private void Asking()
        {
            lock (_gate)
            {
                if (_at is { } at && _consumed >= at)
                {
                    Noticed.TrySetResult();
                }
            }
        }

        public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            Asking();
            var result = await inner.ReadAsync(cancellationToken);
            _last = result.Buffer;
            return result;
        }

        public override bool TryRead(out ReadResult result)
        {
            Asking();
            var read = inner.TryRead(out result);
            _last = result.Buffer;
            return read;
        }

        public override void AdvanceTo(SequencePosition consumed) => AdvanceTo(consumed, consumed);

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
            lock (_gate)
            {
                _consumed += _last.Slice(0, consumed).Length;
            }

            inner.AdvanceTo(consumed, examined);
        }

        public override void CancelPendingRead() => inner.CancelPendingRead();

        public override void Complete(Exception? exception = null) => inner.Complete(exception);
    }

    // a server whose request does its work in steps, reporting each to the reporter; and a client that tracks what it is reported. The server is configured
    // as an RDCore server is (ServerRequestScheduling.WithEveryRequestAnswered).
    private static async Task<Connected> ConnectAsync(IWorkProgressReporter reporter)
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        using var connecting = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        // what the client writes is copied as it goes to the server; the server has taken the client's cancel of a token in once it has read past it and
        // asks for more, which it does once what it read is handed on.
        var toServer = new Pipe();
        var input = new NoticingReader(toServer.Reader);
        var upstream = new System.Text.StringBuilder();
        long written = 0;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                var read = await clientToServer.Reader.ReadAsync();
                foreach (var segment in read.Buffer)
                {
                    upstream.Append(System.Text.Encoding.UTF8.GetString(segment.Span));
                    if (upstream.ToString().Contains("\"window/workDoneProgress/cancel\"", StringComparison.Ordinal))
                    {
                        input.NoticeAt(written + segment.Length);
                    }

                    await toServer.Writer.WriteAsync(segment);
                    written += segment.Length;
                }

                clientToServer.Reader.AdvanceTo(read.Buffer.End);
                if (read.IsCompleted)
                {
                    await toServer.Writer.CompleteAsync();
                    return;
                }
            }
        });

        // what the server writes is copied as it goes to the client: the client's own view of progress keeps only what it makes sense of
        var toClient = new Pipe();
        var wire = new System.Text.StringBuilder();
        _ = Task.Run(async () =>
        {
            while (true)
            {
                var read = await serverToClient.Reader.ReadAsync();
                foreach (var segment in read.Buffer)
                {
                    lock (wire)
                    {
                        wire.Append(System.Text.Encoding.UTF8.GetString(segment.Span));
                    }

                    await toClient.Writer.WriteAsync(segment);
                }

                serverToClient.Reader.AdvanceTo(read.Buffer.End);
                if (read.IsCompleted)
                {
                    await toClient.Writer.CompleteAsync();
                    return;
                }
            }
        });

        var tracker = new WorkProgressTracker();
        var seen = new List<WorkProgressState>();
        var working = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var begun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tracker.Changed += state =>
        {
            lock (seen)
            {
                seen.Add(state);
            }

            (state.IsDone ? ended : begun).TrySetResult();
        };

        // the work goes on until the client has seen it begin - it was worth reporting, however long that took - and then reports its next step.
        var cancelled = input.Noticed;
        var serverTask = OmniSharpLanguageServer.From(options => options
            .WithInput(input)
            .WithOutput(serverToClient.Writer)
            .WithEveryRequestAnswered()
            .OnRequest<WorkParams, int>("test/work", async (request, token) =>
            {
                using var work = reporter.BeginFor(request, "Working");
                work.Report(new WorkProgressStep("first", 25));
                working.TrySetResult();
                await begun.Task.WaitAsync(Patience, token);
                work.Report(new WorkProgressStep("second", 75));
                if (request.Throws)
                {
                    throw new InvalidOperationException("the work failed");
                }

                if (request.AnswersAfterTheClientCancels)
                {
                    work.Complete();
                    await cancelled.Task.WaitAsync(Patience, token);
                }

                return 1;
            })
            .OnRequest<PingParams, int>("test/ping", (_, _) => Task.FromResult(0)), connecting.Token);

        ILanguageClient? client = null;
        var clientTask = LanguageClient.From(options =>
        {
            options
                .WithInput(toClient.Reader)
                .WithOutput(clientToServer.Writer)
                .WithClientCapabilities(new ClientCapabilities { Window = new WindowClientCapabilities { WorkDoneProgress = true } })
                .OnWorkDoneProgressCreate((request, _) =>
                {
                    tracker.Track(request.Token!, client!.WorkDoneManager.Monitor(request.Token!), WorkProgressOrigin.Server);
                    return Task.CompletedTask;
                });
        }, connecting.Token);

        var server = await serverTask;
        client = await clientTask;
        reporter.Attach(server.WorkDoneManager);
        return new Connected
        {
            Server = server, Client = client, Tracker = tracker, Seen = seen, Working = working, Begun = begun, Ended = ended, Cancelled = cancelled, Wire = wire,
        };
    }

    // the threshold every request's work is reported after, on a clock that moves only when a test moves it.
    private static readonly TimeSpan Threshold = TimeSpan.FromMilliseconds(50);

    // a request whose work goes on past the threshold: it is sent, its work reports its first step, and the clock is moved to the threshold, which begins it.
    private static async Task<Task<int>> SentPastTheThresholdAsync(Connected connected, ManualClock clock, WorkParams request)
    {
        var answer = connected.Client.SendRequest(request, CancellationToken.None);
        await connected.Working.Task.WaitAsync(Patience);
        clock.Advance(Threshold);
        return answer;
    }

    // what the server wrote before it answers a request it answers at once is on the wire: it writes in order.
    private static async Task FlushedAsync(Connected connected)
        => await connected.Client.SendRequest(new PingParams(), CancellationToken.None).WaitAsync(Patience);

    // what the server wrote of progress: whether it asked the client to create a token, or sent a notification to one - a message of either method, and not the
    // words anywhere, as the capabilities it answered initialize with have them.
    private static bool WroteProgress(Connected connected)
    {
        lock (connected.Wire)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(connected.Wire.ToString(),
                "\"method\"\\s*:\\s*\"(?:window/workDoneProgress/create|\\$/progress)\"");
        }
    }

    // work that is to end: the client is waited for to see it end, and then whatever the server wrote after the end - a second one, which is what some tests
    // count - is on the wire too.
    private static async Task<WorkProgressState[]> EndedAsync(Connected connected)
    {
        await connected.Ended.Task.WaitAsync(Patience);
        await FlushedAsync(connected);
        lock (connected.Seen)
        {
            return [.. connected.Seen];
        }
    }

    [TestMethod]
    public async Task TheWorkDoneForARequest_IsReportedToItsToken_BegunReportedAndEnded()
    {
        var clock = new ManualClock();
        await using var connected = await ConnectAsync(new WorkProgressReporter(Threshold, clock));

        ProgressToken token = "request-1";
        using (connected.Tracker.Track(token, connected.Client.WorkDoneManager.Monitor(token)))
        {
            var answer = connected.Client.SendRequest(new WorkParams { WorkDoneToken = token }, CancellationToken.None);
            await connected.Working.Task.WaitAsync(Patience);
            Assert.AreEqual(1, clock.Pending, "the work waits for its threshold before anything is reported of it");

            clock.Advance(Threshold);
            await answer.WaitAsync(Patience);
            var seen = await EndedAsync(connected);

            Assert.AreEqual("Working", seen[0].Title);
            Assert.AreEqual("first", seen[0].Message, "the work begins with the last step it reported before it was worth reporting");
            Assert.AreEqual(25, seen[0].Percentage);
            Assert.IsTrue(seen.Any(state => state is { Message: "second", Percentage: 75 }));
            Assert.IsTrue(seen[^1].IsDone, "the work that is begun is ended");
            Assert.AreEqual(WorkProgressOrigin.Request, seen[0].Origin);
        }

        Assert.AreEqual(0, connected.Tracker.Active.Count);
    }

    [TestMethod]
    [DataRow(false, DisplayName = "work that is done")]
    [DataRow(true, DisplayName = "work that fails")]
    public async Task WorkThatIsBegun_IsEndedOnce(bool throws)
    {
        var clock = new ManualClock();
        await using var connected = await ConnectAsync(new WorkProgressReporter(Threshold, clock));

        ProgressToken token = "request-once";
        using (connected.Tracker.Track(token, connected.Client.WorkDoneManager.Monitor(token)))
        {
            try
            {
                await (await SentPastTheThresholdAsync(connected, clock, new WorkParams { WorkDoneToken = token, Throws = throws })).WaitAsync(Patience);
            }
            catch (Exception) when (throws)
            {
                // the request failed, as it was made to: what was reported of its work is what is counted
            }

            await EndedAsync(connected);
        }

        Assert.AreEqual(1, EndsOn(connected), "one end for the work, as it is sent");
    }

    [TestMethod]
    public async Task WorkTheServerDoesOfItsOwn_IsEndedOnce()
    {
        var reporter = new WorkProgressReporter(TimeSpan.Zero);
        await using var connected = await ConnectAsync(reporter);

        using (var work = reporter.Begin("Parsing the workspace"))
        {
            work.Report(new WorkProgressStep("Module1", 50));
            await connected.Begun.Task.WaitAsync(Patience);
        }

        await EndedAsync(connected);
        Assert.AreEqual(1, EndsOn(connected), "one end for the work, as it is sent");
    }

    // the end notifications the server wrote, whatever the client made of them: its own view of a token completes on the first, and drops the others
    private static int EndsOn(Connected connected)
    {
        lock (connected.Wire)
        {
            return System.Text.RegularExpressions.Regex.Matches(connected.Wire.ToString(), "\"kind\"\\s*:\\s*\"end\"").Count;
        }
    }

    [TestMethod]
    public async Task TheWorkTheServerDoesOfItsOwn_IsReportedToATokenItCreates()
    {
        var reporter = new WorkProgressReporter(TimeSpan.Zero);
        await using var connected = await ConnectAsync(reporter);

        // the token is created, and the work begun on it, before the steps are reported to it.
        using (var work = reporter.Begin("Parsing the workspace"))
        {
            await connected.Begun.Task.WaitAsync(Patience);
            work.Report(new WorkProgressStep("Module1 (1 of 2)", 0));
            work.Report(new WorkProgressStep("Module2 (2 of 2)", 50));
        }

        var seen = await EndedAsync(connected);
        Assert.AreEqual("Parsing the workspace", seen[0].Title);
        Assert.AreEqual(WorkProgressOrigin.Server, seen[0].Origin);
        Assert.IsTrue(seen.Any(state => state.Message == "Module2 (2 of 2)"));
        Assert.IsTrue(seen[^1].IsDone);
    }

    [TestMethod]
    public async Task WorkDoneSoonerThanTheThreshold_IsReportedNothingAtAll()
    {
        // a clock that is never moved: the work is done before its threshold, whatever the machine.
        var reporter = new WorkProgressReporter(Threshold, new ManualClock());
        await using var connected = await ConnectAsync(reporter);

        using (var work = reporter.Begin("Quick"))
        {
            work.Report(new WorkProgressStep("done already", 100));
        }

        await FlushedAsync(connected);
        Assert.IsFalse(WroteProgress(connected), "no token is created, and nothing flickers in and out at the client");
    }

    [TestMethod]
    public async Task WorkThatFails_IsEndedAllTheSame()
    {
        var clock = new ManualClock();
        await using var connected = await ConnectAsync(new WorkProgressReporter(Threshold, clock));

        ProgressToken token = "request-2";
        using var tracking = connected.Tracker.Track(token, connected.Client.WorkDoneManager.Monitor(token));
        var answer = await SentPastTheThresholdAsync(connected, clock, new WorkParams { WorkDoneToken = token, Throws = true });
        await Assert.ThrowsAsync<Exception>(() => answer.WaitAsync(Patience));

        var seen = await EndedAsync(connected);
        Assert.IsTrue(seen.Length > 0 && seen[^1].IsDone, "progress completion is always reported");
    }

    // OmniSharp's client cancels a token as it sees the token's end, and the end of a request's work is sent before the request is answered: the cancel can
    // reach the server while the request runs. A server that abandoned a running request for a notification answered it ContentModified (#590's Linux CI).
    [TestMethod]
    public async Task ARequestWhoseWorkTheClientCancelsAsItEnds_IsAnswered()
    {
        var clock = new ManualClock();
        await using var connected = await ConnectAsync(new WorkProgressReporter(Threshold, clock));

        ProgressToken token = "request-3";
        using var tracking = connected.Tracker.Track(token, connected.Client.WorkDoneManager.Monitor(token));
        var answer = await SentPastTheThresholdAsync(connected, clock, new WorkParams { WorkDoneToken = token, AnswersAfterTheClientCancels = true });

        Assert.AreEqual(1, await answer.WaitAsync(Patience));
    }

    // OmniSharp's client says it shows progress whatever it is configured to say, as it can: a client that does not is a server whose manager says so.
    [TestMethod]
    public void AClientThatDoesNotShowProgress_IsReportedNothing()
    {
        var manager = Substitute.For<IServerWorkDoneManager>();
        manager.IsSupported.Returns(false);
        var reporter = new WorkProgressReporter(TimeSpan.Zero);
        reporter.Attach(manager);

        using (var work = reporter.Begin("Indexing"))
        {
            work.Report(new WorkProgressStep("Module1", 50));
        }

        Assert.AreEqual(0, manager.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IServerWorkDoneManager.Create)), "no token is created");
    }

    [TestMethod]
    public void WorkForARequestThatGaveNoToken_IsReportedNothing_AndIsDoneAllTheSame()
    {
        var work = new WorkProgressReporter(TimeSpan.Zero).BeginFor(new WorkParams(), "Working");
        work.Report(new WorkProgressStep("a step", 10));
        work.Complete();
        work.Dispose();
    }
}
