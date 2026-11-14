using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.Host.Handlers;
using RDCore.External.Automation;
using RDCore.Parsing;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Support.Host;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.Runtime.Execution;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.Tests.Runtime.Libraries;
using MemberKind = RDCore.SDK.Runtime.Libraries.MemberKind;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Libraries;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A program that waits in a call - a modal form's <c>Show</c> - handles the events the call raises on the thread of the server that raised them, and a <c>Stop</c>, a
/// breakpoint or a failed <c>Debug.Assert</c> in a handler stops the program there, as MS-VBA's does: the handler goes on when the program is resumed, and the program
/// after it. While it waits, the server's thread makes the calls a debugger asks of it, and handles no other event of the program.
/// </summary>
[TestClass]
[TestCategory("Debugging")]
public sealed class EventHandlerBreakTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static readonly LibraryDescription Rig = new()
    {
        Name = "Rig",
        Classes =
        [
            new ClassDescription
            {
                Name = "Pane",
                ProgId = "Rig.Pane",
                IsCreatable = true,
                Members =
                [
                    new MemberDescription { Name = "Show", Kind = MemberKind.Method },
                    new MemberDescription { Name = "Ping", Kind = MemberKind.Method },
                    new MemberDescription { Name = "Caption", Kind = MemberKind.PropertyGet, Type = "String" },
                ],
                Events =
                [
                    new EventDescription { Name = "Click" },
                    new EventDescription { Name = "Tick" },
                    new EventDescription { Name = "Pinged" },
                ],
            },
        ],
    };

    // a pane of a server with a thread of its own, as an apartment of COM objects is: its objects are called on it, and its events raised on it. Show waits, on that
    // thread, until the pane is closed, doing what is asked of the thread meanwhile - a click among it.
    private sealed class Pane
    {
        public required Apartment Apartment { get; init; }

        public bool Closed { get; set; }

        public void Show()
        {
            while (!Closed)
            {
                Apartment.Serve(Timeout.InfiniteTimeSpan);
            }
        }

        public void Ping() => Apartment.Raise(this, "Pinged", synchronous: true);

        public string Caption => "the pane";
    }

    private sealed class Apartment : IAutomationServer, IDisposable
    {
        private readonly BlockingCollection<Action> _work = [];
        private readonly Thread _thread;
        private readonly Dictionary<object, IAutomationEventSink> _sinks = new(ReferenceEqualityComparer.Instance);

        public Apartment()
        {
            _thread = new Thread(() =>
            {
                foreach (var work in _work.GetConsumingEnumerable())
                {
                    work();
                }
            })
            { IsBackground = true, Name = "test apartment" };
            _thread.Start();
        }

        public List<Pane> Created { get; } = [];

        // completed when the pane is shown, and the program waits in its Show.
        public TaskCompletionSource Shown { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsAvailable => true;

        // what is asked of the thread while it waits: one piece of work, or none in the time given.
        public void Serve(TimeSpan time)
        {
            if (_work.TryTake(out var work, time))
            {
                work();
            }
        }

        // runs on the thread, and waits for it: at once when called on it.
        private T On<T>(Func<T> call)
        {
            if (Thread.CurrentThread == _thread)
            {
                return call();
            }

            using var done = new ManualResetEventSlim();
            T result = default!;
            System.Runtime.ExceptionServices.ExceptionDispatchInfo? failed = null;
            _work.Add(() =>
            {
                try
                {
                    result = call();
                }
                catch (Exception exception)
                {
                    failed = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception);
                }
                finally
                {
                    done.Set();
                }
            });
            done.Wait();
            failed?.Throw();
            return result;
        }

        // an event raised on the thread: a synchronous one is the answer to the call the program waits in (a click on the modal pane).
        public void Raise(Pane pane, string name, bool synchronous)
        {
            if (_sinks.TryGetValue(pane, out var sink))
            {
                sink.OnEvent(new AutomationEvent(name, [], isSynchronous: synchronous, Serve));
            }
        }

        // clicks the pane, on its thread: completed when the click has been handled.
        public Task Click(Pane pane)
        {
            var handled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _work.Add(() =>
            {
                try
                {
                    Raise(pane, "Click", synchronous: true);
                }
                finally
                {
                    handled.SetResult();
                }
            });
            return handled.Task;
        }

        // closes the pane, which its Show returns from.
        public void Close(Pane pane) => _work.Add(() => pane.Closed = true);

        /// <summary>
        /// Raises an event the program did not ask for, from a thread of its own (a timer), and says each time it waits for the session to be open to it: completed when
        /// it has been handled.
        /// </summary>
        public Task Tick(Pane pane, Action waited)
            => Task.Run(() =>
            {
                if (_sinks.TryGetValue(pane, out var sink))
                {
                    sink.OnEvent(new AutomationEvent("Tick", [], isSynchronous: false, time =>
                    {
                        waited();
                        Thread.Sleep(time);
                    }));
                }
            });

        public object CreateObject(string progId)
        {
            var pane = new Pane { Apartment = this };
            Created.Add(pane);
            return pane;
        }

        public object? Invoke(object target, string member, AutomationInvocation invocation, object?[] arguments, bool[] byReference, CultureInfo culture)
            => On(() =>
            {
                var pane = (Pane)target;
                switch (member.ToUpperInvariant())
                {
                    case "SHOW":
                        Shown.TrySetResult();
                        pane.Show();
                        return null;
                    case "PING":
                        pane.Ping();
                        return null;
                    case "CAPTION":
                        return pane.Caption;
                    default:
                        throw new AutomationException(unchecked((int)0x80020003), $"no member {member}");
                }
            });

        public string? ClassNameOf(object target) => "Rig.Pane";

        public bool MoveNext(object enumerator, out object? current)
        {
            current = null;
            return false;
        }

        public void Reset(object enumerator)
        {
        }

        public void Advise(object source, IAutomationEventSink sink) => On(() => _sinks[source] = sink);

        public void Unadvise(object source) => On(() => _sinks.Remove(source));

        public void Release(object handle)
        {
        }

        public void Dispose() => _work.CompleteAdding();
    }

    // the pane's handlers: the first click stops in its handler - at a Stop, or a Debug.Assert that fails - and the others do not.
    private static (string Name, string Source)[] Classes(string stop = "If Clicks = 1 Then Stop") =>
    [
        ("Watcher", ClassModule(
            "Watcher",
            "Private WithEvents Source As Rig.Pane",
            "Private Clicks As Long",
            "Public Sub Watch(ByVal pane As Rig.Pane)",
            "    Set Source = pane",
            "End Sub",
            "Private Sub Source_Click()",
            "    Clicks = Clicks + 1",
            "    Debug.Print \"click \" & Clicks",
            "    " + stop,
            "    Debug.Print \"click \" & Clicks & \" end\"",
            "End Sub",
            "Private Sub Source_Tick()",
            "    Debug.Print \"tick\"",
            "End Sub",
            "Private Sub Source_Pinged()",
            "    Debug.Print \"pinged\"",
            "End Sub")),
    ];

    // Watcher's lines, from 0 at the first of its file (five lines of header): the Stop, the line after it, its End Sub, and the first statement of the handler.
    private const int StopLine = 13;
    private const int AfterStopLine = 14;
    private const int EndSubLine = 15;
    private const int FirstLine = 11;

    private const string Program =
        "Attribute VB_Name = \"Program\"\r\n" +
        "Public Sub Main()\r\n" +
        "    Dim pane As New Rig.Pane\r\n" +
        "    Dim watcher As New Watcher\r\n" +
        "    watcher.Watch pane\r\n" +
        "    pane.Show\r\n" +
        "    Debug.Print \"after show\"\r\n" +
        "End Sub\r\n";

    // Program's line after the Show.
    private const int AfterShowLine = 6;

    private static WorkspaceLibraries Libraries(Apartment server) => new(["Rig"], new InMemoryLibrarySource(Rig), server);

    private static Task<ExecuteResult> Resume(EnvironmentSessionProvider provider, StepKind? step = null)
        => new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
            .Handle(new HostDebugResumeParams { Step = step }, CancellationToken.None).WaitAsync(Patience);

    private static Task<DebugAck> Terminate(EnvironmentSessionProvider provider)
        => Task.Run(() => new HostDebugTerminateHandler(provider).Handle(new HostDebugTerminateParams(), CancellationToken.None)).WaitAsync(Patience);

    private static SyntaxNode Statement(string text)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Execute.bas"), $"Public Sub E()\r\n{text}\r\nEnd Sub\r\n");
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
        return parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single().Children.OfType<StatementNode>().Single();
    }

    // a statement typed at the stop, in the innermost frame.
    private static Task<DebugEvaluateResult> Immediate(EnvironmentSessionProvider provider, string statement)
        => new HostDebugExecuteHandler(provider, Substitute.For<IErrorDetailsRecorder>())
            .Handle(new HostDebugExecuteParams { FrameId = 0, Json = PlatformJson.Serialize(Statement(statement)) }, CancellationToken.None).WaitAsync(Patience);

    private static string[] Lines(IEnumerable<string> output) => [.. output.Select(line => line.Trim())];

    // runs Main under a debugger with a fresh apartment, clicks the pane once it is shown, and hands the stop the click's handler made to `test`.
    private static async Task StoppedInTheHandlerAsync(VBExecutionEngine engine, Func<EnvironmentSessionProvider, Apartment, Task, ExecuteResult, Task> test,
        string stop = "If Clicks = 1 Then Stop")
    {
        using var server = new Apartment();

        // the click is made once the program waits in Show: the first answer of the run is then the stop in its handler.
        var clicked = server.Shown.Task.WaitAsync(Patience).ContinueWith(_ => server.Click(server.Created.Single()), TaskScheduler.Default).Unwrap();
        await InspectAsync(Classes(stop), Program, async (provider, stopped, _) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);
            await test(provider, server, clicked, stopped);
        },
        debug: true, libraries: Libraries(server), engine: engine);
    }

    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, "If Clicks = 1 Then Stop")]
    [DataRow(VBExecutionEngine.Compiled, "If Clicks = 1 Then Stop")]
    [DataRow(VBExecutionEngine.Interpreter, "Debug.Assert Clicks <> 1")]
    [DataRow(VBExecutionEngine.Compiled, "Debug.Assert Clicks <> 1")]
    public async Task AStopInAHandlerOfAnEventOfACallTheProgramWaitsIn_StopsThere_AndTheHandlerThenTheProgramGoOn(VBExecutionEngine engine, string stop)
        => await StoppedInTheHandlerAsync(engine, stop: stop, test: async (provider, server, clicked, stopped) =>
        {
            Assert.AreEqual(StopLine, stopped.ErrorLine);
            CollectionAssert.AreEqual(new[] { "click 1" }, Lines(stopped.Output));
            Assert.IsFalse(clicked.IsCompleted, "the server waits for the handler to be done");

            var frames = provider.Execution.Stack().Frames;
            CollectionAssert.AreEqual(new[] { "Source_Click", "Main" }, frames.Select(frame => frame.Procedure).ToArray());
            CollectionAssert.AreEqual(new[] { true, false }, frames.Select(frame => frame.BeginsAnEvent).ToArray(), "the handler begins the event; Main is in the call");

            var resumed = Resume(provider);
            await clicked.WaitAsync(Patience);
            server.Close(server.Created.Single());
            var done = await resumed;

            Assert.AreEqual(ExecutionOutcome.Completed, done.Outcome, done.ErrorMessage);
            CollectionAssert.AreEqual(new[] { "click 1 end", "after show" }, Lines(done.Output), string.Join(" | ", done.Output));
        });

    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter)]
    [DataRow(VBExecutionEngine.Compiled)]
    public async Task WhileStoppedInAHandler_TheServersThreadMakesTheCallsADebuggerAsksOf_AndTheirOwnEventsAreHandled(VBExecutionEngine engine)
        => await StoppedInTheHandlerAsync(engine, async (provider, server, clicked, _) =>
        {
            // the stopped thread is the only one the pane is called on: the statement's calls are made there, and what its own call raises is handled at once.
            var read = await Immediate(provider, "Debug.Print Source.Caption");
            Assert.IsTrue(read.Success, read.Error);
            CollectionAssert.AreEqual(new[] { "the pane" }, Lines(read.Output));

            var pinged = await Immediate(provider, "Source.Ping");
            Assert.IsTrue(pinged.Success, pinged.Error);
            CollectionAssert.AreEqual(new[] { "pinged" }, Lines(pinged.Output));

            Assert.AreEqual(ProgramState.Suspended, provider.Execution.State, "the program still waits");
            var resumed = Resume(provider);
            await clicked.WaitAsync(Patience);
            server.Close(server.Created.Single());
            Assert.AreEqual(ExecutionOutcome.Completed, (await resumed).Outcome);
        });

    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter)]
    [DataRow(VBExecutionEngine.Compiled)]
    public async Task AnEventOfTheProgramThatComesWhileItIsStoppedInAHandler_WaitsForTheResume_EvenWhileAStatementAtTheStopCalls(VBExecutionEngine engine)
        => await StoppedInTheHandlerAsync(engine, async (provider, server, clicked, _) =>
        {
            // the tick says each time it waits for the session to be open to it: twice is twice it was found closed.
            var waits = 0;
            var waitedTwice = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tick = server.Tick(server.Created.Single(), () =>
            {
                if (Interlocked.Increment(ref waits) == 2)
                {
                    waitedTwice.TrySetResult();
                }
            });
            await waitedTwice.Task.WaitAsync(Patience);

            // a call of a statement at the stop gives the turn up to its own event, not to the program's that waits.
            var pinged = await Immediate(provider, "Source.Ping");
            CollectionAssert.AreEqual(new[] { "pinged" }, Lines(pinged.Output));
            Assert.IsFalse(tick.IsCompleted, "no event of the program is handled while it is stopped");

            var resumed = Resume(provider);
            await clicked.WaitAsync(Patience);
            await tick.WaitAsync(Patience);
            server.Close(server.Created.Single());
            var done = await resumed;

            Assert.AreEqual(ExecutionOutcome.Completed, done.Outcome, done.ErrorMessage);
            CollectionAssert.AreEqual(new[] { "click 1 end", "tick", "after show" }, Lines(done.Output), $"the tick after the handler it waited for: {string.Join(" | ", done.Output)}");
        });

    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter)]
    [DataRow(VBExecutionEngine.Compiled)]
    public async Task AStopInAProcedureAStatementAtTheStopCalls_StopsTheStatement_AndTheProgramStillWaitsInTheHandler(VBExecutionEngine engine)
        => await StoppedInTheHandlerAsync(engine, async (provider, server, clicked, _) =>
        {
            // the handler called again stops at its Stop again, with Clicks back at 0: that Stop is the statement's, not the program's.
            Assert.IsTrue((await Immediate(provider, "Clicks = 0")).Success);
            var stopped = await Immediate(provider, "Source_Click");

            Assert.IsFalse(stopped.Success, "the statement was stopped");
            Assert.AreEqual(ProgramState.Suspended, provider.Execution.State, "the program waits where it did");
            Assert.AreEqual("Source_Click", provider.Execution.Stack().Frames[0].Procedure);

            var resumed = Resume(provider);
            await clicked.WaitAsync(Patience);
            server.Close(server.Created.Single());
            Assert.AreEqual(ExecutionOutcome.Completed, (await resumed).Outcome);
        });

    // MS-VBA, measured in Excel: a step past the handler's End Sub runs on; the next instruction at a depth no deeper than the handler's stops - the
    // Sub line of the next event's handler, or the line after the call - and a step out of the handler only stops above it. A step into a procedure stops at its first
    // statement in RD-VBA, not at its Sub line (ResumeTests.AStepInto_AtACall_StopsInTheProcedureThatIsCalled), and so it does in the next handler.
    [TestMethod]
    [DataRow(StepKind.Over, true, FirstLine, DisplayName = "Over past End Sub, then a click: the next handler's first statement")]
    [DataRow(StepKind.Into, true, FirstLine, DisplayName = "Into past End Sub, then a click: the next handler's first statement")]
    [DataRow(StepKind.Over, false, AfterShowLine, DisplayName = "Over past End Sub, then the pane closed: the line after Show")]
    [DataRow(StepKind.Out, true, AfterShowLine, DisplayName = "Out of the handler, then a click and the pane closed: the line after Show")]
    public async Task AStepThatLeavesAHandler_StopsWhereMsVbaDoes(StepKind step, bool clickAgain, int expectedLine)
        => await StoppedInTheHandlerAsync(VBExecutionEngine.Interpreter, async (provider, server, clicked, _) =>
        {
            var pane = server.Created.Single();
            if (step is not StepKind.Out)
            {
                Assert.AreEqual(AfterStopLine, (await Resume(provider, step)).ErrorLine);
                Assert.AreEqual(EndSubLine, (await Resume(provider, step)).ErrorLine);
            }

            var left = Resume(provider, step);
            await clicked.WaitAsync(Patience);
            if (clickAgain)
            {
                var second = server.Click(pane);
                if (expectedLine != FirstLine)
                {
                    await second.WaitAsync(Patience);
                }
            }

            if (!clickAgain || expectedLine != FirstLine)
            {
                server.Close(pane);
            }

            var hit = await left;
            Assert.AreEqual(ExecutionOutcome.Suspended, hit.Outcome, hit.ErrorMessage);
            Assert.AreEqual(expectedLine, hit.ErrorLine, $"{hit.ErrorMessage} | {string.Join(" <- ", provider.Execution.Stack().Frames.Select(frame => $"{frame.Procedure}@{frame.Line}"))} | {string.Join(" | ", hit.Output)}");

            if (expectedLine == FirstLine)
            {
                server.Close(pane);
            }

            Assert.AreEqual(ExecutionOutcome.Completed, (await Resume(provider)).Outcome);
        });

    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter)]
    [DataRow(VBExecutionEngine.Compiled)]
    public async Task ATerminate_WhileStoppedInAHandler_EndsTheProgram_AndNoHandlerRunsOnTheWayOut(VBExecutionEngine engine)
        => await StoppedInTheHandlerAsync(engine, async (provider, server, clicked, _) =>
        {
            var terminated = Terminate(provider);
            await clicked.WaitAsync(Patience);

            // the program is ending: an event the server raises as it closes its pane finds no code of the program to run.
            var said = new List<string>();
            provider.Output.Target = new RuntimeOutputBuffer(said.Add);
            await server.Click(server.Created.Single()).WaitAsync(Patience);
            server.Close(server.Created.Single());

            Assert.IsTrue((await terminated).Acted);
            Assert.AreEqual(ProgramState.Idle, provider.Execution.State);
            Assert.IsEmpty(said, $"no handler ran after the terminate: {string.Join(" | ", said)}");
        });
}
