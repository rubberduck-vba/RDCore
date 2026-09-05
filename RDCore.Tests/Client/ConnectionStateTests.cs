using RDCore.SDK.Client.Connection;

namespace RDCore.Tests.Client;

[TestClass]
public class ConnectionStateTests
{
    private static ConnectionState State(ConnectionStateValue value) => value switch
    {
        ConnectionStateValue.NotStarted => ConnectionState.NotStarted,
        ConnectionStateValue.Spawning => ConnectionState.Spawning,
        ConnectionStateValue.Connecting => ConnectionState.Connecting,
        ConnectionStateValue.Initializing => ConnectionState.Initializing,
        ConnectionStateValue.Ready => ConnectionState.Ready,
        ConnectionStateValue.ShuttingDown => ConnectionState.ShuttingDown,
        ConnectionStateValue.Exited => ConnectionState.Exited,
        ConnectionStateValue.Faulted => ConnectionState.Faulted("test"),
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    [TestMethod]
    [DataRow(ConnectionStateValue.NotStarted, ConnectionStateValue.Spawning)]
    [DataRow(ConnectionStateValue.Spawning, ConnectionStateValue.Connecting)]
    [DataRow(ConnectionStateValue.Connecting, ConnectionStateValue.Initializing)]
    [DataRow(ConnectionStateValue.Initializing, ConnectionStateValue.Ready)]
    [DataRow(ConnectionStateValue.Ready, ConnectionStateValue.ShuttingDown)]
    [DataRow(ConnectionStateValue.ShuttingDown, ConnectionStateValue.Exited)]
    [DataRow(ConnectionStateValue.Connecting, ConnectionStateValue.Faulted)]
    [DataRow(ConnectionStateValue.Ready, ConnectionStateValue.Faulted)]
    [DataRow(ConnectionStateValue.Faulted, ConnectionStateValue.Spawning)]
    [DataRow(ConnectionStateValue.Faulted, ConnectionStateValue.Exited)]
    public void AdvanceTo_LegalTransition_ReturnsTarget(ConnectionStateValue from, ConnectionStateValue to)
        => Assert.AreEqual(to, State(from).AdvanceTo(State(to)).Value);

    [TestMethod]
    [DataRow(ConnectionStateValue.NotStarted, ConnectionStateValue.Ready)]
    [DataRow(ConnectionStateValue.Ready, ConnectionStateValue.Connecting)]
    [DataRow(ConnectionStateValue.Ready, ConnectionStateValue.Exited)]
    [DataRow(ConnectionStateValue.Initializing, ConnectionStateValue.ShuttingDown)]
    [DataRow(ConnectionStateValue.Exited, ConnectionStateValue.Spawning)]
    [DataRow(ConnectionStateValue.Exited, ConnectionStateValue.Faulted)]
    public void AdvanceTo_IllegalTransition_Throws(ConnectionStateValue from, ConnectionStateValue to)
        => Assert.Throws<InvalidConnectionStateException>(() => State(from).AdvanceTo(State(to)));

    [TestMethod]
    public void IsUsable_IsTrueOnlyWhenReady()
    {
        Assert.IsTrue(ConnectionState.Ready.IsUsable);
        Assert.IsFalse(ConnectionState.Initializing.IsUsable);
        Assert.IsFalse(ConnectionState.Faulted("x").IsUsable);
    }

    [TestMethod]
    public void IsTerminal_IsTrueOnlyWhenExited()
    {
        Assert.IsTrue(ConnectionState.Exited.IsTerminal);
        Assert.IsFalse(ConnectionState.Faulted("x").IsTerminal);
        Assert.IsFalse(ConnectionState.Ready.IsTerminal);
    }
}
