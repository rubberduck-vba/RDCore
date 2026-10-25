using System.IO.Pipelines;
using MediatR;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using RDCore.SDK.Client.Progress;
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
    }

    private sealed class Connected : IAsyncDisposable
    {
        public required ILanguageServer Server { get; init; }
        public required ILanguageClient Client { get; init; }
        public required WorkProgressTracker Tracker { get; init; }
        public required List<WorkProgressState> Seen { get; init; }

        public ValueTask DisposeAsync()
        {
            Server.Dispose();
            Client.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    // a server whose request does its work in steps, reporting each to the reporter; and a client that tracks what it is reported.
    private static async Task<Connected> ConnectAsync(IWorkProgressReporter reporter, bool clientSupportsProgress = true)
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        using var connecting = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var serverTask = OmniSharpLanguageServer.From(options => options
            .WithInput(clientToServer.Reader)
            .WithOutput(serverToClient.Writer)
            .OnRequest<WorkParams, int>("test/work", async (request, token) =>
            {
                using var work = reporter.BeginFor(request, "Working");
                work.Report(new WorkProgressStep("first", 25));
                await Task.Delay(reporter.Threshold + TimeSpan.FromMilliseconds(150), token);
                work.Report(new WorkProgressStep("second", 75));
                if (request.Throws)
                {
                    throw new InvalidOperationException("the work failed");
                }

                return 1;
            }), connecting.Token);

        var tracker = new WorkProgressTracker();
        var seen = new List<WorkProgressState>();
        tracker.Changed += state => { lock (seen) { seen.Add(state); } };

        ILanguageClient? client = null;
        var clientTask = LanguageClient.From(options =>
        {
            options
                .WithInput(serverToClient.Reader)
                .WithOutput(clientToServer.Writer)
                .WithClientCapabilities(new ClientCapabilities { Window = new WindowClientCapabilities { WorkDoneProgress = clientSupportsProgress } });
            if (clientSupportsProgress)
            {
                options
                    .OnWorkDoneProgressCreate((request, _) =>
                    {
                        tracker.Track(request.Token!, client!.WorkDoneManager.Monitor(request.Token!), WorkProgressOrigin.Server);
                        return Task.CompletedTask;
                    });
            }
        }, connecting.Token);

        var server = await serverTask;
        client = await clientTask;
        reporter.Attach(server.WorkDoneManager);
        return new Connected { Server = server, Client = client, Tracker = tracker, Seen = seen };
    }

    private static async Task<WorkProgressState[]> SettledAsync(Connected connected)
    {
        // the notifications are asynchronous: the last of them is given time to arrive.
        await Task.Delay(300);
        lock (connected.Seen)
        {
            return [.. connected.Seen];
        }
    }

    [TestMethod]
    public async Task TheWorkDoneForARequest_IsReportedToItsToken_BegunReportedAndEnded()
    {
        await using var connected = await ConnectAsync(new WorkProgressReporter(TimeSpan.FromMilliseconds(50)));

        ProgressToken token = "request-1";
        using (connected.Tracker.Track(token, connected.Client.WorkDoneManager.Monitor(token)))
        {
            await connected.Client.SendRequest(new WorkParams { WorkDoneToken = token }, CancellationToken.None);
            var seen = await SettledAsync(connected);

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
    public async Task TheWorkTheServerDoesOfItsOwn_IsReportedToATokenItCreates()
    {
        var reporter = new WorkProgressReporter(TimeSpan.Zero);
        await using var connected = await ConnectAsync(reporter);

        using (var work = reporter.Begin("Parsing the workspace"))
        {
            await Task.Delay(200);
            work.Report(new WorkProgressStep("Module1 (1 of 2)", 0));
            work.Report(new WorkProgressStep("Module2 (2 of 2)", 50));
            await Task.Delay(100);
        }

        var seen = await SettledAsync(connected);
        Assert.AreEqual("Parsing the workspace", seen[0].Title);
        Assert.AreEqual(WorkProgressOrigin.Server, seen[0].Origin);
        Assert.IsTrue(seen.Any(state => state.Message == "Module2 (2 of 2)"));
        Assert.IsTrue(seen[^1].IsDone);
    }

    [TestMethod]
    public async Task WorkDoneSoonerThanTheThreshold_IsReportedNothingAtAll()
    {
        var reporter = new WorkProgressReporter(TimeSpan.FromSeconds(30));
        await using var connected = await ConnectAsync(reporter);

        using (var work = reporter.Begin("Quick"))
        {
            work.Report(new WorkProgressStep("done already", 100));
        }

        Assert.AreEqual(0, (await SettledAsync(connected)).Length, "no token is created, and nothing flickers in and out at the client");
    }

    [TestMethod]
    public async Task WorkThatFails_IsEndedAllTheSame()
    {
        await using var connected = await ConnectAsync(new WorkProgressReporter(TimeSpan.FromMilliseconds(50)));

        ProgressToken token = "request-2";
        using var tracking = connected.Tracker.Track(token, connected.Client.WorkDoneManager.Monitor(token));
        await Assert.ThrowsAsync<Exception>(() => connected.Client.SendRequest(new WorkParams { WorkDoneToken = token, Throws = true }, CancellationToken.None));

        var seen = await SettledAsync(connected);
        Assert.IsTrue(seen.Length > 0 && seen[^1].IsDone, "progress completion is always reported");
    }

    [TestMethod]
    public async Task AClientThatDoesNotShowProgress_IsReportedNothing()
    {
        var reporter = new WorkProgressReporter(TimeSpan.Zero);
        await using var connected = await ConnectAsync(reporter, clientSupportsProgress: false);

        using (reporter.Begin("Indexing"))
        {
            await Task.Delay(100);
        }

        Assert.AreEqual(0, (await SettledAsync(connected)).Length);
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
