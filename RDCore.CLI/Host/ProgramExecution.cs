using RDCore.Runtime.Execution;
using RDCore.SDK.Model.Errors.Abstract;
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
    /// <returns>
    /// How it ended, or where it waits. A program that is suspended is let go of, and not refused: running one is what <c>RUN</c> does, and in BASIC it starts the
    /// program over. A program that is running is refused.
    /// </returns>
    public Task<ExecuteSessionResult> RunAsync(RuntimeExecutionPipeline pipeline, VBTypeMemberSymbol entryPoint, bool debug, CancellationToken token)
    {
        var session = provider.Session;
        lock (_sync)
        {
            if (_state is ProgramState.Running)
            {
                return Task.FromResult(Refused("a program is running"));
            }

            if (_state is ProgramState.Suspended)
            {
                EndSuspended(session, wipe: false);
            }

            _state = ProgramState.Running;
            _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _terminating = false;
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
                return Refused(_state is ProgramState.Running ? "the program is running" : "no program is suspended");
            }

            if (ChangedModules() is { Length: > 0 } changed)
            {
                return Refused(
                    $"the code of {string.Join(", ", changed)} changed while the program waited, and a program is resumed on the code it was suspended with: run it again to pick the changes up");
            }

            (execution, _state) = (suspended, ProgramState.Running);
            _output = new RuntimeOutputBuffer();
            provider.Output.Target = _output;
        }

        using var registration = token.Register(() => Pause());
        var stop = await (step is { } kind ? execution.StepAsync(kind) : execution.ResumeAsync());
        return Segment(session, stop);
    }

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
            _output = new RuntimeOutputBuffer();
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

    // where the program is, as the request that ran it up to here answers.
    private ExecuteSessionResult Segment(IRuntimeSession session, ExecutionStop stop)
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
            _suspendedWith = provider.Image.Fingerprints();
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
            _idle.TrySetResult();
        }
    }

    // the modules the program was suspended with whose code is not what it was. A module that was loaded since is of no matter to a program that could not have
    // called it, and a module that is gone is a change.
    private string[] ChangedModules()
    {
        var now = provider.Image.Fingerprints();
        return [.. _suspendedWith.Where(module => !now.TryGetValue(module.Key, out var fingerprint) || fingerprint != module.Value).Select(module => module.Key)];
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
                ErrorMessage = "the program was interrupted",
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
                    ? "the program was interrupted"
                    : "the interpreter reached something it cannot run yet",
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
