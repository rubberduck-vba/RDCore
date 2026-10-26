using RDCore.Runtime.Execution;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// Whose turn it is in a session: the program that runs, or an event that something outside the workspace raised. An event is handled when the program can take a call from the
/// server - while it waits for a call of its own, when it yields with <c>DoEvents</c>, when it is over - and never in the middle of a statement.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.8.1.5 DoEvents")]
public sealed class SessionTurnTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    // an event as a server raises it: on a thread of its own, waiting until the session is open to it, and handled then.
    private static Task Raise(SessionTurn turn, List<string> said, string name)
        => Task.Run(() =>
        {
            using (turn.Waiting())
            {
                SpinWait.SpinUntil(() => turn.IsOpenForEvents);
                turn.RunEvent(() => said.Add(name));
            }
        });

    [TestMethod]
    public void Running_IsSaidWhenTheFirstProgramStarts_AndWhenTheLastEnds()
    {
        // what a host of windows holds their messages by: once when a program starts running, once when none runs any more - not for a program run by another.
        var turn = new SessionTurn();
        var said = new List<bool>();
        turn.Running += said.Add;

        turn.Enter();
        Assert.IsTrue(turn.IsRunning);
        using (turn.Yield())
        {
            turn.Enter();
            turn.Exit();
        }

        turn.Exit();

        CollectionAssert.AreEqual(new[] { true, false }, said);
        Assert.IsFalse(turn.IsRunning);
    }

    [TestMethod]
    public async Task WhenNothingRuns_AnEventIsHandledAsItArrives()
    {
        var turn = new SessionTurn();
        var said = new List<string>();

        await Raise(turn, said, "click").WaitAsync(Patience);

        CollectionAssert.AreEqual(new[] { "click" }, said);
    }

    [TestMethod]
    public async Task WhileAProgramRuns_AnEventWaits_UntilTheProgramIsOver()
    {
        var turn = new SessionTurn();
        var said = new List<string>();
        turn.Enter();

        var raised = Raise(turn, said, "click");
        await Task.Delay(100);
        Assert.IsEmpty(said, "the program is busy, and takes no call");

        turn.Exit();
        await raised.WaitAsync(Patience);

        CollectionAssert.AreEqual(new[] { "click" }, said);
    }

    [TestMethod]
    public async Task WhileAProgramWaitsForACall_AnEventIsHandled_AndTheProgramGoesOnAfter()
    {
        var turn = new SessionTurn();
        var said = new List<string>();
        turn.Enter();

        var raised = Raise(turn, said, "click");
        using (turn.Yield())
        {
            await raised.WaitAsync(Patience);
            said.Add("call returned");
        }

        turn.Exit();

        CollectionAssert.AreEqual(new[] { "click", "call returned" }, said);
    }

    [TestMethod]
    public async Task AProgramThatPumps_HasTheEventsThatWaitedHandled_ThenGoesOn()
    {
        var turn = new SessionTurn();
        var said = new List<string>();
        turn.Enter();

        var raised = Raise(turn, said, "click");

        // the event waits: the program is busy, and takes no call until it says so.
        await Task.Delay(200);
        Assert.IsEmpty(said);

        turn.Pump();
        said.Add("pumped");
        turn.Exit();
        await raised.WaitAsync(Patience);

        CollectionAssert.AreEqual(new[] { "click", "pumped" }, said);
    }

    [TestMethod]
    public void AProgramThatIsNotInItsTurn_HasNothingToYield_AndNothingToPump()
    {
        var turn = new SessionTurn();

        using (turn.Yield())
        {
            turn.Pump();
        }

        Assert.IsTrue(turn.IsOpenForEvents);
    }

    [TestMethod]
    public void AnEventHandledWhenNothingRuns_PrintsWhereTheHostSays()
    {
        var turn = new SessionTurn();
        var scopes = new List<string>();
        turn.EventScope = () =>
        {
            scopes.Add("opened");
            return new Closing(() => scopes.Add("closed"));
        };

        turn.RunEvent(() => scopes.Add("handled"));
        turn.Enter();
        using (turn.Yield())
        {
            turn.RunEvent(() => scopes.Add("handled while a program runs"));
        }

        turn.Exit();

        CollectionAssert.AreEqual(new[] { "opened", "handled", "closed", "handled while a program runs" }, scopes, "a program prints where it prints");
    }

    private sealed class Closing(Action close) : IDisposable
    {
        public void Dispose() => close();
    }
}
