using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Collections.Immutable;

namespace RDCore.CLI.Host;

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
public sealed class ProgramExecution(IEnvironmentSessionProvider provider)
{
    private readonly Lock _sync = new();
    private ProgramState _state;
    private SuspendableExecution? _execution;
    private RuntimeOutputBuffer _output = new();
    private ImmutableDictionary<string, string> _suspendedWith = ImmutableDictionary<string, string>.Empty;
    private bool _terminating;
    private bool _wipeOnTerminate;
    private TaskCompletionSource _idle = Settled();
    private readonly ProgramInspector _inspector = new(provider);
    private bool _streaming;
    private long _streamed;

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
    /// <returns>
    /// How it ended, or where it waits. A program that is suspended is let go of, and not refused: running one is what <c>RUN</c> does, and in BASIC it starts the
    /// program over. A program that is running is refused.
    /// </returns>
    public Task<ExecuteSessionResult> RunAsync(
        RuntimeExecutionPipeline pipeline, VBTypeMemberSymbol entryPoint, bool debug, CancellationToken token, bool immediate = false, bool streamOutput = false)
    {
        var session = provider.Session;
        lock (_sync)
        {
            if (_state is ProgramState.Running)
            {
                return Task.FromResult(Refused(Resources.Host_AProgramIsRunning));
            }

            if (_state is ProgramState.Suspended && immediate)
            {
                return Task.FromResult(RunAlongside(session, pipeline, entryPoint, token));
            }

            if (_state is ProgramState.Suspended)
            {
                EndSuspended(session, wipe: false);
            }

            _state = ProgramState.Running;
            _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _terminating = false;
            _streaming = debug && streamOutput;
        }

        // a program starts as one that was never stopped, whatever the one before it did: what a Stop left of its activations is let go of.
        session.Halt.Clear();
        SessionWipe.Abandon(session);

        return debug ? RunUnderDebuggerAsync(session, pipeline, entryPoint, token) : Task.FromResult(RunToTheEnd(session, pipeline, entryPoint, token));
    }

    /// <summary>
    /// Goes on with the program that waits, to the next place it waits or to its end.
    /// </summary>
    /// <param name="step">How far it goes, or <see langword="null"/> for as far as it goes.</param>
    /// <param name="token">The cancellation of the request, which is a break.</param>
    /// <returns>Where the program is by then, or <see cref="ExecutionOutcome.Refused"/> when it cannot be resumed.</returns>
    public async Task<ExecuteSessionResult> ResumeAsync(StepKind? step, CancellationToken token)
    {
        var session = provider.Session;
        SuspendableExecution execution;
        lock (_sync)
        {
            if (_state is not ProgramState.Suspended || _execution is not { } suspended)
            {
                return Refused(NotSuspended(_state));
            }

            if (ChangedCode() is { Length: > 0 } changed)
            {
                return Refused(string.Format(Resources.Host_ChangedWhileWaiting_Resume, string.Join(", ", changed)));
            }

            (execution, _state) = (suspended, ProgramState.Running);
            _inspector.Forget();
            _output = NewSegmentOutput();
            provider.Output.Target = _output;
        }

        using var registration = token.Register(() => Pause());
        var stop = await (step is { } kind ? execution.StepAsync(kind) : execution.ResumeAsync());
        return Segment(session, stop);
    }

    /// <summary>
    /// Moves the point the program that waits goes on from, within the activation it waits in.
    /// </summary>
    /// <param name="line">The zero-based line of the source to go on from. Ignored when <paramref name="label"/> is given.</param>
    /// <param name="label">A statement label or line number of the procedure to go on from.</param>
    /// <returns>Where the program goes on from, or why it was not moved.</returns>
    public HostDebugGotoResult Goto(int line, string? label)
    {
        lock (_sync)
        {
            if (_state is not ProgramState.Suspended)
            {
                return NotMoved(NotSuspended(_state));
            }

            if (ChangedCode() is { Length: > 0 } changed)
            {
                return NotMoved(string.Format(Resources.Host_ChangedWhileWaiting_Goto, string.Join(", ", changed)));
            }

            if (provider.Session.CallStack.Current is not CallStackFrame { Body: { } body } frame)
            {
                return NotMoved(Resources.Host_WaitsInNoProcedure);
            }

            int offset;
            if (label is { Length: > 0 })
            {
                if (!body.TryGetLabelOffset(label, out offset))
                {
                    return NotMoved(string.Format(Resources.Host_NoSuchLabel, label));
                }
            }
            else if (!body.TryGetOffsetAtLine(line, out offset))
            {
                return NotMoved(Resources.Host_NoStatementAtOrAfterLine);
            }

            frame.MoveTo(offset);
            var location = offset < body.Items.Length ? body.Items[offset].Node?.SourceLocation : null;
            return new HostDebugGotoResult
            {
                Moved = true,
                Line = location?.Range.Start.Line ?? -1,
                Character = location?.Range.Start.Character ?? -1,
            };
        }
    }

