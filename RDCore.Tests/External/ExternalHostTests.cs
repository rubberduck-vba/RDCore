using RDCore.External.Automation;
using RDCore.External.Client;
using System.Diagnostics;
using System.Globalization;

namespace RDCore.Tests.External;

/// <summary>
/// The external host is <c>rdc</c> in external mode, a process of its own: it is started when it is needed, says whether the platform it runs on has automation servers,
/// and when it stops, what it held is gone - a program that uses it is told the server is not available, as MS-VBA tells it of an out-of-process server that stopped -
/// and the next call starts another.
/// </summary>
[TestClass]
[TestCategory("External host")]
public sealed class ExternalHostTests
{
    private const int ServerUnavailable = unchecked((int)0x800706BA);

    private static readonly TimeSpan LostSafetyCap = TimeSpan.FromMinutes(5);

    private static ExternalHost NewHost() => ExternalHosts.New();

    [TestMethod]
    public void TheExternalHost_SaysWhetherThePlatformItRunsOnHasAutomationServers()
    {
        using var host = NewHost();
        var server = new RemoteAutomationServer(host);

        Assert.AreEqual(OperatingSystem.IsWindows(), server.IsAvailable);
    }

    [TestMethod]
    public void AnExternalHostWhoseConnectionCannotBeMade_IsOneThatCouldNotBeStarted_AndTheNextCallTriesAgain()
    {
        var canStart = false;
        using var host = ExternalHosts.New(() => canStart);

        Assert.IsFalse(host.TryConnect(out var none), "a host that could not be started is none, and nothing is thrown");
        Assert.IsNull(none);
        Assert.ThrowsExactly<ExternalHostUnavailableException>(() => host.Connect());

        canStart = true;
        Assert.IsTrue(host.TryConnect(out var started));
        Assert.AreEqual(1, started!.Number, "a start that failed before its connection was made is no incarnation");
    }

    [TestMethod]
    public void AnExternalHostThatIsGotReady_IsTheOneTheFirstCallFinds()
    {
        using var host = NewHost();
        var server = new RemoteAutomationServer(host);

        server.Prepare();
        var first = host.Connect();

        Assert.AreSame(first, host.Connect());
        Assert.AreEqual(1, first.Number);
    }

    [TestMethod]
    public void AnExternalHostGotReadyByManySessionsWhileItStarts_IsStartedOnce_OnAThreadOfItsOwn()
    {
        using var started = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        var starts = 0;
        var startedOnThePool = true;
        using var host = ExternalHosts.New(canStart: () =>
        {
            if (Interlocked.Increment(ref starts) == 1)
            {
                startedOnThePool = Thread.CurrentThread.IsThreadPoolThread;
                started.Set();
                finish.Wait();
            }

            return false;
        });
        var libraries = new RemoteNativeLibraryHost(host);
        var servers = new RemoteAutomationServer(host);

        libraries.Prepare();
        Assert.IsTrue(started.Wait(TimeSpan.FromMinutes(1)), "the external host was not started");
        for (var session = 0; session < 20; session++)
        {
            libraries.Prepare();
            servers.Prepare();
        }

        finish.Set();
        Assert.IsFalse(host.TryConnect(out _), "a call waits for the start that is being made, then makes its own");

        Assert.AreEqual(2, starts, "the sessions that got the host ready while it was being started started it again");
        Assert.IsFalse(startedOnThePool, "the host was started on a thread of the pool, which the programs of the sessions go on with");
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public async Task AnObjectOfAnExternalHostThatStopped_IsNoLongerAvailable_AndTheNextCallStartsAnother()
    {
        using var host = NewHost();
        var server = new RemoteAutomationServer(host);
        var dictionary = server.CreateObject("Scripting.Dictionary");
        _ = server.Invoke(dictionary, "Add", AutomationInvocation.Method, ["key", 1], [false, false], CultureInfo.InvariantCulture);

        var first = host.Connect();
        using (var process = Process.GetProcessById(first.Connection.ProcessId))
        {
            process.Kill();
        }

        // the loss itself is awaited: the channel closes when the process stops, however long a loaded machine takes to say so. The cap only keeps a
        // host that is never seen to stop from hanging the run.
        await first.Lost.WaitAsync(LostSafetyCap);
        Assert.IsTrue(first.IsLost);

        var failure = Assert.ThrowsExactly<AutomationException>(()
            => server.Invoke(dictionary, "Count", AutomationInvocation.Get, [], [], CultureInfo.InvariantCulture));
        Assert.AreEqual(ServerUnavailable, failure.HResult);

        var another = server.CreateObject("Scripting.Dictionary");
        Assert.AreEqual(0, server.Invoke(another, "Count", AutomationInvocation.Get, [], [], CultureInfo.InvariantCulture));
        Assert.AreEqual(2, host.Connect().Number);
    }
}
