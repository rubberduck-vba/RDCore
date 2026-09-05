using RDCore.SDK.Client.Connection;

namespace RDCore.Tests.Client;

[TestClass]
public class ChildConnectionTests
{
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
