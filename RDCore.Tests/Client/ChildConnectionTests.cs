using Microsoft.Extensions.Logging;
using NSubstitute;
using RDCore.SDK.Client;
using RDCore.SDK.Client.Connection;
using RDCore.SDK.Server;

namespace RDCore.Tests.Client;

[TestClass]
public class ChildConnectionTests
{
    [TestMethod]
    public void Dispose_IsIdempotent()
    {
        var sut = new ChildConnection(
            Substitute.For<IRDCoreServerProcess>(),
            Substitute.For<ILanguageServerProtocolTransportLayer>(),
            Substitute.For<ILogger<ChildConnection>>());

        sut.Dispose();
        sut.Dispose(); // the CLI disposes via the host container and again explicitly

        Assert.AreEqual(ConnectionStateValue.NotStarted, sut.State.Value);
    }

    [TestMethod]
    [DataRow(0, 500, 10000, 500)]
    [DataRow(1, 500, 10000, 1000)]
    [DataRow(2, 500, 10000, 2000)]
    [DataRow(3, 500, 10000, 4000)]
    [DataRow(4, 500, 10000, 8000)]
    [DataRow(5, 500, 10000, 10000)]   // capped
    [DataRow(10, 500, 10000, 10000)]  // stays capped, no overflow
    [DataRow(0, 250, 1000, 250)]
    public void RestartDelayMs_IsExponentialBackoffCappedAtMax(int attempt, int baseMs, int maxMs, int expected)
        => Assert.AreEqual(expected, ChildConnection.RestartDelayMs(attempt, baseMs, maxMs));
}
