using Microsoft.Extensions.Configuration;
using NSubstitute;
using RDCore.SDK.Server.Services.States;

namespace RDCore.Tests.Server;

[TestClass]
public class ServerStateProviderTests
{
    private IConfiguration TestConfiguration = Substitute.For<IConfiguration>();

    [TestMethod]
    public void Uninitialized_State_IsStartingState()
    {
        // arrange
        var sut = new ServerStateProvider(TestConfiguration);

        // act
        var result = sut.State;

        // assert
        Assert.IsInstanceOfType<StartingServerState>(result);
    }

    [TestMethod]
    [DataRow(null, ServerStateValue.Initializing)]
    [DataRow(ServerStateValue.Starting, ServerStateValue.Initializing)]
    [DataRow(ServerStateValue.Initializing, null)]
    [DataRow(ServerStateValue.Running, null)]
    [DataRow(ServerStateValue.RunningVerbose, null)]
    [DataRow(ServerStateValue.ShuttingDown, null)]
    [DataRow(ServerStateValue.Exiting, null)]
    public void OnInitialize(ServerStateValue? initialState, ServerStateValue? expectedState)
    => TestServerStateTransition(sut => sut.OnInitialize(), initialState, expectedState);

    [TestMethod]
    [DataRow(null, null)]
    [DataRow(ServerStateValue.Starting, null)]
    [DataRow(ServerStateValue.Initializing, ServerStateValue.Running)]
    [DataRow(ServerStateValue.Running, null)]
    [DataRow(ServerStateValue.RunningTraceless, null)]
    [DataRow(ServerStateValue.RunningVerbose, null)]
    [DataRow(ServerStateValue.ShuttingDown, null)]
    [DataRow(ServerStateValue.Exiting, null)]
    public void OnInitialized(ServerStateValue? initialState, ServerStateValue? expectedState)
    => TestServerStateTransition(sut => sut.OnInitialized(), initialState, expectedState);

    [TestMethod]
    [DataRow(null, null)]
    [DataRow(ServerStateValue.Starting, null)]
    [DataRow(ServerStateValue.Initializing, null)]
    [DataRow(ServerStateValue.Running, ServerStateValue.ShuttingDown)]
    [DataRow(ServerStateValue.RunningTraceless, ServerStateValue.ShuttingDown)]
    [DataRow(ServerStateValue.RunningVerbose, ServerStateValue.ShuttingDown)]
    [DataRow(ServerStateValue.ShuttingDown, null)]
    [DataRow(ServerStateValue.Exiting, null)]
    public void OnShutdown(ServerStateValue? initialState, ServerStateValue? expectedState)
    => TestServerStateTransition(sut => sut.OnShutdown(), initialState, expectedState);

    [TestMethod]
    [DataRow(null, ServerStateValue.Exiting)]
    [DataRow(ServerStateValue.Starting, ServerStateValue.Exiting)]
    [DataRow(ServerStateValue.Initializing, ServerStateValue.Exiting)]
    [DataRow(ServerStateValue.Running, ServerStateValue.Exiting)]
    [DataRow(ServerStateValue.RunningTraceless, ServerStateValue.Exiting)]
    [DataRow(ServerStateValue.RunningVerbose, ServerStateValue.Exiting)]
    [DataRow(ServerStateValue.ShuttingDown, ServerStateValue.Exiting)]
    [DataRow(ServerStateValue.Exiting, ServerStateValue.Exiting)]
    public void OnExit(ServerStateValue? initialState, ServerStateValue? expectedState)
    => TestServerStateTransition(sut => sut.OnExit(), initialState, expectedState);

    [TestMethod]
    [DataRow(null, null)]
    [DataRow(ServerStateValue.Starting, null)]
    [DataRow(ServerStateValue.Initializing, null)]
    [DataRow(ServerStateValue.Running, ServerStateValue.Running)]
    [DataRow(ServerStateValue.RunningTraceless, ServerStateValue.Running)]
    [DataRow(ServerStateValue.RunningVerbose, ServerStateValue.Running)]
    [DataRow(ServerStateValue.ShuttingDown, null)]
    [DataRow(ServerStateValue.Exiting, null)]
    public void OnTraceMessages(ServerStateValue? initialState, ServerStateValue? expectedState)
    => TestServerStateTransition(sut => sut.OnTraceMessages(), initialState, expectedState);

    [TestMethod]
    [DataRow(null, null)]
    [DataRow(ServerStateValue.Starting, null)]
    [DataRow(ServerStateValue.Initializing, null)]
    [DataRow(ServerStateValue.Running, ServerStateValue.RunningVerbose)]
    [DataRow(ServerStateValue.RunningTraceless, ServerStateValue.RunningVerbose)]
    [DataRow(ServerStateValue.RunningVerbose, ServerStateValue.RunningVerbose)]
    [DataRow(ServerStateValue.ShuttingDown, null)]
    [DataRow(ServerStateValue.Exiting, null)]
    public void OnTraceVerbose(ServerStateValue? initialState, ServerStateValue? expectedState)
    => TestServerStateTransition(sut => sut.OnTraceVerbose(), initialState, expectedState);

    [TestMethod]
    [DataRow(null, null)]
    [DataRow(ServerStateValue.Starting, null)]
    [DataRow(ServerStateValue.Initializing, null)]
    [DataRow(ServerStateValue.Running, ServerStateValue.RunningTraceless)]
    [DataRow(ServerStateValue.RunningTraceless, ServerStateValue.RunningTraceless)]
    [DataRow(ServerStateValue.RunningVerbose, ServerStateValue.RunningTraceless)]
    [DataRow(ServerStateValue.ShuttingDown, null)]
    [DataRow(ServerStateValue.Exiting, null)]
    public void OnTraceOff(ServerStateValue? initialState, ServerStateValue? expectedState)
    => TestServerStateTransition(sut => sut.OnTraceOff(), initialState, expectedState);

    private void TestServerStateTransition(Action<ServerStateProvider> act, ServerStateValue? initialState, ServerStateValue? expectedState)
    {
        // arrange
        var sut = new ServerStateProvider(TestConfiguration);

        if (initialState.HasValue)
        {
            switch (initialState.Value)
            {
                case ServerStateValue.Starting:
                    // already in that state, nothing to do.
                    break;
                case ServerStateValue.Initializing:
                    sut.OnInitialize();
                    break;
                case ServerStateValue.Running:
                case ServerStateValue.RunningVerbose:
                case ServerStateValue.RunningTraceless:
                    sut.OnInitialize();
                    sut.OnInitialized();
                    break;
                case ServerStateValue.ShuttingDown:
                    sut.OnInitialize();
                    sut.OnInitialized();
                    sut.OnShutdown();
                    break;
                case ServerStateValue.Exiting:
                    sut.OnInitialize();
                    sut.OnInitialized();
                    sut.OnShutdown();
                    sut.OnExit();
                    break;
            }
        }

        if (expectedState.HasValue)
        {
            act.Invoke(sut);

            var result = sut.State;
            Assert.AreEqual(expectedState, result.Value);
        }
        else
        {
            Assert.Throws<InvalidServerStateException>(() => act.Invoke(sut));
        }
    }
}
