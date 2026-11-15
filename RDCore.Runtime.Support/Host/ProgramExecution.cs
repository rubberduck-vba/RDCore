using Microsoft.Extensions.Logging;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.Runtime.LanguageCore.Static;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.Symbols;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Collections.Immutable;
using RDCore.SDK.Platform.Components;
using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Runtime.Support.Host;

/// <summary>
/// Whether the host is running a program.
/// </summary>
public enum ProgramState
{
    /// <summary>There is no program. A request to run one is accepted.</summary>
    Idle,

    /// <summary>A program is running, and so owns the session.</summary>
    Running,

    /// <summary>A program that was run under a debugger waits where it stopped, with its activations on the call stack.</summary>
    Suspended,
}

/// <summary>
/// The one owner of the program that runs in the host's session: it starts it, and it is what resumes, steps, pauses and ends it.
/// </summary>
/// <remarks>
/// A program is a thread and a call stack, and the session has one of each to give. Everything that would run something else in the session while a program waits
/// at a <c>Stop</c> - another run, a module taken out from under its activations - used to be free to, and wrecked it; this is what says no.
/// <para>
/// A run that is not under a debugger is what it always was: it runs to the end on the thread of the request, and a <c>Stop</c> ends it. A run under a debugger
/// is a <see cref="SuspendableExecution"/>: a <c>Stop</c> suspends it, the request that ran it answers <see cref="ExecutionOutcome.Suspended"/>, and
/// <see cref="ResumeAsync"/> is the request that runs it on to the next place it waits or its end.
/// </para>
/// <para>
/// A suspended program is resumed only on the code it was suspended with. The code of each module is fingerprinted when the program stops, and again when it is asked
/// to go on; the analysis pass is free to load whatever it likes in between, because what matters is whether the code came out the same.
/// </para>
/// </remarks>
/// <param name="provider">The provider of the session, which is composed again from time to time, and with it this.</param>
/// <param name="logger">Where a failure of the host itself, met while running something at a stop, is said.</param>
public sealed class ProgramExecution(IEnvironmentSessionProvider provider, ILogger? logger = null)
{
    private readonly Lock _sync = new();
    private ProgramState _state;
    private SuspendableExecution? _execution;
    private RuntimeOutputBuffer _output = new();
    private ProgramEdits? _suspendedWith;
    private bool _terminating;
    private bool _wipeOnTerminate;

    // whether the program that runs was started under a debugger, which can stop it; and a stop asked of it before it executes, which it is asked as it starts.
    private bool _debugging;
    private bool _stopWhenStarted;
    private TaskCompletionSource _idle = Settled();
    private readonly ProgramInspector _inspector = new(provider);
    private bool _streaming;
    private long _streamed;
    private bool _inTurn;

    // a statement or an expression runs alongside the program that waits, in its session and on its stack: nothing else resumes, moves, ends or runs beside the program
    // until it is done. Set and cleared with the lock held; the code itself runs with nothing locked (OffTheConnectionAsync).
    private bool _alongside;

    // the session's gate while no program runs, when somebody is told of a handler that stops (IEnvironmentSessionProvider.SessionStopped): the handler that
    // waits at it is then the program that waits.
    private SuspendableExecution? _sessionEvents;

    /// <summary>
    /// Holds the handler of an event of the session that stops while no program runs, or lets it be stopped, as
    /// <see cref="IEnvironmentSessionProvider.SessionStopped"/> says. Called when it is set.
    /// </summary>
    internal void SessionStopsChanged()
    {
        lock (_sync)
        {
            if (_state is ProgramState.Idle)
            {
                OpenToSessionStops();
            }
        }
    }

    // with no program, a handler that stops waits when somebody is told of it, and is stopped otherwise. A breakpoint is waited at only under a gate, and so is
    // never one with nobody to tell. Called with the lock held.
    private void OpenToSessionStops()
    {
        var session = provider.Session;
        if (provider.SessionStopped is null)
        {
            CloseToSessionStops(session);
            return;
        }

        _sessionEvents ??= SuspendableExecution.ForSessionEvents(session, TellSessionStopAsync);
        session.Halt.Gate = _sessionEvents;
    }

    // a program runs: the session's gate is not its. Called with the lock held.
    private void CloseToSessionStops(IRuntimeSession session)
    {
        if (_sessionEvents is not null && ReferenceEquals(session.Halt.Gate, _sessionEvents))
        {
            session.Halt.Gate = null;
        }
    }

    // A handler of an event of the session stopped (SuspendableExecution.ForSessionEvents): it is the program that waits, as a run under a debugger that stopped
    // would be, when nothing else runs and somebody takes the stop. Called while the handler waits for the answer.
    private async Task<bool> TellSessionStopAsync(ExecutionStop stop)
    {
        if (provider.SessionStopped is not { } tell)
        {
            return false;
        }

        var session = provider.Session;
        ExecuteResult stopped;
        lock (_sync)
        {
            if (_state is not ProgramState.Idle || _alongside || _sessionEvents is not { } gate)
            {
                return false;
            }

            _execution = gate;
            _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
            (_terminating, _debugging, _stopWhenStarted, _streaming) = (false, true, false, false);
            _output = new RuntimeOutputBuffer();
            HeldDefinitions.Hold(session);
            stopped = Segment(session, stop);
        }

        bool taken;
        try
        {
            taken = await tell(stopped);
        }
        catch (Exception exception)
        {
            logger?.LogError(exception, "❌ A handler that stopped with no program running could not be told.");
            taken = false;
        }

        if (!taken)
        {
            lock (_sync)
            {
                if (_state is ProgramState.Suspended && _execution is not null && ReferenceEquals(_execution, _sessionEvents))
                {
                    _execution = null;
                    _suspendedWith = null;
                    _ = HeldDefinitions.Release(session);
                    Become(ProgramState.Idle);
                    _inspector.Forget();
                    _idle.TrySetResult();
                }
            }
        }

        return taken;
    }

