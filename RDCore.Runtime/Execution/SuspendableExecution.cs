using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Where a program that was run by a <see cref="SuspendableExecution"/> is: waiting at a <c>Stop</c>, or over.
/// </summary>
/// <param name="Result">What the entry point answered when the program is over, or <see langword="null"/> while it waits.</param>
/// <param name="Kind">Why the program waits, or <see langword="null"/> when it is over.</param>
/// <param name="Location">The statement the program waits at, when it is known.</param>
public sealed record class ExecutionStop(RuntimeSemanticsEvaluationResult? Result, RuntimeHaltKind? Kind = null, SourceLocation? Location = null)
{
    /// <summary>Whether the program waits to be resumed.</summary>
    public bool IsSuspended => Result is null;
}

/// <summary>
/// Runs a program so that a <c>Stop</c> suspends it, and it can be resumed (<strong>MS-VBAL 5.4.2.11</strong>: "all variables maintain their state if execution resumes").
/// </summary>
/// <remarks>
/// The program runs on a thread of its own, and a <c>Stop</c> - or a failed <c>Debug.Assert</c>, or a break asked for from outside - holds that thread where it is, between
/// the instruction that stopped and the next one (see <see cref="IExecutionGate"/>). Nothing of the program is unwound, so there is nothing to put back: its call stack is
/// the session's, with the locals of every activation in it, and what the activations that called it were in the middle of is still on the stack of the thread.
/// <para>
/// A program that is not run by this class is stopped by a <c>Stop</c> as it always was: it is unwound, with its activations left on the call stack to be looked at.
/// </para>
/// <para>
/// Only one program runs in a session at a time, and so only one of these is started in it. The session is the program's while it runs, and is not touched while the
/// program waits except to look at it: the variables, the call stack and the files are as the program left them.
/// </para>
/// </remarks>
/// <param name="session">The session the program runs in.</param>
public sealed class SuspendableExecution(IRuntimeSession session) : IExecutionGate, IDisposable
{
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _decided = new(0);
    private TaskCompletionSource<ExecutionStop>? _next;
    private Thread? _thread;
    private SuspensionDecision _decision;
    private bool _suspended;

    /// <summary>
    /// Whether the program waits to be resumed.
    /// </summary>
    public bool IsSuspended
    {
        get
        {
            lock (_gate)
            {
                return _suspended;
            }
        }
    }

    /// <summary>
    /// Starts <paramref name="program"/> and answers when it first waits or is over.
    /// </summary>
    /// <param name="program">What runs the entry point; it is run on a thread of its own.</param>
    /// <returns>Where the program is by then.</returns>
    public Task<ExecutionStop> StartAsync(Func<RuntimeSemanticsEvaluationResult> program)
    {
        ArgumentNullException.ThrowIfNull(program);

        TaskCompletionSource<ExecutionStop> first;
        lock (_gate)
        {
            if (_thread is not null)
            {
                throw new InvalidOperationException("The execution has been started already.");
            }

            first = _next = NewStop();
            session.Halt.Gate = this;
            _thread = new Thread(() => Run(program)) { IsBackground = true, Name = "RDCore suspendable execution" };
        }

        _thread.Start();
        return first.Task;
    }

    /// <summary>
    /// Resumes the program from the place it waits at, and answers when it next waits or is over.
    /// </summary>
    /// <returns>Where the program is by then.</returns>
    /// <exception cref="InvalidOperationException">The program is not waiting.</exception>
    public Task<ExecutionStop> ResumeAsync() => ContinueAsync(step: null);

    /// <summary>
    /// Resumes the program for one step, and answers when it has waited again at the instruction <paramref name="step"/> says, or is over.
    /// </summary>
    /// <param name="step">How far the program goes.</param>
    /// <returns>Where the program is by then.</returns>
    /// <exception cref="InvalidOperationException">The program is not waiting.</exception>
    public Task<ExecutionStop> StepAsync(StepKind step) => ContinueAsync(step);

    private Task<ExecutionStop> ContinueAsync(StepKind? step)
    {
        TaskCompletionSource<ExecutionStop> next;
        lock (_gate)
        {
            if (!_suspended)
            {
                throw new InvalidOperationException("The program is not suspended.");
            }

            if (step is { } kind)
            {
                session.Halt.RequestStep(kind, session.CallStack.Depth);
            }

            next = _next = NewStop();
            _suspended = false;
            _decision = SuspensionDecision.Resume;
        }

        _ = _decided.Release();
        return next.Task;
    }

    /// <summary>
    /// Ends a program that waits: it is unwound as one is that nothing can resume, and what is left of its activations is let go of.
    /// </summary>
    /// <remarks>
    /// Does nothing for a program that is over. The session keeps what the program did to it, as it does when a program is stopped and not resumed.
    /// </remarks>
    public void Abandon()
    {
        Thread? thread;
        lock (_gate)
        {
            if (!_suspended)
            {
                return;
            }

            _next = NewStop();
            _suspended = false;
            _decision = SuspensionDecision.Abandon;
            thread = _thread;
        }

        _ = _decided.Release();
        thread?.Join();
        SessionWipe.Abandon(session);
        session.Halt.Clear();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Abandon();
        _decided.Dispose();
    }

    /// <inheritdoc/>
    SuspensionDecision IExecutionGate.Suspend(RuntimeHaltKind kind, SourceLocation? location)
    {
        // Only the program waits. Something else that runs in the session while it does - a statement typed at a stop - and reaches a Stop is stopped by it, as
        // a program that is not under a debugger is: holding its thread would hold whoever is waiting for the answer, and the program is not its to resume.
        if (Thread.CurrentThread != _thread)
        {
            return SuspensionDecision.Abandon;
        }

        TaskCompletionSource<ExecutionStop> waiting;
        lock (_gate)
        {
            waiting = _next!;
            _suspended = true;
        }

        // whoever started or resumed the program is told, and then the program waits: the lock is not held, since the answer is what lets that someone call us back.
        waiting.SetResult(new ExecutionStop(Result: null, kind, location));
        _decided.Wait();

        lock (_gate)
        {
            return _decision;
        }
    }

    private void Run(Func<RuntimeSemanticsEvaluationResult> program)
    {
        try
        {
            var result = program();
            TaskCompletionSource<ExecutionStop> last;
            lock (_gate)
            {
                last = _next!;
            }

            session.Halt.Gate = null;
            last.SetResult(new ExecutionStop(result));
        }
        catch (Exception exception)
        {
            TaskCompletionSource<ExecutionStop> last;
            lock (_gate)
            {
                last = _next!;
            }

            session.Halt.Gate = null;
            last.SetException(exception);
        }
    }

    private static TaskCompletionSource<ExecutionStop> NewStop() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
