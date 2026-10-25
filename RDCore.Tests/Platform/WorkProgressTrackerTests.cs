using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Client.Progress;

namespace RDCore.Tests.Platform;

/// <summary>
/// What a client makes of the progress it is reported: each piece of work folded into where it stands, from its begin to its end.
/// </summary>
[TestClass]
public sealed class WorkProgressTrackerTests
{
    // what a server reports to a token, pushed by the test.
    private sealed class Reported : IObservable<WorkDoneProgress>
    {
        private IObserver<WorkDoneProgress>? _observer;

        public IDisposable Subscribe(IObserver<WorkDoneProgress> observer)
        {
            _observer = observer;
            return new Unsubscribed(() => _observer = null);
        }

        public void Push(WorkDoneProgress progress) => _observer?.OnNext(progress);

        public void Complete() => _observer?.OnCompleted();

        private sealed class Unsubscribed(Action action) : IDisposable
        {
            public void Dispose() => action();
        }
    }

    [TestMethod]
    public void AReport_KeepsWhatItDoesNotSay()
    {
        var tracker = new WorkProgressTracker();
        var reported = new Reported();
        tracker.Track("t", reported);

        reported.Push(new WorkDoneProgressBegin { Title = "Analyzing Program", Message = "Parsing", Percentage = 0 });
        reported.Push(new WorkDoneProgressReport { Percentage = 50 });

        var state = tracker.Active.Single();
        Assert.AreEqual(("Analyzing Program", "Parsing", (int?)50), (state.Title, state.Message, state.Percentage));
    }

    [TestMethod]
    public void TheEnd_TakesTheWorkOutOfWhatIsActive_AndSaysItIsDone()
    {
        var tracker = new WorkProgressTracker();
        var changes = new List<WorkProgressState>();
        tracker.Changed += changes.Add;
        var reported = new Reported();
        tracker.Track("t", reported);

        reported.Push(new WorkDoneProgressBegin { Title = "Working" });
        reported.Push(new WorkDoneProgressEnd { Message = "Done" });

        Assert.AreEqual(0, tracker.Active.Count);
        Assert.IsTrue(changes[^1] is { IsDone: true, Message: "Done" });
    }

    [TestMethod]
    public void NothingIsShownOfAToken_UntilItsWorkBegins_AndAReportWithoutABeginIsNoWork()
    {
        var tracker = new WorkProgressTracker();
        var changes = new List<WorkProgressState>();
        tracker.Changed += changes.Add;
        var reported = new Reported();
        var tracking = tracker.Track("t", reported);

        reported.Push(new WorkDoneProgressReport { Message = "orphan" });
        tracking.Dispose();

        Assert.AreEqual(0, changes.Count, "work that never began is not shown ending either");
    }

    [TestMethod]
    public void WorkThatIsNotHeardFromAnyMore_IsDone()
    {
        var tracker = new WorkProgressTracker();
        var reported = new Reported();
        var tracking = tracker.Track("t", reported);
        reported.Push(new WorkDoneProgressBegin { Title = "Working" });

        tracking.Dispose();

        Assert.AreEqual(0, tracker.Active.Count, "a request that is answered ends what was done for it, said or not");
    }

    [TestMethod]
    public void ThePiecesOfWork_AreTrackedApart_InTheOrderTheyBegan()
    {
        var tracker = new WorkProgressTracker();
        var (first, second) = (new Reported(), new Reported());
        tracker.Track("a", first);
        tracker.Track("b", second, WorkProgressOrigin.Server);

        first.Push(new WorkDoneProgressBegin { Title = "First" });
        second.Push(new WorkDoneProgressBegin { Title = "Second" });
        first.Push(new WorkDoneProgressEnd());

        Assert.AreEqual("Second", tracker.Active.Single().Title);
        Assert.AreEqual(WorkProgressOrigin.Server, tracker.Active.Single().Origin);
    }
}
