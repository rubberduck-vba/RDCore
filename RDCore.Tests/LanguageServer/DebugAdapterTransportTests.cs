using RDCore.LanguageServer.Debugging;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// A client that is gone is known by the stream it spoke over having nothing more to say.
/// </summary>
[TestClass]
public sealed class DebugAdapterTransportTests
{
    [TestMethod]
    public async Task TheInput_PassesOnWhatTheClientSays_AndEndsWhenTheClientIsGone()
    {
        var transport = new DebugAdapterTransport(new MemoryStream([1, 2, 3]), new MemoryStream());
        var buffer = new byte[8];

        var read = await transport.Input.ReadAsync(buffer);
        Assert.AreEqual(3, read);
        Assert.IsFalse(transport.InputEnded.IsCompleted, "the client may still say more");

        read = await transport.Input.ReadAsync(buffer);
        Assert.AreEqual(0, read);
        await transport.InputEnded.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
