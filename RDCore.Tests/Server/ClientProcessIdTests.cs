using RDCore.SDK.Server;

namespace RDCore.Tests.Server;

/// <summary>
/// Which process a server watches so it can exit when its client is gone
/// (<c>RDCoreServerApp.ClientProcessId</c>).
/// </summary>
/// <remarks>
/// Only the <c>-p</c> argument was ever read. LSP's <c>initialize</c> carries the client's own process
/// id, and a client that sent one was told in the log that it had sent none — and nothing watched it,
/// so the platform outlived the client that owned it. Reported against a real client in the 2026-09-27
/// assessment, both as the misleading log line and as four orphaned processes after a client crash.
/// </remarks>
[TestClass]
public sealed class ClientProcessIdTests
{
    [TestMethod]
    public void TheRequestsProcessId_Wins()
    {
        // it is the client's own statement of which process owns this server; -p could only ever have
        // been passed by a client that spawned the server itself.
        var resolved = RDCoreServerApp.ClientProcessId(requested: 4321, configured: 1234, out var outOfRange);

        Assert.AreEqual(4321, resolved);
        Assert.IsFalse(outOfRange);
    }

    [TestMethod]
    [DataRow(null, DisplayName = "no processId at all")]
    [DataRow(0L, DisplayName = "processId 0")]
    public void WithNoProcessIdInTheRequest_TheArgumentIsUsed(long? requested)
    {
        // LSP: null is "the parent is not a process I can name".
        var resolved = RDCoreServerApp.ClientProcessId(requested, configured: 1234, out var outOfRange);

        Assert.AreEqual(1234, resolved);
        Assert.IsFalse(outOfRange);
    }

    [TestMethod]
    [DataRow(long.MaxValue, DisplayName = "above Int32.MaxValue")]
    [DataRow(-1L, DisplayName = "negative")]
    public void AProcessIdNoProcessCanHave_IsReportedAndTheArgumentIsUsed(long requested)
    {
        var resolved = RDCoreServerApp.ClientProcessId(requested, configured: 1234, out var outOfRange);

        Assert.AreEqual(1234, resolved);
        Assert.IsTrue(outOfRange);
    }

    [TestMethod]
    public void WithNeitherSource_NothingIsWatched()
        // 0 is what the caller reads as "no client process to watch", and what it warns about.
        => Assert.AreEqual(0, RDCoreServerApp.ClientProcessId(requested: null, configured: 0, out _));
}
