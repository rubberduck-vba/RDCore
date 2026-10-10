using RDCore.External.Automation;
using RDCore.SDK.Runtime.Libraries;
using RDCore.Tests.Runtime.Libraries;
using System.Globalization;
using System.Reflection;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// The events of an object of a library are handled by the procedures of the <c>WithEvents</c> variables that hold it (<strong>MS-VBAL §5.4.3.9</strong>). When is up to the
/// session: an event that a call raises is handled inside the call, as the server is waiting for the answer; any other is handled between two activations, when the
/// activation that was running has returned and the instruction that called it is done; and when nothing runs, as it arrives.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.9 WithEvents")]
public sealed class ServerEventTests
{
    private static readonly LibraryDescription Rig = new()
    {
        Name = "Rig",
        Classes =
        [
            new ClassDescription
            {
                Name = "Valve",
                ProgId = "Rig.Valve",
                IsCreatable = true,
                Members =
                [
                    new MemberDescription
                    {
                        Name = "Tap", Kind = MemberKind.Method, Type = "String",
                        Parameters = [new ParameterDescription { Name = "Amount", Type = "Long", IsByVal = true }],
                    },
                    new MemberDescription { Name = "Gate", Kind = MemberKind.Method },
                ],
                Events =
                [
                    new EventDescription
                    {
                        Name = "Spilled",
                        Parameters = [new ParameterDescription { Name = "Amount", Type = "Long", IsByVal = true }, new ParameterDescription { Name = "Cancel", Type = "Boolean" }],
                    },
                ],
            },
        ],
    };

    private sealed class Valve
    {
        public Action<string, object?[]>? Raise { get; set; }

        public Action? Gating { get; set; }

        // raises an event during the call, as a server does when what it was asked to do is something that can be cancelled.
        public string Tap(int amount)
        {
            var arguments = new object?[] { amount, false };
            Raise?.Invoke("Spilled", arguments);
            return $"{amount} cancelled={arguments[1]}";
        }

        public void Gate() => Gating?.Invoke();
    }

    // the sink as the server sees it, with a way to know that an event has reached it and waits.
    private sealed class Probe(IAutomationEventSink inner, ManualResetEventSlim waiting) : IAutomationEventSink
    {
        public bool IsOpenForEvents => inner.IsOpenForEvents;

        public IDisposable Waiting()
        {
            waiting.Set();
            return inner.Waiting();
        }

        public void OnEvent(string name, object?[] arguments) => inner.OnEvent(name, arguments);
    }

    private sealed class FakeServer : IAutomationServer
    {
        private readonly Dictionary<object, IAutomationEventSink> _sinks = new(ReferenceEqualityComparer.Instance);

        public List<Valve> Created { get; } = [];

        public ManualResetEventSlim Delivered { get; } = new();

        public List<object> Unadvised { get; } = [];

        public ManualResetEventSlim Gated { get; } = new();

        public ManualResetEventSlim Parked { get; } = new();

        public bool IsAvailable => true;

        public object CreateObject(string progId)
        {
            var valve = new Valve();
            valve.Raise = (name, arguments) => RaiseInCall(valve, name, arguments);
            valve.Gating = () =>
            {
                // a call that takes as long as the server needs to raise an event of its own accord, and returns when that has been handled.
                Gated.Set();
                _ = Delivered.Wait(TimeSpan.FromSeconds(10));
            };

            Created.Add(valve);
            return valve;
        }

        // a call that raises an event: the program waits for the call, so the event is the answer to it.
        private void RaiseInCall(Valve valve, string name, object?[] arguments)
        {
            if (_sinks.TryGetValue(valve, out var sink))
            {
                AutomationEvents.Deliver(sink, name, arguments, synchronous: true, _ => { });
            }
        }

        // something that happens to the server that the program did not ask for.
        public void RaiseAsynchronously(Valve valve, string name, object?[] arguments)
        {
            if (_sinks.TryGetValue(valve, out var sink))
            {
                AutomationEvents.Deliver(sink, name, arguments, synchronous: false, time => Thread.Sleep(time));
                Delivered.Set();
            }
        }

        public object? Invoke(object target, string member, AutomationInvocation invocation, object?[] arguments, bool[] byReference, CultureInfo culture)
        {
            try
            {
                return target.GetType().InvokeMember(
                    member, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase | BindingFlags.InvokeMethod, null, target, arguments, CultureInfo.InvariantCulture);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                throw exception.InnerException;
            }
        }

        public string? ClassNameOf(object target) => "Rig.Valve";

        public bool MoveNext(object enumerator, out object? current)
        {
            current = null;
            return false;
        }