    private static HostDebugGotoResult NotMoved(string reason) => new() { Reason = reason };

    /// <summary>
    /// Sets the lines of a module that a program under a debugger waits at.
    /// </summary>
    /// <param name="moduleName">The programmatic name of the module.</param>
    /// <param name="lines">The zero-based lines of the source. None removes them.</param>
    /// <returns>Each line, and whether a statement of the module's loaded code begins on it.</returns>
    /// <remarks>
    /// A breakpoint is where an instruction is, and a line no statement begins on is reported as not verified - nothing would ever wait there, and a client is to drop it.
    /// Every line is kept all the same, since the code can be loaded again before the program runs (a shell redefines its program at each <c>RUN</c>), and what is
    /// verified is a fact about the code that is loaded now. Whether it is the code the person means is what <see cref="HostDebugBreakpointsResult.Judged"/> says.
    /// </remarks>
    public HostDebugBreakpointsResult SetBreakpoints(string moduleName, IReadOnlyList<int> lines)
    {
        var session = provider.Session;
        if (!session.Symbols.TryResolveValue(moduleName, GlobalSymbols.UnresolvedSymbol, out var module) || module is null)
        {
            return new HostDebugBreakpointsResult { Breakpoints = [.. lines.Select(line => new HostBreakpoint(line, false))] };
        }

        session.Halt.Breakpoints.Set(module.Uri.AbsoluteUri, lines);
        var bodies = provider.Image.BodiesOf(module.Uri);
        return new HostDebugBreakpointsResult
        {
            Breakpoints = [.. lines.Select(line => new HostBreakpoint(line, IBreakpointTable.Verify(bodies, line)))],
            Judged = bodies.Any(),
        };
    }

    /// <summary>
    /// The activations of the program that waits, innermost first.
    /// </summary>
    /// <returns>None when no program waits.</returns>
    public HostDebugStackResult Stack()
    {
        lock (_sync)
        {
            return _state is ProgramState.Suspended ? _inspector.Stack() : new HostDebugStackResult();
        }
    }

    /// <summary>
    /// The variables of an activation of the program that waits, or the parts of one of them.
    /// </summary>
    /// <param name="frameId">The activation, by its place on the stack.</param>
    /// <param name="scope">Which of its variables.</param>
    /// <param name="reference">The reference of a variable that has parts, or <c>0</c>.</param>
    /// <returns>None when no program waits.</returns>
    public HostDebugVariablesResult Variables(int frameId, HostVariableScope scope, int reference)
    {
        lock (_sync)
        {
            return _state is ProgramState.Suspended ? _inspector.Variables(frameId, scope, reference) : new HostDebugVariablesResult();
        }
    }

    /// <summary>
    /// The value of an expression in an activation of the program that waits.
    /// </summary>
    /// <param name="pipeline">What evaluates it.</param>
    /// <param name="frameId">The activation, by its place on the stack.</param>
    /// <param name="expression">The expression.</param>
    /// <remarks>
    /// The expression is evaluated on the thread of the request, with the activation selected on the call stack: its names are the activation's names, and a call in it
    /// pushes an activation above, as any call does. The program stays where it waits. Something that stops - a <c>Stop</c> in a procedure the expression calls - stops
    /// the expression and not the program, and an <c>End</c> is the end of it.
    /// </remarks>
    public HostDebugEvaluateResult Evaluate(RuntimeExecutionPipeline pipeline, int frameId, ExpressionNode expression)
    {
        lock (_sync)
        {
            var session = provider.Session;
            if (_state is not ProgramState.Suspended)
            {
                return Unevaluated(NotSuspended(_state));
            }

            var frames = session.CallStack.Frames.OfType<CallStackFrame>().ToArray();
            if (frameId < 0 || frameId >= frames.Length || frames[frameId].Procedure is not { } procedure || session.CallStack is not RuntimeCallStack stack)
            {
                return Unevaluated(Resources.Host_NoSuchActivation);
            }

            var output = new RuntimeOutputBuffer();
            var depth = session.CallStack.Depth;
            provider.Output.Target = output;
            try
            {
                RuntimeSemanticsEvaluationResult result;
                using (stack.Select(frames[frameId]))
                {
                    result = pipeline.Expressions.Evaluate(session, expression, new RuntimeEvaluationContext(procedure.Uri));
                }

                if (session.Halt.Pending is { } halt)
                {
                    session.Halt.Clear();
                    if (halt is RuntimeHaltKind.End)
                    {
                        EndSuspended(session, wipe: true);
                        return Unevaluated(Resources.Host_TheExpressionEndedTheProgram, output);
                    }

                    return Unevaluated(Resources.Host_TheExpressionWasStopped, output);
                }

                if (result.IsSuccess)
                {
                    var described = _inspector.Describe(string.Empty, result.Result);
                    return new HostDebugEvaluateResult
                    {
                        Success = true,
                        Value = described.Value,
                        Type = described.Type,
                        Reference = described.Reference,
                        Output = output.Lines,
                    };
                }

                return Unevaluated(
                    result.IsInternalError ? UnevaluableBecause(session, expression, procedure.Uri) : result.ErrorInfo!.Description,
                    output);
            }
            finally
            {
                provider.Output.Target = NullRuntimeOutput.Instance;
                UnwindTo(session, depth);
            }
        }
    }

