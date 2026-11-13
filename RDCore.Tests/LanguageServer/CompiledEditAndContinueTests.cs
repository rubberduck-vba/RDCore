using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.DebugAdapter.Client;
using OmniSharp.Extensions.DebugAdapter.Protocol.Events;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;
using OmniSharp.Extensions.DebugAdapter.Server;
using RDCore.Runtime.Support.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.LanguageServer;
using RDCore.LanguageServer.Debugging;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Runtime.Compiled;
using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Client;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Server.Configuration;
using RDCore.Tests.Cli;
using RDCore.Tests.Platform;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using RDCore.SDK.Platform.Components;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// Break, edit, continue, in a procedure that was running compiled: a debugger client speaks DAP to the debug adapter, the
/// adapter has the language server's debug service relay each request to the environment host's own handlers, and the program the host runs is stopped in a
/// compiled loop, edited, and goes on in the edited code - and the next call of the edited procedure compiles the edited body.
/// </summary>
/// <remarks>
/// Only the JSON-RPC hop between the language server and the environment host is not a process's: each request and each answer is written and read back as
/// the transport writes it (<see cref="Wire.RoundTrip"/>), and handed to the handler the host serves it with. The files are the workspace's as the adapter
/// is told of them: what is saved is what it loads when the program is asked to go on.
/// </remarks>
[TestClass]
public sealed class CompiledEditAndContinueTests
{
    // a cap on a hang, not a budget: each wait is on the event itself, which a loaded machine brings as late as it does.
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

