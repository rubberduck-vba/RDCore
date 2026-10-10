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

    private static ExternalHost NewHost() => ExternalHosts.New();

    [TestMethod]
    public void TheExternalHost_SaysWhetherThePlatformItRunsOnHasAutomationServers()
    {
        using var host = NewHost();
        var server = new RemoteAutomationServer(host);

        Assert.AreEqual(OperatingSystem.IsWindows(), server.IsAvailable);
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
    [OSCondition(OperatingSystems.Windows)]
    public void AnObjectOfAnExternalHostThatStopped_IsNoLongerAvailable_AndTheNextCallStartsAnother()
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

        Assert.IsTrue(SpinWait.SpinUntil(() => first.IsLost, TimeSpan.FromSeconds(10)), "the external host was not seen to stop");

        var failure = Assert.ThrowsExactly<AutomationException>(()
            => server.Invoke(dictionary, "Count", AutomationInvocation.Get, [], [], CultureInfo.InvariantCulture));
        Assert.AreEqual(ServerUnavailable, failure.HResult);

        var another = server.CreateObject("Scripting.Dictionary");
        Assert.AreEqual(0, server.Invoke(another, "Count", AutomationInvocation.Get, [], [], CultureInfo.InvariantCulture));
        Assert.AreEqual(2, host.Connect().Number);
    }
}
