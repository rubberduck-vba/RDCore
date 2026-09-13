using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Server;
using RDCore.SDK.Server.Services;

namespace RDCore.Tests.Server;

/// <summary>
/// A client that sends a bare <c>Exit</c> notification without ever sending <c>Initialize</c> is not a
/// protocol violation (LSP's Server Lifecycle §Initialize Request carves out exactly this exception),
/// but <c>OmniSharp</c>'s own <see cref="LspServerReceiver"/> drops it like any other pre-Initialize
/// notification — so the handler never runs and the host hangs waiting for a shutdown that never comes.
/// <see cref="RDCoreLifecycleReceiver"/> is the fix; the first test here pins the upstream behavior it
/// works around.
/// </summary>
[TestClass]
public sealed class RDCoreLifecycleReceiverTests
{
    private static readonly JObject ExitNotification = JObject.Parse("""{"jsonrpc":"2.0","method":"exit"}""");
    private static readonly JObject DidOpenNotification = JObject.Parse("""{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{}}""");
    private static readonly JObject HoverRequest = JObject.Parse("""{"jsonrpc":"2.0","id":1,"method":"textDocument/hover","params":{}}""");
    private static readonly JObject InitializeRequest = JObject.Parse("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");

    [TestMethod]
    public void UpstreamReceiver_DropsExit_BeforeInitialize()
    {
        var sut = new LspServerReceiver(Substitute.For<ILogger<LspServerReceiver>>());

        var (results, _) = sut.GetRequests(ExitNotification);

        Assert.IsFalse(results.Any(), "this pins OmniSharp's own gap: a bare Exit before Initialize is silently dropped, not handled.");
    }

    [TestMethod]
    public void LetsExitThrough_BeforeInitialize()
    {
        var sut = new RDCoreLifecycleReceiver(NullLogger<RDCoreLifecycleReceiver>.Instance);

        var (results, _) = sut.GetRequests(ExitNotification);

        var renor = results.Single();
        Assert.IsTrue(renor.IsNotification);
        Assert.AreEqual("exit", renor.Notification!.Method);
    }

    [TestMethod]
    public void AllowsInitializeRequest_BeforeInitialize()
    {
        var sut = new RDCoreLifecycleReceiver(NullLogger<RDCoreLifecycleReceiver>.Instance);

        var (results, _) = sut.GetRequests(InitializeRequest);

        var renor = results.Single();
        Assert.IsTrue(renor.IsRequest);
        Assert.AreEqual("initialize", renor.Request!.Method);
    }

    [TestMethod]
    public void StillDropsUnrelatedNotifications_BeforeInitialize()
    {
        var sut = new RDCoreLifecycleReceiver(NullLogger<RDCoreLifecycleReceiver>.Instance);

        var (results, _) = sut.GetRequests(DidOpenNotification);

        Assert.IsFalse(results.Any());
    }

    [TestMethod]
    public void StillRejectsUnrelatedRequests_BeforeInitialize()
    {
        var sut = new RDCoreLifecycleReceiver(NullLogger<RDCoreLifecycleReceiver>.Instance);

        var (results, _) = sut.GetRequests(HoverRequest);

        var renor = results.Single();
        Assert.IsTrue(renor.IsError);
        Assert.AreEqual(-32002, renor.Error!.Error!.Code);
    }

    [TestMethod]
    public void PassesEverythingThrough_OnceInitialized()
    {
        var sut = new RDCoreLifecycleReceiver(NullLogger<RDCoreLifecycleReceiver>.Instance);
        sut.Initialized();

        var (results, _) = sut.GetRequests(DidOpenNotification);

        var renor = results.Single();
        Assert.IsTrue(renor.IsNotification);
        Assert.AreEqual("textDocument/didOpen", renor.Notification!.Method);
    }
}
