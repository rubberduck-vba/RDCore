using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.External;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// The external objects of a session, let go of: their provider is a server, told with the program's turn given up, as any call of it is made.
/// </summary>
[TestClass]
public sealed class SessionExternalObjectsTests
{
    // a provider that, as it lets go, has an event of the session raised on a thread of its own and waits for it, as an RDForms form waits for the thread of
    // its windows to destroy its window while that thread handles an event of the session. The event is raised only when the turn is open to it, so that a
    // provider told with the turn kept records it and returns instead of waiting forever.
    private sealed class WaitsForAnEvent(SessionTurn turn) : IExternalObjectOwner
    {
        public List<string> Said { get; } = [];

        public void Release(object handle)
        {
            if (!turn.IsOpenForEvents)
            {
                Said.Add($"{handle}: the turn is kept");
                return;
            }

            var windows = new Thread(() => turn.RunEvent(() => Said.Add($"{handle}: an event while it lets go")));
            windows.Start();
            windows.Join();
            Said.Add($"{handle}: let go");
        }
    }

    [TestMethod]
    [DataRow(false, DisplayName = "Release")]
    [DataRow(true, DisplayName = "ReleaseAll")]
    public void TheProvider_IsToldWithTheTurnGivenUp_AndAnEventTakesItMeanwhile(bool all)
    {
        var turn = new SessionTurn();
        var provider = new WaitsForAnEvent(turn);
        var objects = new SessionExternalObjects(turn);
        var identity = new VBRuntimeObjectId();
        objects.Bind(identity, provider, "form");

        turn.Enter();
        if (all)
        {
            objects.ReleaseAll();
        }
        else
        {
            objects.Release(identity);
        }

        turn.Exit();

        CollectionAssert.AreEqual(new[] { "form: an event while it lets go", "form: let go" }, provider.Said, string.Join(" | ", provider.Said));
        Assert.IsFalse(objects.TryGet(identity, out _, out _));
    }

    // let go of with no program running, as a session that is over lets go of what it held: nothing holds the turn, and nothing is given up.
    [TestMethod]
    public void TheProvider_IsTold_WhenNoProgramRuns()
    {
        var turn = new SessionTurn();
        var provider = new WaitsForAnEvent(turn);
        var objects = new SessionExternalObjects(turn);
        objects.Bind(new VBRuntimeObjectId(), provider, "form");

        objects.ReleaseAll();

        CollectionAssert.AreEqual(new[] { "form: an event while it lets go", "form: let go" }, provider.Said, string.Join(" | ", provider.Said));
        Assert.IsFalse(turn.IsRunning);
    }
}