    /// <summary>
    /// Runs program code on a thread of its own, and not on the thread of the request that asked for it.
    /// </summary>
    /// <remarks>
    /// The thread of a request is the thread that reads the connection: a handler is called on it, and a handler that does not return until a program is over keeps
    /// it for as long. A program that waits for an answer over the same connection - a <c>MsgBox</c> the client answers - then waits for an answer nothing reads, and
    /// a <c>$/cancelRequest</c> that would stop it is not read either. Code that runs for as long as a program pleases runs here instead.
    /// <para>
    /// The thread's stack has room for every activation the call stack takes (<see cref="RuntimeCallStack.ProgramThreadStackSize"/>): a thread's default stack
    /// holds fewer than a hundred, and a deeper recursion would be error 28 far sooner than its limit.
    /// </para>
    /// </remarks>
    internal static Task<T> OffTheConnectionAsync<T>(Func<T> run)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = SDK.Platform.LargeStackThread.NewThread(() =>
        {
            try
            {
                done.SetResult(run());
            }
            catch (Exception exception)
            {
                done.SetException(exception);
            }
        }, "RDCore program");
        thread.Start();
        return done.Task;
    }

    // gives the turn back when the program is over. Called with the lock held, or by what ends the program.
    private void LeaveTurn(IRuntimeSession session)
    {
        if (_inTurn)
        {
            _inTurn = false;
            session.Turn.Exit();
        }
    }

    // the output of a stretch of the program under a debugger: said as it is printed when the run asked for that and somebody listens, kept for the answer otherwise.
    private RuntimeOutputBuffer NewSegmentOutput()
        => _streaming && provider.OutputStreamed is { } stream
            ? new RuntimeOutputBuffer(line => stream([line], Interlocked.Increment(ref _streamed)))
            : new RuntimeOutputBuffer();

    /// <summary>
    /// Whether a program is running or waits.
    /// </summary>
    public ProgramState State
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    /// <summary>
    /// Raised each time the program's <see cref="State"/> changes, with the state it is now in: as it starts, as it waits, as it goes on, and as it ends.
    /// </summary>
    /// <remarks>
    /// Raised with the execution's lock held, on the thread that changed the state: a handler says that it happened (it completes a task, or queues a message), and
    /// does not wait for anything, nor ask anything of the execution but its <see cref="State"/>.
    /// </remarks>
    public event Action<ProgramState>? StateChanged;

    // the state changes here and nowhere else, so that every change is told. Called with the lock held.
    private void Become(ProgramState state)
    {
        _state = state;
        StateChanged?.Invoke(state);
    }

    /// <summary>
    /// Runs a program from its entry point.
    /// </summary>
    /// <param name="pipeline">What runs it. Composed for this run: a run that is not under a debugger is cancelled through it.</param>
    /// <param name="entryPoint">The procedure to run.</param>
    /// <param name="debug">Whether the program runs under a debugger.</param>
    /// <param name="token">The cancellation of the request: for a program under a debugger it is a break, and not the end of it.</param>
    /// <param name="immediate">
    /// Whether the entry point is a statement typed at a prompt. While a program waits it is run alongside it, in the session as the program left it, and the program
    /// waits still: that is how its variables are read and set at a stop. Otherwise it is a run like any other.
    /// </param>
    /// <param name="streamOutput">
    /// Whether the program's output is said as it is printed (<see cref="IEnvironmentSessionProvider.OutputStreamed"/>), for the whole of a run under a debugger, and not
    /// kept for the answers. Ignored for a run that is not.
    /// </param>
    /// <param name="unattended">
    /// Whether nobody is there to answer a dialog the program opens, which fails the run (<see cref="IRuntimeSession.RunsUnattended"/>), for the whole of the run.
    /// Ignored for a statement run alongside a program that waits, which runs as that program does.
    /// </param>
    /// <returns>
    /// How it ended, or where it waits. A program that is suspended is let go of, and not refused: running one is what <c>RUN</c> does, and in BASIC it starts the
    /// program over. A program that is running is refused.
    /// </returns>
    public Task<ExecuteResult> RunAsync(
        RuntimeExecutionPipeline pipeline, VBTypeMemberSymbol entryPoint, bool debug, CancellationToken token, bool immediate = false, bool streamOutput = false,
        bool unattended = false)
        => StartAsync(pipeline, entryPoint, debug, token, immediate, streamOutput, unattended, started: null);

    /// <summary>
    /// Runs a program from its entry point to its end as a command line starts one: not under a debugger, with the arguments it was started with, and its output
    /// said as it is printed.
    /// </summary>
    /// <param name="pipeline">What runs it. Composed for this run: the run is cancelled through it.</param>
    /// <param name="entryPoint">The procedure to run.</param>
    /// <param name="arguments">
    /// What the program was started with, which a <c>ParamArray</c> the entry point declares collects
    /// (<see cref="RuntimeProcedureInvoker.ProgramArguments(VBTypeMemberSymbol, IReadOnlyList{string})"/>).
    /// </param>
    /// <param name="printed">Receives each line the program prints, as it is completed. A line the program leaves open is the output of the result.</param>
    /// <param name="token">The cancellation of the run.</param>
    /// <param name="unattended">Whether nobody is there to answer a dialog the program opens, which fails the run (<see cref="IRuntimeSession.RunsUnattended"/>).</param>
    /// <returns>How it ended. A program that is suspended is let go of, and one that is running is refused, as they are for any run.</returns>
    public Task<ExecuteResult> RunAsync(
        RuntimeExecutionPipeline pipeline, VBTypeMemberSymbol entryPoint, IReadOnlyList<string> arguments, Action<string> printed, CancellationToken token,
        bool unattended = false)
        => StartAsync(pipeline, entryPoint, debug: false, token, immediate: false, streamOutput: false, unattended, new ProgramStart(arguments, printed));

    // what a command line starts a program with, and where it says what the program prints.
    private sealed record class ProgramStart(IReadOnlyList<string> Arguments, Action<string> Printed);

    private Task<ExecuteResult> StartAsync(
        RuntimeExecutionPipeline pipeline, VBTypeMemberSymbol entryPoint, bool debug, CancellationToken token, bool immediate, bool streamOutput, bool unattended,
        ProgramStart? started)
    {
        var session = provider.Session;
        lock (_sync)
        {
            if (_state is ProgramState.Running || _alongside)
            {
                return Task.FromResult(Refused(Resources.Host_AProgramIsRunning));
            }

            if (_state is ProgramState.Suspended && immediate)
            {
                _alongside = true;
                return OffTheConnectionAsync(() => RunAlongside(session, pipeline, entryPoint, token));
            }

            if (_state is ProgramState.Suspended)
            {
                EndSuspended(session, wipe: false);
            }

            CloseToSessionStops(session);
            Become(ProgramState.Running);
            _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _terminating = false;
            (_debugging, _stopWhenStarted) = (debug, false);
            _streaming = debug && streamOutput;
            session.RunsUnattended = unattended;
        }

        // the program is in its turn for as long as it runs or waits at a Stop: an event that something outside the workspace raises is handled between two programs and not
        // in the middle of one (ISessionTurn). It waits here for the handlers of an event that is being handled, which are short.
        session.Turn.Enter();
        lock (_sync)
        {
            _inTurn = true;
        }

        // a program starts as one that was never stopped, whatever the one before it did: what a Stop left of its activations is let go of.
        session.Halt.Clear();
        SessionWipe.Abandon(session);

        // an edit made while it runs or waits takes no value from it (HeldDefinitions).
        HeldDefinitions.Hold(session);

        return debug ? RunUnderDebuggerAsync(session, pipeline, entryPoint, token) : OffTheConnectionAsync(() => RunToTheEnd(session, pipeline, entryPoint, token, started));
    }

    /// <summary>
    /// Writes the session's memory where no program runs in it: at a stop, or with no program. A program that runs owns its memory, and a byte written into a variable
    /// while the program writes it could leave the variable part the one value and part the other.
    /// </summary>
    /// <param name="write">What writes it. Called with the lock held: nothing resumes, starts or runs alongside the program until it is done.</param>
    /// <param name="refusal">Why nothing was written, when a program runs; <see langword="null"/> otherwise.</param>
    /// <returns>What <paramref name="write"/> returned, or <see langword="false"/> when a program runs.</returns>
    public bool TryWriteMemory(Func<bool> write, out string? refusal)
    {
        lock (_sync)
        {
            if (_state is ProgramState.Running || _alongside)
            {
                refusal = Resources.Host_TheProgramIsRunning;
                return false;
            }

            refusal = null;
            return write();
        }
    }

    /// <summary>
    /// Goes on with the program that waits, to the next place it waits or to its end.
    /// </summary>
    /// <param name="step">How far it goes, or <see langword="null"/> for as far as it goes.</param>
    /// <param name="token">The cancellation of the request, which is a break.</param>
    /// <returns>Where the program is by then, or <see cref="ExecutionOutcome.Refused"/> when it cannot be resumed.</returns>
    public async Task<ExecuteResult> ResumeAsync(StepKind? step, CancellationToken token)
    {
        var session = provider.Session;
        SuspendableExecution execution;
        lock (_sync)
        {
            if (_state is not ProgramState.Suspended || _alongside || _execution is not { } suspended)
            {
                return Refused(_alongside ? Resources.Host_TheProgramIsRunning : NotSuspended(_state));
            }

            if (ChangedCode() is { Length: > 0 } changed)
            {
                return Refused(string.Format(Resources.Host_ChangedWhileWaiting_Resume, string.Join(", ", changed)));
            }

            GoOnInTheEditedCode();
            execution = suspended;
            Become(ProgramState.Running);
            _inspector.Forget();
            _output = NewSegmentOutput();
            provider.Output.Target = _output;
        }

        using var registration = token.Register(() => Pause());
        var stop = await (step is { } kind ? execution.StepAsync(kind) : execution.ResumeAsync());
        return Segment(session, stop);
    }

    /// <summary>
    /// The edits made while the program waits that are not applied to it: what resuming it, stepping it or moving its point of execution is refused for.
    /// </summary>
    /// <returns>None, and not suspended, when no program waits.</returns>
    public DebugEditStatusResult EditStatus()
    {
        lock (_sync)
        {
            if (_state is not ProgramState.Suspended || _alongside || _suspendedWith is null)
            {
                return new DebugEditStatusResult();
            }

            return new DebugEditStatusResult
            {
                Suspended = true,
                Edits = [.. ProgramEdits.Since(_suspendedWith, provider.Session, provider.Image)
                    .Where(edit => edit.Kind is not ProgramEditKind.Applies)
                    .Select(edit => new DebugEdit
                    {
                        Name = edit.Name,
                        Kind = edit.Kind switch
                        {
                            ProgramEditKind.Restart => DebugEditKind.Restart,
                            ProgramEditKind.DoesNotCompile => DebugEditKind.DoesNotCompile,
                            _ => DebugEditKind.NotYet,
                        },
                        Reason = edit.Reason,
                        ModuleUri = edit.Module,
                        Range = edit.Range,
                    })],
            };
        }
    }

    /// <summary>
    /// Moves the point the program that waits goes on from, within the activation it waits in.
    /// </summary>
    /// <param name="line">The zero-based line of the source to go on from. Ignored when <paramref name="label"/> is given.</param>
    /// <param name="label">A statement label or line number of the procedure to go on from.</param>
    /// <param name="moduleName">The module <paramref name="line"/> is a line of, or <see langword="null"/> for the module of the procedure the program waits in.</param>
    /// <param name="peek">Whether nothing is moved, and the answer only says where the program would go on from.</param>
    /// <returns>Where the program goes on from, or why it was not moved.</returns>
    /// <remarks>
    /// A line is a line of the procedure the program waits in, as the session defines it now: the first statement that begins on it or after it is one of that
    /// procedure only when the line is, and a line of another procedure or module would otherwise land on a statement nobody pointed at.
    /// </remarks>
    public DebugGotoResult Goto(int line, string? label, string? moduleName = null, bool peek = false)
    {
        lock (_sync)
        {
            if (_state is not ProgramState.Suspended || _alongside)
            {
                return NotMoved(_alongside ? Resources.Host_TheProgramIsRunning : NotSuspended(_state));
            }

            if (ChangedCode() is { Length: > 0 } changed)
            {
                return NotMoved(string.Format(Resources.Host_ChangedWhileWaiting_Goto, string.Join(", ", changed)));
            }

            if (provider.Session.CallStack.Current is not CallStackFrame { Body: { } waitedIn } frame)
            {
                return NotMoved(Resources.Host_WaitsInNoProcedure);
            }

            // the line to go on from is a line of the code as it is now, which a move goes on in and a peek only looks at.
            var body = waitedIn;
            if (!peek)
            {
                GoOnInTheEditedCode();
                body = frame.Body ?? waitedIn;
            }
            else if (frame.Procedure is { } waiting && provider.Image.TryGetValue(waiting.SemanticId, out var edited))
            {
                body = edited;
            }

            int offset;
            if (label is { Length: > 0 })
            {
                if (!body.TryGetLabelOffset(label, out offset))
                {
                    return NotMoved(string.Format(Resources.Host_NoSuchLabel, label));
                }
            }
            else
            {
                if (frame.Procedure is { } procedure)
                {
                    var module = ProgramInspector.ModuleNameOf(frame);
                    if (moduleName is { Length: > 0 } && !string.Equals(moduleName, module, StringComparison.OrdinalIgnoreCase))
                    {
                        return NotMoved(string.Format(Resources.Host_GotoInAnotherModule, moduleName, module));
                    }

                    var extent = (ProgramEdits.CurrentProcedureOf(provider.Session, frame) ?? procedure).Range;
                    if (line < extent.Start.Line || line > extent.End.Line)
                    {
                        return NotMoved(string.Format(Resources.Host_GotoOutsideTheProcedure, $"{module}.{procedure.Name}"));
                    }
                }

                if (!BreakpointTable.TryGetOffsetAtLine(body, line, out offset))
                {
                    return NotMoved(Resources.Host_NoStatementAtOrAfterLine);
                }
            }

            if (peek)
            {
                return MovedTo(body, offset);
            }

            frame.MoveTo(offset);
            return MovedTo(body, offset);
        }
    }

    private static DebugGotoResult MovedTo(InstructionList body, int offset)
    {
        var location = offset < body.Items.Length ? BreakpointTable.LocationOf(body.Items[offset]) : null;
        return new DebugGotoResult
        {
            Moved = true,
            Line = location?.Range.Start.Line ?? -1,
            Character = location?.Range.Start.Character ?? -1,
        };
    }

    private static DebugGotoResult NotMoved(string reason) => new() { Reason = reason };

    /// <summary>
    /// Sets the lines of a module that a program under a debugger waits at.
    /// </summary>
    /// <param name="moduleName">The programmatic name of the module.</param>
    /// <param name="breakpoints">The breakpoints, one per zero-based line of the source. None removes them.</param>
    /// <returns>Each line, and whether a statement of the module's loaded code begins on it.</returns>
    /// <remarks>
    /// A breakpoint is where an instruction is, and a line no statement begins on is reported as not verified - nothing would ever wait there, and a client is to drop it.
    /// Every line is kept all the same, since the code can be loaded again before the program runs (a shell redefines its program at each <c>RUN</c>), and what is
    /// verified is a fact about the code that is loaded now. Whether it is the code the person means is what <see cref="DebugBreakpointsResult.Judged"/> says.
    /// A disabled breakpoint is judged as any other is, and is not waited at; neither is one whose condition the request did not carry an expression for.
    /// </remarks>
    public DebugBreakpointsResult SetBreakpoints(string moduleName, IReadOnlyList<HostBreakpointLine> breakpoints)
    {
        var session = provider.Session;

        // the module of that name, and not what the name means in code: a class module's name is no value, and a form's is its default instance.
        if (!session.Symbols.TryGetModule(moduleName, out var module))
        {
            return new DebugBreakpointsResult { Breakpoints = [.. breakpoints.Select(breakpoint => new DebugBreakpoint(breakpoint.Line, false))] };
        }

        var refused = new string?[breakpoints.Count];
        var waited = new List<RuntimeBreakpoint>(breakpoints.Count);
        for (var i = 0; i < breakpoints.Count; i++)
        {
            var breakpoint = breakpoints[i];
            if (!TryReadCondition(breakpoint.ConditionJson, out var condition))
            {
                refused[i] = Resources.Host_RequestCarriedNoExpression;
            }
            else if (breakpoint.IsEnabled)
            {
                waited.Add(new RuntimeBreakpoint(breakpoint.Line, condition));
            }
        }

        session.Halt.Breakpoints.Set(module.Uri.AbsoluteUri, waited);
        var bodies = provider.Image.BodiesOf(module.Uri);
        return new DebugBreakpointsResult
        {
            Breakpoints = [.. breakpoints.Select((breakpoint, i) => new DebugBreakpoint(breakpoint.Line, BreakpointTable.Verify(bodies, breakpoint.Line), refused[i]))],
            Judged = bodies.Any(),
        };
    }

    // a breakpoint without a condition has none to read; one with a condition that is not an expression's tree is refused.
    private static bool TryReadCondition(string? json, out ExpressionNode? condition)
    {
        condition = null;
        if (string.IsNullOrEmpty(json))
        {
            return true;
        }

        try
        {
            condition = PlatformJson.Deserialize<ExpressionNode>(json);
            return condition is not null;
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// The activations of the program that waits, innermost first.
    /// </summary>
    /// <returns>None when no program waits.</returns>
    public DebugStackResult Stack()
    {
        lock (_sync)
        {
            return _state is ProgramState.Suspended ? _inspector.Stack() : new DebugStackResult();
        }
    }

    /// <summary>
    /// The variables of an activation of the program that waits, or the parts of one of them.
    /// </summary>
    /// <param name="frameId">The activation, by its place on the stack.</param>
    /// <param name="scope">Which of its variables.</param>
    /// <param name="reference">The reference of a variable that has parts, or <c>0</c>.</param>
    /// <returns>None when no program waits.</returns>
    public DebugVariablesResult Variables(int frameId, DebugVariableScope scope, int reference)
    {
        // reading a variable that holds a server's object is a call to it, beside the program that waits.
        lock (_sync)
        {
            if (_state is not ProgramState.Suspended)
            {
                return new DebugVariablesResult();
            }

            using (provider.Session.Turn.Alongside())
            {
                return _inspector.Variables(frameId, scope, reference);
            }
        }
    }

    /// <summary>
    /// The value of an expression in an activation of the program that waits.
    /// </summary>
    /// <param name="pipeline">What evaluates it.</param>
    /// <param name="frameId">The activation, by its place on the stack.</param>
    /// <param name="expression">The expression.</param>
    /// <remarks>
    /// The expression is evaluated with the activation selected on the call stack: its names are the activation's names, and a call in it
    /// pushes an activation above, as any call does. The program stays where it waits. Something that stops - a <c>Stop</c> in a procedure the expression calls - stops
    /// the expression and not the program, and an <c>End</c> is the end of it.
    /// </remarks>
    public DebugEvaluateResult Evaluate(RuntimeExecutionPipeline pipeline, int frameId, ExpressionNode expression)
        => InFrame(frameId, ofAStatement: false, (session, stack, frame, procedure) =>
        {
            RuntimeSemanticsEvaluationResult result;
            using (stack.Select(frame))
            {
                result = pipeline.Expressions.Evaluate(session, expression, new RuntimeEvaluationContext(procedure.Uri));
            }

            return result.IsSuccess
                ? (result.Result, null)
                : (null, result.IsInternalError ? UnevaluableBecause(session, expression, procedure.Uri) : result.ErrorInfo!.Description);
        });

    /// <summary>
    /// Runs a statement in an activation of the program that waits, as if it were written in the procedure that activation is of.
    /// </summary>
    /// <param name="pipeline">What runs it.</param>
    /// <param name="frameId">The activation, by its place on the stack.</param>
    /// <param name="statement">The statement: an assignment, a call, anything a person could type in the Immediate window of a procedure that is stopped.</param>
    /// <remarks>
    /// Like <see cref="Evaluate"/>, with the activation selected, so that its names are the activation's: an assignment to one of its locals
    /// changes the local the program will go on with. The statement is a body of its own, in an activation of its own that is not on the call stack - the activation
    /// selected is the program's, and where it waits is not for the statement to move.
    /// </remarks>
    public DebugEvaluateResult Execute(RuntimeExecutionPipeline pipeline, int frameId, SyntaxNode statement)
        => InFrame(frameId, ofAStatement: true, (session, stack, selected, procedure) =>
        {
            if (DeclareImplicitly(session, statement, selected, procedure) is { } refused)
            {
                return (null, refused);
            }

            // what is wrong with the statement is found out as it is for the procedure's own code (ModuleLoader), in the procedure's scope - an assignment to a
            // function of a library is not a variable there either - and a statement that is wrong does not run.
            var block = new StatementBlock([statement]);
            if (session.Symbols.ScopeOf(procedure.Uri) is { } scope
                && StatementStaticSemanticsEvaluator.Analyze(procedure.SemanticId, block, new StaticSemanticsOptions(Language: session.Environment.Language), MemberKind.Procedure,
                    new StaticEvaluationContext(session.Symbols.Resolver, scope, Environment: new StaticEnvironment(session.Environment.Is64Bit))).CompileErrors is [var error, ..])
            {
                return (null, string.IsNullOrEmpty(error.Verbose) || error.Verbose == error.Description ? error.Description : $"{error.Description}: {error.Verbose}");
            }

            var options = new InstructionLoweringOptions(IsReleaseBuild: !session.IsDebugBuild(), Language: session.Environment.Language,
                DropsDebugStatements: session.Environment.ReleaseDropsDebugStatements);
            var lowered = InstructionListLowering.Lower(block, options, MemberKind.Procedure);
            var scratch = (CallStackFrame)session.Symbols.CreateFrame(selected.NodeId, selected.StaticSymbol, selected.Directives);
            (scratch.Procedure, scratch.Body) = (procedure, lowered.InstructionList);

            RuntimeExecutionOutcome outcome;
            using (stack.Select(selected))
            {
                outcome = pipeline.Executor.Run(session, scratch, lowered.InstructionList, new RuntimeEvaluationContext(procedure.Uri));
            }

            return outcome.Kind switch
            {
                RuntimeExecutionOutcomeKind.Error => (null, outcome.ErrorInfo!.Description),
                RuntimeExecutionOutcomeKind.InternalError => (null, UnevaluableBecause(session, statement, procedure.Uri, Resources.Host_CannotRunYet)),
                _ => (null, null),
            };
        });

    // What it takes to run something in an activation of the program that waits, whichever the something is: the program waits, the activation is on its stack, what the
    // something prints is its own, and whatever it leaves on the call stack - a Stop in a procedure it calls - is let go of. The program is claimed with the lock held
    // (_alongside), and the something runs with nothing locked, so that a request that asks about the program while it runs - while a dialog it opened waits for its
    // answer - is answered rather than waiting for it. Called on a thread of its own by the handlers (OffTheConnectionAsync).
    private DebugEvaluateResult InFrame(
        int frameId, bool ofAStatement, Func<IRuntimeSession, RuntimeCallStack, CallStackFrame, VBTypeMemberSymbol, (VBTypedValue? Value, string? Failure)> run)
    {
        IRuntimeSession session;
        CallStackFrame selected;
        VBTypeMemberSymbol procedure;
        RuntimeCallStack stack;
        lock (_sync)
        {
            session = provider.Session;
            if (_state is not ProgramState.Suspended || _alongside)
            {
                return Unevaluated(_alongside ? Resources.Host_TheProgramIsRunning : NotSuspended(_state));
            }

            var frames = session.CallStack.Frames.OfType<CallStackFrame>().ToArray();
            if (frameId < 0 || frameId >= frames.Length || frames[frameId].Procedure is not { } activated || session.CallStack is not RuntimeCallStack callStack)
            {
                return Unevaluated(Resources.Host_NoSuchActivation);
            }

            (selected, procedure, stack) = (frames[frameId], activated, callStack);
            _alongside = true;
        }

        var output = new RuntimeOutputBuffer();
        var depth = session.CallStack.Depth;
        provider.Output.Target = output;
        try
        {
            var (value, failure) = RunGuarded(session, stack, selected, procedure, run);

            lock (_sync)
            {
                if (session.Halt.Pending is { } halt)
                {
                    session.Halt.Clear();
                    if (halt is RuntimeHaltKind.End)
                    {
                        EndSuspended(session, wipe: true);
                        return Unevaluated(ofAStatement ? Resources.Host_TheStatementEndedTheProgram : Resources.Host_TheExpressionEndedTheProgram, output) with
                        {
                            EndedTheProgram = true,
                        };
                    }

                    return Unevaluated(ofAStatement ? Resources.Host_TheStatementWasStopped : Resources.Host_TheExpressionWasStopped, output);
                }

                if (failure is not null)
                {
                    return Unevaluated(failure, output);
                }

                if (value is null)
                {
                    // a statement has no value: it is done.
                    return new DebugEvaluateResult { Success = true, Output = output.Lines };
                }

                var described = _inspector.Describe(string.Empty, value);
                return new DebugEvaluateResult
                {
                    Success = true,
                    Value = described.Value,
                    Type = described.Type,
                    Reference = described.Reference,
                    Output = output.Lines,
                };
            }
        }
        finally
        {
            provider.Output.Target = NullRuntimeOutput.Instance;
            UnwindTo(session, depth);
            lock (_sync)
            {
                _alongside = false;
            }
        }
    }

    // a statement or an expression typed at the stop that throws is a failure of the host, not of the program's code: it is said with its stack, the statement is
    // answered with the runtime's own internal error (51), and the program waits on. The request is answered, and never with the exception.
    private (VBTypedValue? Value, string? Failure) RunGuarded(IRuntimeSession session, RuntimeCallStack stack, CallStackFrame selected, VBTypeMemberSymbol procedure,
        Func<IRuntimeSession, RuntimeCallStack, CallStackFrame, VBTypeMemberSymbol, (VBTypedValue? Value, string? Failure)> run)
    {
        try
        {
            // what the code as edited declares, the statement names: the locals an edit added, the fields a class was given.
            ProgramEdits.Prepare(session);

            // the code runs beside the program that waits: the events its calls raise are its own, and none of the program's runs meanwhile.
            using (session.Turn.Alongside())
            {
                return run(session, stack, selected, procedure);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger?.LogError(exception, "❌ A statement typed at the stop in {procedure} failed in the host.", procedure.Name);
            return (null, VBRuntimeErrorInfo.For(VBRuntimeErrorId.InternalError, new SourceLocation(procedure.Uri, procedure.Range), exception.Message).Description);
        }
    }

    // MS-VBAL 5.6.10: a name the procedure does not see, typed where the program waits, declares a variable as it would in the procedure's own code - a Variant
    // that is Empty, so ?x prints an empty line - unless the module says Option Explicit (MS-VBAL 5.2.1.3), where it is not defined and nothing runs. Where the
    // variable lives is the environment's to say (ImplicitDeclarationScope), as for code: in a BASIC shell a variable of the module, which a statement typed at
    // the prompt with no program waiting declares as well; in VBA and VB6 a local of the procedure the activation is of, on that activation. A class member's
    // activation has no module storage to give it, and gives it a local. The name is declared before the statement runs, so that what it reads is a variable.
    private static string? DeclareImplicitly(IRuntimeSession session, SyntaxNode statement, CallStackFrame selected, VBTypeMemberSymbol procedure)
    {
        var resolver = session.Symbols.Resolver;
        var references = SymbolBuilder.ImplicitDeclarationCandidates(statement)
            .DistinctBy(name => name.IdentifierName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // an implicit local declared by a statement typed in another activation of the procedure is one this activation has no storage for yet.
        foreach (var reference in references)
        {
            if (resolver.ResolveValue(reference, ScopeKind.Local, procedure.Uri).Symbol is VBLocalVariableSymbol { DeclaredBy: LocalDeclarationKind.Implicit } local
                && local.ParentUri.AbsoluteUri == procedure.Uri.AbsoluteUri && !selected.TryResolve(local, out _))
            {
                selected.PushDeclaredAtAStop(local, local.ResolvedType.DefaultValue);
            }
        }

        // MS-VBAL 5.4.3.3: a ReDim of a name the procedure does not see declares a dynamic array local to it, as it does in the procedure's own code
        // (SymbolBuilder.BuildRedimLocal), Option Explicit or not: on this activation.
        var builder = new SymbolBuilder(procedure.WorkspaceRoot, procedure.ParentUri, ScopeKind.Module, resolver, VBVariantType.TypeInfo);
        foreach (var redim in Descendants(statement).OfType<RedimDeclarationNode>().DistinctBy(redim => redim.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (redim.Target is not SimpleNameExpressionNode target || !resolver.ResolveValue(target, ScopeKind.Local, procedure.Uri).IsUnbound)
            {
                continue;
            }

            var array = (VBLocalVariableSymbol)builder.BuildRedimLocal(redim, procedure.Uri);
            if (session.Symbols.TryDefine(array, ScopeKind.Local))
            {
                selected.PushDeclaredAtAStop(array, array.ResolvedType.DefaultValue);
            }
        }

        var undeclared = references.Where(name => resolver.ResolveValue(name, ScopeKind.Local, procedure.Uri).IsUnbound).ToArray();
        if (undeclared.Length == 0)
        {
            return null;
        }

        if (selected.Directives.Explicit)
        {
            return string.Format(Resources.Host_VariableNotDefined, undeclared[0].IdentifierName);
        }

        var ofTheModule = session.Environment.Language.ImplicitDeclarationScope is ImplicitDeclarationScope.Module && selected.Target is null;
        foreach (var name in undeclared)
        {
            if (ofTheModule)
            {
                _ = session.Symbols.TryDefine(builder.BuildImplicitModuleVariable(name), ScopeKind.Module);
                continue;
            }

            var local = (VBLocalVariableSymbol)builder.BuildImplicitLocal(name, procedure.Uri);
            if (session.Symbols.TryDefine(local, ScopeKind.Local))
            {
                selected.PushDeclaredAtAStop(local, local.ResolvedType.DefaultValue);
            }
        }

        return null;
    }

    // The evaluator answers an internal error for a name nothing defines, since static semantics were to have rejected it. What a person is told is the name: the first
    // one in the expression - or the statement - that does not resolve from the procedure. One every name of which resolves is one the interpreter cannot run yet.
    private static string UnevaluableBecause(IRuntimeSession session, SyntaxNode node, Uri scope, string? otherwise = null)
    {
        var undefined = Descendants(node)
            .OfType<SimpleNameExpressionNode>()
            .FirstOrDefault(name => !session.Symbols.Resolver.ResolveValue(name, ScopeKind.Local, scope).IsResolved);

        return undefined is null ? otherwise ?? Resources.Host_CannotEvaluateYet : string.Format(Resources.Host_NameIsNotDefined, undefined.IdentifierName);
    }

    // why nothing can be asked of the program that does not wait.
    private static string NotSuspended(ProgramState state) => state is ProgramState.Running ? Resources.Host_TheProgramIsRunning : Resources.Host_NoProgramIsSuspended;

    private static IEnumerable<SyntaxNode> Descendants(SyntaxNode node) => node.Children.SelectMany(Descendants).Prepend(node);

    private static DebugEvaluateResult Unevaluated(string reason, RuntimeOutputBuffer? output = null)
        => new() { Error = reason, Output = output?.Lines ?? [] };

    /// <summary>
    /// Stops the program that is running, at the next instruction, so that it waits there.
    /// </summary>
    /// <returns>Whether there was a program under a debugger running.</returns>
    public bool Pause()
    {
        lock (_sync)
        {
            if (_state is not ProgramState.Running || !_debugging)
            {
                return false;
            }

            StopAtTheNextInstruction();
            return true;
        }
    }

    /// <summary>
    /// Ends the program that is running or waits.
    /// </summary>
    /// <param name="wipe">Whether the session is wiped, as an <c>End</c> wipes it, or keeps what the program made of it.</param>
    /// <param name="token">
    /// Stops the wait for a program that is running to stop: one in a call outside it - a <c>Declare</c>, an object, a dialog - stops when the call returns, and the
    /// end that was asked of it stands meanwhile.
    /// </param>
    /// <returns>Whether there was a program to end, and whether it had not stopped yet when the wait was stopped.</returns>
    public async Task<DebugAck> TerminateAsync(bool wipe, CancellationToken token = default)
    {
        Task idle;
        lock (_sync)
        {
            switch (_state)
            {
                case ProgramState.Idle:
                    return new DebugAck();

                // what runs alongside the program is let finish: it is short, and it is what the program would be ended in the middle of.
                case ProgramState.Suspended when _alongside:
                    return new DebugAck();

                case ProgramState.Suspended:
                    EndSuspended(provider.Session, wipe);
                    return new DebugAck { Acted = true };

                default:
                    // a program that is not under a debugger has nothing to stop it at: it ends when it ends, or when its request is cancelled.
                    if (!_debugging)
                    {
                        return new DebugAck();
                    }

                    // a program that is running is stopped first, and ended by the request that runs it when it waits.
                    (_terminating, _wipeOnTerminate) = (true, wipe);
                    idle = _idle.Task;
                    StopAtTheNextInstruction();
                    break;
            }
        }

        try
        {
            await idle.WaitAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // the end stands: the program ends at its next instruction, once the call it is in returns.
            return new DebugAck { Acted = true, Stopping = true };
        }

        return new DebugAck { Acted = true };
    }

    // a step into is due at the very next instruction, whichever procedure it is in: that is a pause. A program that was started and is not executing yet - it
    // waits for its turn, or is being set up - would lose a step asked of it now (a run starts as one that was never stopped): it is asked of it as it starts.
    // Called with the lock held.
    private void StopAtTheNextInstruction()
    {
        if (_execution is null)
        {
            _stopWhenStarted = true;
            return;
        }

        provider.Session.Halt.RequestStep(StepKind.Into, callDepth: 0);
    }

    // ---- a statement typed while the program waits ----

    // Run above the activations of the program that waits, which are the session's and stay as they are. It cannot wait at a Stop of its own (the gate holds only
    // the thread of the program), so it is stopped by one, as a run that is not under a debugger is. An End in it is the end of the program: the one that waits is
    // ended with the session wiped, which is what End does. Called with _alongside set and nothing locked, on a thread of its own (OffTheConnectionAsync): nothing else
    // resumes or ends the program meanwhile, and a request that asks whether one could is answered while it runs - a dialog it opens included.
    private ExecuteResult RunAlongside(IRuntimeSession session, RuntimeExecutionPipeline pipeline, VBTypeMemberSymbol entryPoint, CancellationToken token)
    {
        var output = new RuntimeOutputBuffer();
        var depth = session.CallStack.Depth;
        provider.Output.Target = output;
        try
        {
            var result = Report(pipeline.Invoker.Invoke(entryPoint, session.Symbols.Resolver, []), session, output, token);
            if (result.Outcome is ExecutionOutcome.Halted)
            {
                lock (_sync)
                {
                    EndSuspended(session, wipe: true);
                }
            }

            return result;
        }
        finally
        {
            provider.Output.Target = NullRuntimeOutput.Instance;
            UnwindTo(session, depth);
            lock (_sync)
            {
                _alongside = false;
            }
        }
    }

    // A statement or an expression that a Stop stopped leaves the activations it pushed on the stack, as a program that is stopped does, to be looked at. It is not a
    // program that waits, so they are let go of: the stack is the program's, and is what it was.
    private static void UnwindTo(IRuntimeSession session, int depth)
    {
        while (session.CallStack.Depth > depth && session.CallStack.TryPop(out _))
        {
        }
    }

    // ---- a run that is not under a debugger ----

    private ExecuteResult RunToTheEnd(IRuntimeSession session, RuntimeExecutionPipeline pipeline, VBTypeMemberSymbol entryPoint, CancellationToken token, ProgramStart? started)
    {
        // the session is this host's own and outlives the run; the output buffer and the cancellation
        // are this run's, so the pipeline is composed per run and the output routed for its duration.
        var output = started is null ? new RuntimeOutputBuffer() : new RuntimeOutputBuffer(started.Printed);
        provider.Output.Target = output;
        try
        {
            var arguments = started is null
                ? RuntimeProcedureInvoker.ProgramArguments(entryPoint)
                : RuntimeProcedureInvoker.ProgramArguments(entryPoint, started.Arguments);
            return Report(pipeline.Invoker.Invoke(entryPoint, session.Symbols.Resolver, arguments), session, output, token);
        }
        finally
        {
            Finished(session);
        }
    }

    // ---- a run under a debugger ----

    private async Task<ExecuteResult> RunUnderDebuggerAsync(
        IRuntimeSession session, RuntimeExecutionPipeline pipeline, VBTypeMemberSymbol entryPoint, CancellationToken token)
    {
        SuspendableExecution execution;
        lock (_sync)
        {
            execution = _execution = new SuspendableExecution(session);
            _output = NewSegmentOutput();
            provider.Output.Target = _output;
            if (_stopWhenStarted)
            {
                _stopWhenStarted = false;
                session.Halt.RequestStep(StepKind.Into, callDepth: 0);
            }
        }

        try
        {
            using var registration = token.Register(() => Pause());
            var stop = await execution.StartAsync(() => pipeline.Invoker.Invoke(entryPoint, session.Symbols.Resolver, RuntimeProcedureInvoker.ProgramArguments(entryPoint)));
            return Segment(session, stop);
        }
        catch
        {
            lock (_sync)
            {
                _execution = null;
            }

            Finished(session);
            throw;
        }
    }

    // where the program is, as the request that ran it up to here answers - and how many lines it has been said, so that the receiver knows when it has them all.
    private ExecuteResult Segment(IRuntimeSession session, ExecutionStop stop)
        => SegmentCore(session, stop) with { StreamedLines = Interlocked.Read(ref _streamed) };

    private ExecuteResult SegmentCore(IRuntimeSession session, ExecutionStop stop)
    {
        var output = _output;
        if (!stop.IsSuspended)
        {
            try
            {
                return Report(stop.Result!.Value, session, output, CancellationToken.None);
            }
            finally
            {
                lock (_sync)
                {
                    _execution = null;
                }

                Finished(session);
            }
        }

        lock (_sync)
        {
            if (_terminating)
            {
                EndSuspended(session, _wipeOnTerminate);
                return new ExecuteResult { Outcome = ExecutionOutcome.Halted, Output = output.Lines };
            }

            Become(ProgramState.Suspended);
            _suspendedWith = ProgramEdits.Of(session, provider.Image);
            provider.Output.Target = NullRuntimeOutput.Instance;
        }

        // a program that waits where an error was raised says which: the program is asked why it waits by what it has of the error.
        var error = session.Halt.StoppedOnError;
        return new ExecuteResult
        {
            Outcome = ExecutionOutcome.Suspended,
            Output = output.Lines,
            ErrorLine = stop.Location?.Range.Start.Line ?? -1,
            ErrorCharacter = stop.Location?.Range.Start.Character ?? -1,
            ErrorModule = session.CallStack.Current is CallStackFrame waitsIn ? ProgramInspector.ModuleNameOf(waitsIn) : string.Empty,
            ErrorNumber = error?.ErrorId ?? 0,
            ErrorMessage = error?.Description ?? string.Empty,
            ErrorCode = error?.ToDiagnosticCode() ?? string.Empty,
            ErrorTitle = error is null ? string.Empty : error.AsErrorInfo.ToDiagnosticTitle(),
            ConditionError = session.Halt.StoppedOnCondition is { } failure
                ? failure.Error?.Description ?? UnevaluableBecause(session, failure.Condition, failure.Scope)
                : string.Empty,
        };
    }

    /// <summary>
    /// Says which run-time errors a program that runs under a debugger waits at, where they are raised.
    /// </summary>
    /// <param name="mode">Which. The session's, and it is still so when the program is started again.</param>
    public void SetErrorBreak(ErrorBreakMode mode) => provider.Session.Halt.BreakOnErrors = mode;

    // ends the program that waits. Called with the lock held.
    private void EndSuspended(IRuntimeSession session, bool wipe)
    {
        _execution?.Abandon();
        _execution = null;
        if (wipe)
        {
            SessionWipe.End(session);
        }

        Become(ProgramState.Idle);
        _inspector.Forget();
        provider.Output.Target = NullRuntimeOutput.Instance;
        session.Memory.Reclaim();
        _ = HeldDefinitions.Release(session);
        LeaveTurn(session);
        OpenToSessionStops();
        _idle.TrySetResult();
    }

    private void Finished(IRuntimeSession session)
    {
        provider.Output.Target = NullRuntimeOutput.Instance;

        // what the program held for as long as it ran is released, and was freed in the order it was allocated in: the free memory at the end of
        // the space is unused again, and what is free afterwards is fragmentation - a hole with something allocated after it.
        session.Memory.Reclaim();

        // what an edit made while the program ran was held from it is applied now that it is over.
        _ = HeldDefinitions.Release(session);

        lock (_sync)
        {
            Become(ProgramState.Idle);
            _inspector.Forget();
            LeaveTurn(session);
            OpenToSessionStops();
            _idle.TrySetResult();
        }
    }

    // what was edited since the program was suspended that is not applied to it, each with why, in the user's words. What is
    // applied - the code of a procedure the program is not in, a declaration added, a value read afresh where it is next used - is of no matter here: the
    // analysis pass already loaded it, and the program reads it as it goes on.
    private string[] ChangedCode()
        => _suspendedWith is null
            ? []
            : [.. ProgramEdits.Since(_suspendedWith, provider.Session, provider.Image)
                .Where(edit => edit.Kind is not ProgramEditKind.Applies)
                .Select(edit => $"{edit.Name} ({edit.Reason})")];

    // the procedures the program is in go on in their code as it was edited, from where they are; what was edited is then what the program waits with.
    private void GoOnInTheEditedCode()
    {
        if (_suspendedWith is null)
        {
            return;
        }

        ProgramEdits.Rebase(provider.Session, provider.Image);
        _suspendedWith = ProgramEdits.Of(provider.Session, provider.Image);
    }

    private static ExecuteResult Refused(string reason) => new() { Outcome = ExecutionOutcome.Refused, ErrorMessage = reason };

    private static TaskCompletionSource Settled()
    {
        var settled = new TaskCompletionSource();
        settled.SetResult();
        return settled;
    }

    // What the invoker answered, as the caller sees it. An internal error means the interpreter met
    // something it has no implementation for - unless the run was cancelled, in which case that is
    // exactly what an interrupted run looks like from here.
    private ExecuteResult Report(
        RuntimeSemanticsEvaluationResult invocation, IRuntimeSession session, RuntimeOutputBuffer output, CancellationToken token)
    {
        // a program that was stopped is not one that failed: an End is over and leaves nothing of it, and a Stop - or a break asked for from outside, which is what
        // cancelling the request is - leaves the session as the program made it, with the place it stopped at.
        if (session.Halt.Pending is { } halt)
        {
            var location = session.Halt.Location;
            session.Halt.Clear();

            if (halt is RuntimeHaltKind.End)
            {
                SessionWipe.End(session);
                return new ExecuteResult { Outcome = ExecutionOutcome.Halted, Output = output.Lines };
            }

            return new ExecuteResult
            {
                Outcome = ExecutionOutcome.Interrupted,
                Output = output.Lines,
                ErrorMessage = Resources.Host_TheProgramWasInterrupted,
                ErrorLine = location?.Range.Start.Line ?? -1,
                ErrorCharacter = location?.Range.Start.Character ?? -1,
            };
        }

        if (invocation.IsSuccess)
        {
            return new ExecuteResult { Outcome = ExecutionOutcome.Completed, Output = output.Lines };
        }

        if (invocation.IsInternalError)
        {
            return new ExecuteResult
            {
                Outcome = token.IsCancellationRequested ? ExecutionOutcome.Interrupted : ExecutionOutcome.NotImplemented,
                Output = output.Lines,
                ErrorMessage = token.IsCancellationRequested
                    ? Resources.Host_TheProgramWasInterrupted
                    : Resources.Host_CannotRunYet,
            };
        }

        // the error itself says what and where; the session's own error state says the rest, because that is
        // where Err lives - its Source, and the stack trace captured when the error was raised.
        var error = invocation.ErrorInfo!;
        return new ExecuteResult
        {
            Outcome = ExecutionOutcome.RuntimeError,
            Output = output.Lines,
            ErrorNumber = error.ErrorId,
            ErrorMessage = error.Description,
            ErrorCode = error.ToDiagnosticCode(),
            ErrorTitle = error.AsErrorInfo.ToDiagnosticTitle(),
            ErrorLineNumber = session.Errors.LineNumber,
            // MS-VBAL 6.1.3.2.2.6: unspecified, Source is the current project name - which the session
            // does not know and this does.
            ErrorSource = session.Errors.Source is { Length: > 0 } source ? source : provider.ProjectName,
            ErrorLine = error.Location.Range.Start.Line,
            ErrorCharacter = error.Location.Range.Start.Character,
            // the error was raised in the innermost activation of the trace, which was captured where it was raised.
            ErrorModule = session.Errors.StackTrace.Frames is [var raisedIn, ..] ? raisedIn.Module : string.Empty,
            StackTrace =
            [
                .. session.Errors.StackTrace.Frames.Select(frame => new ExecuteStackFrame(
                    frame.ProcedureName,
                    frame.Location?.Range.Start.Line ?? -1,
                    frame.Location?.Range.Start.Character ?? -1,
                    frame.GoSubs.IsDefaultOrEmpty ? null : [.. frame.GoSubs.Select(gosub => gosub.Range.Start.Line)])
                {
                    Module = frame.Module,
                }),
            ],
            ErrorDetails = error.AsErrorInfo.Details,
        };
    }
}
