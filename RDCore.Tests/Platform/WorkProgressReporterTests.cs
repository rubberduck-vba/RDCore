using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.WorkDone;
using RDCore.SDK.Server.Progress;

namespace RDCore.Tests.Platform;

/// <summary>
/// The steps of work whose progress is reported, against a work done manager the test answers for: what the client is sent, in the order the work reported it.
/// </summary>
[TestClass]
public sealed class WorkProgressReporterTests
{
    private static readonly TimeSpan Threshold = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    // a step the work reports while the client is being told it began: the manager reports it before it gives the observer of the begin, every time.
    [TestMethod]
    [DataRow(false, DisplayName = "the work goes on")]
    [DataRow(true, DisplayName = "the work is then completed")]
    public async Task AStepReportedWhileTheClientIsToldTheWorkBegan_IsSent(bool completes)
    {
        var clock = new ManualClock();
        var reporter = new WorkProgressReporter(Threshold, clock);
        var observer = Substitute.For<IWorkDoneObserver>();
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var begun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        observer.When(o => o.OnNext("second", Arg.Any<int?>(), Arg.Any<bool?>())).Do(_ => sent.TrySetResult());
        observer.When(o => o.Dispose()).Do(_ => ended.TrySetResult());

        IWorkProgress? work = null;
        var manager = Substitute.For<IServerWorkDoneManager>();
        manager.IsSupported.Returns(true);
        manager.Create(default!, default!, default!, default).ReturnsForAnyArgs(_ =>
        {
            work!.Report(new WorkProgressStep("second", 50));
            begun.TrySetResult();
            return Task.FromResult(observer);
        });
        reporter.Attach(manager);

        work = reporter.Begin("Indexing");
        work.Report(new WorkProgressStep("first", 10));
        clock.Advance(Threshold);
        await begun.Task.WaitAsync(Patience);

        if (completes)
        {
            work.Complete();
            await ended.Task.WaitAsync(Patience);
            Received.InOrder(() =>
            {
                observer.OnNext("second", 50, false);
                observer.Dispose();
            });
        }
        else
        {
            await sent.Task.WaitAsync(Patience);
            observer.Received(1).OnNext("second", 50, false);
            work.Complete();
        }

        var begin = (WorkDoneProgressBegin)manager.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(IServerWorkDoneManager.Create)).GetArguments()[0]!;
        Assert.AreEqual("first", begin.Message, "the begin says the step reported before it");
    }
}