    // The evaluator answers an internal error for a name nothing defines, since static semantics were to have rejected it. What a person is told is the name: the first
    // one in the expression that does not resolve from the procedure. An expression every name of which resolves is one the interpreter cannot evaluate.
    private static string UnevaluableBecause(IRuntimeSession session, ExpressionNode expression, Uri scope)
    {
        var undefined = Descendants(expression)
            .OfType<SimpleNameExpressionNode>()
            .FirstOrDefault(name => !session.Symbols.Resolver.ResolveValue(name, ScopeKind.Local, scope).IsResolved);

        return undefined is null ? Resources.Host_CannotEvaluateYet : string.Format(Resources.Host_NameIsNotDefined, undefined.IdentifierName);
    }

    // why nothing can be asked of the program that does not wait.
    private static string NotSuspended(ProgramState state) => state is ProgramState.Running ? Resources.Host_TheProgramIsRunning : Resources.Host_NoProgramIsSuspended;

    private static IEnumerable<SyntaxNode> Descendants(SyntaxNode node) => node.Children.SelectMany(Descendants).Prepend(node);

    private static HostDebugEvaluateResult Unevaluated(string reason, RuntimeOutputBuffer? output = null)
        => new() { Error = reason, Output = output?.Lines ?? [] };

    /// <summary>
    /// Stops the program that is running, at the next instruction, so that it waits there.
    /// </summary>
    /// <returns>Whether there was a program under a debugger running.</returns>
    public bool Pause()
    {
        lock (_sync)
        {
            if (_state is not ProgramState.Running || _execution is null)
            {
                return false;
            }

            // a step into is due at the very next instruction, whichever procedure it is in: that is a pause.
            provider.Session.Halt.RequestStep(StepKind.Into, callDepth: 0);
            return true;
        }
    }

    /// <summary>
    /// Ends the program that is running or waits.
    /// </summary>
    /// <param name="wipe">Whether the session is wiped, as an <c>End</c> wipes it, or keeps what the program made of it.</param>
    /// <returns>Whether there was a program to end.</returns>
    public async Task<bool> TerminateAsync(bool wipe)
    {
        Task idle;
        lock (_sync)
        {
            switch (_state)
            {
                case ProgramState.Idle:
                    return false;

                case ProgramState.Suspended:
                    EndSuspended(provider.Session, wipe);
                    return true;

                default:
                    // a program that is not under a debugger has nothing to stop it at: it ends when it ends, or when its request is cancelled.
                    if (_execution is null)
                    {
                        return false;
                    }

                    // a program that is running is stopped first, and ended by the request that runs it when it waits.
                    (_terminating, _wipeOnTerminate) = (true, wipe);
                    idle = _idle.Task;
                    provider.Session.Halt.RequestStep(StepKind.Into, callDepth: 0);
                    break;
            }
        }

        await idle;
        return true;
    }

    // ---- a statement typed while the program waits ----