    private static string Lines(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static string PathOf(string module) => Path.GetFullPath(Path.Combine(ModuleWorkspace.Root, module + ".bas"));

    // Work's loop compiles. Its Step of 0 in the first call leaves the counter where it is, so that call runs compiled until it is stopped, however late the
    // breakpoint comes; in the second call, with a Step of 1, the loop goes round a thousand times, enough for compiled code to look at its back edge, and ends
    // whichever body runs it.
    private static readonly string[] Work =
    [
        "Function Work(ByVal s As Long) As Long",
        "    Dim i As Long, n As Long",
        "    For i = 1 To 1000 Step s",
        "        n = 1",
        "    Next",
        "    Work = n",
        "End Function",
    ];

    // the line of Work's loop the breakpoint is set on, from the line Work begins on, as the client counts lines: from 1.
    private const int LoopLine = 4;

    // what is saved at the breakpoint: the statement the program waits at is as it was, the loop is left after it in the first call, and the function returns
    // what only the edited code returns.
    private static string Edited(string source) => source
        .Replace("        n = 1\r\n", "        n = 1\r\n        If s = 0 Then Exit For\r\n", StringComparison.Ordinal)
        .Replace("    Work = n\r\n", "    Work = n * 10 + s\r\n", StringComparison.Ordinal);

    // Main waits at its Stop, then calls Work twice.
    private static readonly string Calling = Lines(
    [
        "Public Sub Main()",
        "    Stop",
        "    Debug.Print Work(0)",
        "    Debug.Print Work(1)",
        "End Sub",
        .. Work.Select((line, i) => i == 0 ? "Private " + line : line),
    ]);

    [TestMethod]
    public async Task AProcedureRunningCompiled_StoppedAtABreakpoint_EditedAndContinued_GoesOnInTheEditedCode_AndItsNextCallCompilesTheEditedBody()
    {
        CompiledExecutionTests.RequirePayloads();
        await ModuleWorkspace.InspectAsync([], Calling, async (provider, first, runAgain) =>
        {
            // the activation that was stopped went on in the edited code, out of its loop: 1 * 10 + 0. The next call ran the edited code: 1 * 10 + 1, where the
            // body compiled before the edit returns 1.
            await BreakEditContinueAsync(provider, first, new() { ["Program"] = Calling }, "Program", 5 + LoopLine, ["10", "11"], atBreak: null);
        }, debug: true, engine: VBExecutionEngine.Compiled);
    }

    // Main waits at its Stop, then calls Outer twice, which calls Work - a procedure of another module - from compiled code, directly: the module edited is
    // Work's, and Outer's is not.
    private static readonly string CallingOuter = Lines(
        "Public Sub Main()",
        "    Stop",
        "    Debug.Print Outer(0)",
        "    Debug.Print Outer(1)",
        "End Sub",
        "Private Function Outer(ByVal s As Long) As Long",
        "    Dim t As Long",
        "    t = Work(s)",
        "    Outer = t + 100",
        "End Function");

    private static readonly string Worker = Lines([.. Work.Select((line, i) => i == 0 ? "Public " + line : line)]);

    [TestMethod]
    public async Task AProcedureRunningCompiled_CalledDirectlyByCompiledCodeOfAnotherModule_StoppedEditedAndContinued_ItsNextCallRunsTheEditedBody()
    {
        CompiledExecutionTests.RequirePayloads();
        await ModuleWorkspace.InspectWithModulesAsync([("Worker", Worker)], CallingOuter, async (provider, first, runAgain) =>
        {
            // Outer went on from its call with what the edited Work returned, and its next call called the edited Work.
            await BreakEditContinueAsync(provider, first, new() { ["Worker"] = Worker, ["Program"] = CallingOuter }, "Worker", LoopLine, ["110", "111"], atBreak: async session =>
            {
                // Outer, compiled, waits in its call of Work.
                var frames = (await session.SendAsync(new StackTraceArguments { ThreadId = ProgramDebugAdapter.ThreadId })).StackFrames!.ToArray();
                CollectionAssert.AreEqual(new[] { "Work", "Outer", "Main" }, frames.Select(frame => frame.Name).ToArray());
                if (!CompiledExecution.HandsOverAtRandom)
                {
                    Assert.IsTrue(provider.Session.CallStack.Frames.OfType<CallStackFrame>().ElementAt(1).RunsCompiled, "Outer is running compiled, in the middle of its call");
                }
            });
        }, debug: true, engine: VBExecutionEngine.Compiled);
    }

    // the same, Outer calling Work inside an expression, after reading s: what Outer goes on with is what it read, and what the edited Work returned.
    private static readonly string CallingOuterInside = CallingOuter.Replace("    t = Work(s)\r\n", "    t = s + 7 + Work(s) * 2\r\n", StringComparison.Ordinal);

    [TestMethod]
    public async Task ACallInsideAnExpression_StoppedInTheCallee_EditedAndContinued_GoesOnWithWhatTheEditedCalleeReturned()
    {
        CompiledExecutionTests.RequirePayloads();
        await ModuleWorkspace.InspectWithModulesAsync([("Worker", Worker)], CallingOuterInside, async (provider, first, runAgain) =>
        {
            // Outer finished its statement with s as it read it, and what the edited Work returned: 0 + 7 + 10 * 2, then 1 + 7 + 11 * 2.
            await BreakEditContinueAsync(provider, first, new() { ["Worker"] = Worker, ["Program"] = CallingOuterInside }, "Worker", LoopLine, ["127", "130"],
                atBreak: async session =>
                {
                    var frames = (await session.SendAsync(new StackTraceArguments { ThreadId = ProgramDebugAdapter.ThreadId })).StackFrames!.ToArray();
                    CollectionAssert.AreEqual(new[] { "Work", "Outer", "Main" }, frames.Select(frame => frame.Name).ToArray());
                    if (!CompiledExecution.HandsOverAtRandom)
                    {
                        Assert.IsTrue(provider.Session.CallStack.Frames.OfType<CallStackFrame>().ElementAt(1).RunsCompiled,
                            "Outer is running compiled, in the middle of its statement");
                    }
                });
        }, debug: true, engine: VBExecutionEngine.Compiled);
    }

    // the program, started by the test and waiting at Main's Stop, debugged through the adapter: continued until Work runs compiled; stopped at a breakpoint
    // set in Work's loop then, at line of workModule; edited there, Work's module saved as Edited; and continued to its end, having printed expected. Work,
    // compiled at its first call, is compiled again - the edited body - at its first call after the edit, and runs compiled.
    private static async Task BreakEditContinueAsync(EnvironmentSessionProvider provider, ExecuteResult first, Dictionary<string, string> modules,
        string workModule, int line, string[] expected, Func<Session, Task>? atBreak)
    {
        // the program was started as the platform starts it, under a debugger, and waits at Main's Stop: Work is not called yet.
        Assert.AreEqual((ExecutionOutcome.Suspended, 1), (first.Outcome, first.ErrorLine), first.ErrorMessage);

        var bodies = new ConcurrentQueue<CompiledBody>();
        provider.Image.Compiler.Compiled += (procedure, result) =>
        {
            if (procedure.Name == "Work" && result.Body is { } body)
            {
                bodies.Enqueue(body);
            }
        };
        var looked = WorkLookedAsync(provider);

        await using var session = new Session(provider, first, modules);
        try
        {
            await session.StartAsync();
            _ = await session.SendAsync(new ProgramLaunchRequestArguments { Module = "Program", EntryPoint = "Main" });
            _ = await session.SendAsync(new ConfigurationDoneArguments());
            Assert.AreEqual(StoppedEventReason.Breakpoint, (await session.NextAsync<StoppedEvent>()).Reason, "Main's Stop");

            // Work is called, compiled, and runs compiled: its loop has looked whether the program is to stop.
            _ = await session.SendAsync(new ContinueArguments { ThreadId = ProgramDebugAdapter.ThreadId });
            var running = await looked.WaitAsync(Patience);
            var walkedBack = running.WalkBacks;

            // a breakpoint set in the loop while it runs compiled: the activation walks back to the interpreter, which stops there.
            var set = await session.SetBreakpointsAsync(workModule, line);
            Assert.IsTrue(set.Breakpoints.Single().Verified, set.Breakpoints.Single().Message);
            var hit = await session.NextAsync<StoppedEvent>();
            Assert.AreEqual(StoppedEventReason.Breakpoint, hit.Reason, hit.Description);
            var top = (await session.SendAsync(new StackTraceArguments { ThreadId = ProgramDebugAdapter.ThreadId })).StackFrames!.First();
            Assert.AreEqual(("Work", line, PathOf(workModule)), (top.Name, (int)top.Line, top.Source?.Path));

            // under the random walk-back the activation may have been handed to the interpreter at its first look, before the breakpoint was set, which stops
            // it all the same.
            if (!CompiledExecution.HandsOverAtRandom)
            {
                Assert.AreEqual(walkedBack + 1, running.WalkBacks, "the compiled activation that was running walked back to stop at the breakpoint");
            }

            if (atBreak is not null)
            {
                await atBreak(session);
            }

            // the breakpoint is cleared - a breakpoint set anywhere keeps new activations in the interpreter - and the edit saved; the program goes on.
            _ = await session.SetBreakpointsAsync(workModule);
            session.Workspace.Save(workModule, Edited(modules[workModule]));
            _ = await session.SendAsync(new ContinueArguments { ThreadId = ProgramDebugAdapter.ThreadId });

            _ = await session.NextAsync<TerminatedEvent>();
            var said = session.Pending<OutputEvent>();
            Assert.AreEqual(0, session.Pending<ExitedEvent>().Single().ExitCode, string.Join("", said.Select(output => output.Output)));
            string[] printed = [.. said.Where(output => output.Category == OutputEventCategory.StandardOutput).Select(output => output.Output.Trim())];
            CollectionAssert.AreEqual(expected, printed, string.Join(" | ", printed));
            Assert.AreEqual(1, session.Workspace.Reloads, "the edit was loaded when the program was asked to go on, once");

            // the edited procedure's compiled body was dropped with the edit: its next call compiled the edited code, and ran it compiled.
            var compiled = bodies.ToArray();
            Assert.HasCount(2, compiled, "Work compiled at its first call, and again at its first call after the edit");
            Assert.AreSame(running, compiled[0]);
            Assert.AreNotSame(compiled[0], compiled[1]);
            if (!CompiledExecution.HandsOverAtRandom)
            {
                Assert.IsTrue(compiled[1].Looked.IsCompleted, "the edited body ran its loop compiled");
                Assert.AreEqual(0, compiled[1].WalkBacks, "the edited body ran to its end compiled");
            }
        }
        finally
        {
            _ = await new HostDebugTerminateHandler(provider).Handle(new HostDebugTerminateParams(), CancellationToken.None).WaitAsync(Patience);
        }
    }

    // the compiled body of Work, once its first activation has looked whether the program is to stop, at the back edge of its loop: it runs compiled code
    // then, and a breakpoint set after it is seen at its next look.
    private static Task<CompiledBody> WorkLookedAsync(EnvironmentSessionProvider provider)
    {
        var looked = new TaskCompletionSource<CompiledBody>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.Image.Compiler.Compiled += (procedure, result) =>
        {
            if (procedure.Name == "Work" && result.Body is { } body && !looked.Task.IsCompleted)
            {
                _ = body.Looked.ContinueWith(_ => looked.TrySetResult(body), TaskScheduler.Default);
            }
        };

        return looked.Task;
    }

    /// <summary>
    /// The workspace the adapter debugs: standard modules, a file each, run in the host's session the test was given, and the text saved in each file.
    /// </summary>
    private sealed class InProcessDebugWorkspace(EnvironmentSessionProvider provider, ExecuteResult started, Dictionary<string, string> modules) : IDebugWorkspace
    {
        private readonly TaskCompletionSource<string> _lost = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Dictionary<string, string> _loaded = new(modules, StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _saved = new(modules, StringComparer.OrdinalIgnoreCase);
        private int _reloads;

        /// <summary>
        /// How many times what was saved was loaded in the host.
        /// </summary>
        public int Reloads => Volatile.Read(ref _reloads);

        /// <summary>
        /// Saves the file of <paramref name="module"/> as <paramref name="text"/>: what the adapter loads when the program is next asked to go on.
        /// </summary>
        /// <param name="module">The name of the module whose file is saved.</param>
        /// <param name="text">The text the file is saved with.</param>
        public void Save(string module, string text) => _saved[module] = text;

        public Task<string> Lost => _lost.Task;

        public Task OpenAsync(CancellationToken token) => Task.CompletedTask;

        public bool TryGetModule(string path, out string moduleName)
        {
            moduleName = _loaded.Keys.FirstOrDefault(module => string.Equals(PathOf(module), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
            return moduleName.Length > 0;
        }

        public bool TryGetPath(string moduleName, out string path)
        {
            var found = _loaded.ContainsKey(moduleName);
            path = found ? PathOf(moduleName) : string.Empty;
            return found;
        }

        public Task<IReadOnlyList<string>> ModulesDeclaringAsync(string entryPoint, CancellationToken token) => Task.FromResult<IReadOnlyList<string>>(["Program"]);

        // the program was started in the host as the platform starts it, before the adapter was: what it answered is where it waits.
        public Task<ExecuteResult> StartAsync(string moduleName, string entryPoint, CancellationToken token) => Task.FromResult(Wire.RoundTrip(started));

        // as the platform's workspace does: a file whose text is what was loaded is nothing changed; the ones saved otherwise are loaded in the host, as the
        // language server loads an edit of a document, the names of every module bound among all of them as they now read.
        public async Task<string?> ReloadChangedAsync(CancellationToken token)
        {
            var changed = _saved.Where(module => !string.Equals(module.Value, _loaded[module.Key], StringComparison.Ordinal)).Select(module => module.Key).ToArray();
            if (changed.Length == 0)
            {
                return null;
            }

            var errors = await ModuleWorkspace.ReloadOnlyAsync(provider, changed, [.. _saved.Select(module => (module.Key, ModuleType.StdModule, module.Value))]);
            foreach (var module in changed)
            {
                _loaded[module] = _saved[module];
            }

            _ = Interlocked.Increment(ref _reloads);
            return errors.Count == 0 ? null : string.Join("\n", errors);
        }

        public Task CloseAsync() => Task.CompletedTask;
    }

    /// <summary>
    /// A debugger client, the debug adapter it speaks DAP to, and the language server's debug service the adapter relays to the host through.
    /// </summary>
    private sealed class Session : IAsyncDisposable
    {
        private readonly Lock _eventsLock = new();
        private readonly List<object> _events = [];
        private readonly SemaphoreSlim _arrived = new(0);
        private readonly ProgramDebugAdapter _adapter;
        private DebugAdapterServer? _server;
        private DebugAdapterClient? _client;

        public InProcessDebugWorkspace Workspace { get; }

        public Session(EnvironmentSessionProvider provider, ExecuteResult started, Dictionary<string, string> modules)
        {
            Workspace = new InProcessDebugWorkspace(provider, started, modules);
            var host = EnvironmentHost(provider);
            var orchestration = Substitute.For<IPlatformOrchestrationService>();
            orchestration.RuntimeEnvironment.Returns(host);
            var debugging = new ProgramDebugService(orchestration, Substitute.For<IParsingClientService>(), Options.Create(new SdkAppOptions()));
            var editStatus = new EditStatusMonitor(debugging, Substitute.For<IWorkspaceRefresh>(), NullLogger<EditStatusMonitor>.Instance);
            _adapter = new ProgramDebugAdapter(Workspace, debugging, new HostOutputRelay(new ManualClock()), editStatus, NullLogger<ProgramDebugAdapter>.Instance);
        }

        // the environment host, as the language server reaches it: each request it is sent is handled by the handler the host serves it with, as the other
        // end of the wire reads it, and answered as this end reads the answer.
        private static IRDCoreClientApp EnvironmentHost(EnvironmentSessionProvider provider)
        {
            var host = Substitute.For<IRDCoreClientApp>();
            host.WaitForReadyAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            Serve<HostDebugResumeParams, ExecuteResult>(host, new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance).Handle);
            Serve<HostDebugPauseParams, DebugAck>(host, new HostDebugPauseHandler(provider).Handle);
            Serve<HostDebugBreakpointsParams, DebugBreakpointsResult>(host, new HostDebugBreakpointsHandler(provider).Handle);
            Serve<HostDebugEditStatusParams, DebugEditStatusResult>(host, new HostDebugEditStatusHandler(provider).Handle);
            Serve<HostDebugStackParams, DebugStackResult>(host, new HostDebugStackHandler(provider).Handle);
            Serve<HostDebugVariablesParams, DebugVariablesResult>(host, new HostDebugVariablesHandler(provider).Handle);
            Serve<HostDebugTerminateParams, DebugAck>(host, new HostDebugTerminateHandler(provider).Handle);
            return host;
        }

        private static void Serve<TParams, TResult>(IRDCoreClientApp host, Func<TParams, CancellationToken, Task<TResult>> handle)
            where TParams : IRequest<TResult>
            where TResult : notnull
            => host.SendRequestAsync<TParams, TResult>(Arg.Any<TParams>(), Arg.Any<CancellationToken>())
                .Returns(async call => Wire.RoundTrip(await handle(Wire.RoundTrip(call.Arg<TParams>()), call.Arg<CancellationToken>())));

        public async Task StartAsync()
        {
            var toServer = new Pipe();
            var toClient = new Pipe();

            // the server is started when the client has initialized it.
            var starting = DebugAdapterApp.StartServerAsync(new DebugAdapterTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream()), _adapter);
            _client = await DebugAdapterClient.From(options =>
            {
                options.WithInput(toClient.Reader.AsStream()).WithOutput(toServer.Writer.AsStream());
                options.OnInvalidated(Arrived);
                options.OnStopped(Arrived);
                options.OnOutput(Arrived);
                options.OnExited(Arrived);
                options.OnTerminated(Arrived);
            });
            _server = await starting.WaitAsync(Patience);
            _adapter.Initialize(new InitializeRequestArguments { AdapterId = "rdcore", LinesStartAt1 = true, ColumnsStartAt1 = true, SupportsInvalidatedEvent = true });
        }

        public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request) => _client!.SendRequest(request, CancellationToken.None).WaitAsync(Patience);

        /// <summary>
        /// Sets the breakpoints of the file of <paramref name="module"/> to <paramref name="lines"/>, counted from 1; none clears them.
        /// </summary>
        /// <param name="module">The name of the module whose file the breakpoints are in.</param>
        /// <param name="lines">The lines the breakpoints are set on, counted from 1.</param>
        public Task<SetBreakpointsResponse> SetBreakpointsAsync(string module, params int[] lines) => SendAsync(new SetBreakpointsArguments
        {
            Source = new Source { Path = PathOf(module) },
            Breakpoints = new Container<SourceBreakpoint>(lines.Select(line => new SourceBreakpoint { Line = line })),
        });

        private void Arrived(object @event)
        {
            lock (_eventsLock)
            {
                _events.Add(@event);
            }

            _arrived.Release();
        }

        /// <summary>
        /// The next event of the type, whenever it comes; the events before it of other types are kept.
        /// </summary>
        /// <typeparam name="TEvent">The type of the event waited for.</typeparam>
        public async Task<TEvent> NextAsync<TEvent>()
        {
            using var hangGuard = new CancellationTokenSource(Patience);
            while (true)
            {
                // the event is taken out where it is, under the lock an event that arrives is added under: the others stay in the order they came.
                lock (_eventsLock)
                {
                    var at = _events.FindIndex(next => next is TEvent);
                    if (at >= 0)
                    {
                        var found = (TEvent)_events[at];
                        _events.RemoveAt(at);
                        return found;
                    }
                }

                // an event that came since the queue was looked at has released the semaphore already: it is not missed.
                try
                {
                    await _arrived.WaitAsync(hangGuard.Token);
                }
                catch (OperationCanceledException)
                {
                    Assert.Fail($"no {typeof(TEvent).Name} came");
                }
            }
        }

        /// <summary>
        /// Everything that was said and not asked for yet, of one kind, in the order it was said.
        /// </summary>
        /// <typeparam name="TEvent">The type of the events.</typeparam>
        public IReadOnlyList<TEvent> Pending<TEvent>()
        {
            lock (_eventsLock)
            {
                return [.. _events.OfType<TEvent>()];
            }
        }

        public async ValueTask DisposeAsync()
        {
            _client?.Dispose();
            _server?.Dispose();
            await Task.CompletedTask;
        }
    }
}