        public void Reset(object enumerator)
        {
        }

        public void Advise(object source, IAutomationEventSink sink) => _sinks[source] = new Probe(sink, Parked);

        public void Unadvise(object source)
        {
            _ = _sinks.Remove(source);
            Unadvised.Add(source);
        }

        public void Release(object handle)
        {
        }
    }

    private static readonly (string Name, string Source)[] Classes =
    [
        ("Watcher", ClassModule(
            "Watcher",
            "Private WithEvents Source As Rig.Valve",
            "Public Sub Watch(ByVal valve As Rig.Valve)",
            "    Set Source = valve",
            "End Sub",
            "Public Sub Unwatch()",
            "    Set Source = Nothing",
            "End Sub",
            "Private Sub Source_Spilled(ByVal Amount As Long, Cancel As Boolean)",
            "    Debug.Print \"event \" & Amount",
            "    Cancel = True",
            "End Sub")),
    ];

    private static string Program(params string[] lines)
        => $"Attribute VB_Name = \"Program\"\r\nPublic Sub Settle()\r\nEnd Sub\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n";

    private static WorkspaceLibraries Libraries(FakeServer server) => new(["Rig"], new InMemoryLibrarySource(Rig), server);

    [TestMethod]
    public async Task AnEventThatACallRaises_IsHandledInsideTheCall_AndWhatTheHandlerLeavesInAnArgumentIsTheServersAnswer()
    {
        var server = new FakeServer();

        var output = await RunAsync(Classes, Program(
            "Dim valve As New Rig.Valve",
            "Dim watcher As New Watcher",
            "watcher.Watch valve",
            "Debug.Print valve.Tap(5)"), Libraries(server));

        CollectionAssert.AreEqual(new[] { "event 5", "5 cancelled=True" }, output);
    }

    [TestMethod]
    public async Task AVariableThatNoLongerHoldsTheObject_HandlesNoEventOfIt_AndTheServerIsToldToStopListening()
    {
        var server = new FakeServer();

        var output = await RunAsync(Classes, Program(
            "Dim valve As New Rig.Valve",
            "Dim watcher As New Watcher",
            "watcher.Watch valve",
            "watcher.Unwatch",
            "Debug.Print valve.Tap(6)"), Libraries(server));

        CollectionAssert.AreEqual(new[] { "6 cancelled=False" }, output);
        Assert.HasCount(1, server.Unadvised);
    }

    [TestMethod]
    public async Task AnEventThatNoCallRaised_ReachesAProgramThatWaitsForACall_AsItWouldReachOneThatDoes()
    {
        var server = new FakeServer();

        // while the program waits for the call, the server raises an event that nobody asked for: the program takes the call the server makes, as MS-VBA does, and the
        // call it was waiting for returns afterwards. (The program runs on this thread until it is over, so the server is another.)
        var raising = Task.Run(() =>
        {
            Assert.IsTrue(server.Gated.Wait(TimeSpan.FromSeconds(10)));
            server.RaiseAsynchronously(server.Created.Single(), "Spilled", [7, false]);
        });

        var output = await RunAsync(Classes, Program(
            "Dim valve As New Rig.Valve",
            "Dim watcher As New Watcher",
            "watcher.Watch valve",
            "valve.Gate",
            "Debug.Print \"called\""), Libraries(server));
        await raising;

        CollectionAssert.AreEqual(new[] { "event 7", "called" }, output);
    }

    [TestMethod]
    public async Task AnEventThatArrivesWhenNothingRuns_IsHandledAsItArrives_AndWhatItPrintsIsSaidByTheHost()
    {
        var server = new FakeServer();
        IReadOnlyList<string> said = [];

        await InspectAsync(
            Classes,
            // what the program leaves in module variables is there for the events that come after it; its locals are gone with it.
            "Attribute VB_Name = \"Program\"\r\nPrivate valve As New Rig.Valve\r\nPrivate watcher As New Watcher\r\nPublic Sub Main()\r\nwatcher.Watch valve\r\nEnd Sub\r\n",
            async (sessionProvider, result, _) =>
            {
                Assert.AreEqual(RDCore.SDK.Platform.Protocol.ExecutionOutcome.Completed, result.Outcome);

                // the program is over, and the objects it held are the session's until it is wiped.
                await Task.Run(() => server.RaiseAsynchronously(server.Created.Single(), "Spilled", [9, false]));
                said = sessionProvider.EventLines;
            },
            libraries: Libraries(server));

        CollectionAssert.AreEqual(new[] { "event 9" }, said.ToArray());
    }
}