    // Run on the thread of the request, above the activations of the program that waits, which are the session's and stay as they are. It cannot wait at a Stop
    // of its own (the gate holds only the thread of the program), so it is stopped by one, as a run that is not under a debugger is. An End in it is the end of the
    // program: the one that waits is ended with the session wiped, which is what End does. Called with the lock held, and so nothing else resumes or ends the
    // program meanwhile.
    private ExecuteSessionResult RunAlongside(IRuntimeSession session, RuntimeExecutionPipeline pipeline, VBTypeMemberSymbol entryPoint, CancellationToken token)
    {
        var output = new RuntimeOutputBuffer();
        var depth = session.CallStack.Depth;
        provider.Output.Target = output;
        try
        {
            var result = Report(pipeline.Invoker.Invoke(entryPoint, session.Symbols.Resolver, []), session, output, token);
            if (result.Outcome is ExecutionOutcome.Halted)
            {
                EndSuspended(session, wipe: true);
            }

            return result;
        }
        finally
        {
            provider.Output.Target = NullRuntimeOutput.Instance;
            UnwindTo(session, depth);
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

    private ExecuteSessionResult RunToTheEnd(IRuntimeSession session, RuntimeExecutionPipeline pipeline, VBTypeMemberSymbol entryPoint, CancellationToken token)
    {
        // the session is this host's own and outlives the run; the output buffer and the cancellation
        // are this run's, so the pipeline is composed per run and the output routed for its duration.
        var output = new RuntimeOutputBuffer();
        provider.Output.Target = output;
        try
        {
            return Report(pipeline.Invoker.Invoke(entryPoint, session.Symbols.Resolver, []), session, output, token);
        }
        finally
        {
            Finished(session);
        }
    }

    // ---- a run under a debugger ----

    private async Task<ExecuteSessionResult> RunUnderDebuggerAsync(
        IRuntimeSession session, RuntimeExecutionPipeline pipeline, VBTypeMemberSymbol entryPoint, CancellationToken token)
    {
        SuspendableExecution execution;
        lock (_sync)
        {
            execution = _execution = new SuspendableExecution(session);
            _output = NewSegmentOutput();
            provider.Output.Target = _output;
        }

        try
        {
            using var registration = token.Register(() => Pause());
            var stop = await execution.StartAsync(() => pipeline.Invoker.Invoke(entryPoint, session.Symbols.Resolver, []));
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
    private ExecuteSessionResult Segment(IRuntimeSession session, ExecutionStop stop)
        => SegmentCore(session, stop) with { StreamedLines = Interlocked.Read(ref _streamed) };

    private ExecuteSessionResult SegmentCore(IRuntimeSession session, ExecutionStop stop)
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
                return new ExecuteSessionResult { Outcome = ExecutionOutcome.Halted, Output = output.Lines };
            }

            _state = ProgramState.Suspended;
            _suspendedWith = provider.Image.ProcedureFingerprints();
            provider.Output.Target = NullRuntimeOutput.Instance;
        }

        return new ExecuteSessionResult
        {
            Outcome = ExecutionOutcome.Suspended,
            Output = output.Lines,
            ErrorLine = stop.Location?.Range.Start.Line ?? -1,
            ErrorCharacter = stop.Location?.Range.Start.Character ?? -1,
        };
    }

    // ends the program that waits. Called with the lock held.
    private void EndSuspended(IRuntimeSession session, bool wipe)
    {
        _execution?.Abandon();
        _execution = null;
        if (wipe)
        {
            SessionWipe.End(session);
        }

        _state = ProgramState.Idle;
        _inspector.Forget();
        provider.Output.Target = NullRuntimeOutput.Instance;
        session.Memory.Reclaim();
        _idle.TrySetResult();
    }

    private void Finished(IRuntimeSession session)
    {
        provider.Output.Target = NullRuntimeOutput.Instance;

        // what the program held for as long as it ran is released, and was freed in the order it was allocated in: the free memory at the end of
        // the space is unused again, and what is free afterwards is fragmentation - a hole with something allocated after it.
        session.Memory.Reclaim();

        lock (_sync)
        {
            _state = ProgramState.Idle;
            _inspector.Forget();
            _idle.TrySetResult();
        }
    }

    // the procedures the program was suspended with whose code is not what it was, by name. A procedure that was loaded since is of no matter to a program that
    // could not have called it - the statement typed at a break is one - and a procedure that is gone is a change.
    private string[] ChangedCode()
    {
        var now = provider.Image.ProcedureFingerprints();
        return
        [
            .. _suspendedWith
                .Where(procedure => !now.TryGetValue(procedure.Key, out var fingerprint) || fingerprint != procedure.Value)
                .Select(procedure => procedure.Key[(procedure.Key.LastIndexOf('#') + 1)..]),
        ];
    }

    private static ExecuteSessionResult Refused(string reason) => new() { Outcome = ExecutionOutcome.Refused, ErrorMessage = reason };

    private static TaskCompletionSource Settled()
    {
        var settled = new TaskCompletionSource();
        settled.SetResult();
        return settled;
    }

    // What the invoker answered, as the caller sees it. An internal error means the interpreter met
    // something it has no implementation for - unless the run was cancelled, in which case that is
    // exactly what an interrupted run looks like from here.
    private ExecuteSessionResult Report(
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
                return new ExecuteSessionResult { Outcome = ExecutionOutcome.Halted, Output = output.Lines };
            }

            return new ExecuteSessionResult
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
            return new ExecuteSessionResult { Outcome = ExecutionOutcome.Completed, Output = output.Lines };
        }

        if (invocation.IsInternalError)
        {
            return new ExecuteSessionResult
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
        return new ExecuteSessionResult
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
            StackTrace =
            [
                .. session.Errors.StackTrace.Frames.Select(frame => new ExecuteStackFrame(
                    frame.ProcedureName,
                    frame.Location?.Range.Start.Line ?? -1,
                    frame.Location?.Range.Start.Character ?? -1)),
            ],
        };
    }
}
