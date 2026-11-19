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
/// <para>
/// With no program running, the session's own gate is one of these, made by <see cref="ForSessionEvents"/>: it has no thread of its own, and holds the thread of an
/// event of the session (<see cref="ISessionTurn.RunsAnEventOfTheSession"/>) where its handler stops, when somebody takes the stop. The handler is then resumed,
/// stepped and ended as a program is, and is over when its handlers are done.
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
    private bool _abandoned;

    // the thread that waits at the stop - the program's, or an event's - and the stop as the session's turn knows it, over once it is decided.
    private Thread? _waiter;
    private IDisposable? _stopped;

    // the session's gate (ForSessionEvents): who is told of a stop no request is running, the thread of the event it holds from its first stop until its handlers
    // are done, and whether they are.
    private readonly Func<ExecutionStop, Task<bool>>? _tell;
    private Thread? _eventThread;
    private readonly ManualResetEventSlim _eventDone = new(initialState: true);

    private SuspendableExecution(IRuntimeSession session, Func<ExecutionStop, Task<bool>> tell)
        : this(session)
    {
        _tell = tell;
        if (session.Turn is SessionTurn turn)
        {
            turn.SessionEventHandled += OnSessionEventHandled;
        }
    }

    /// <summary>
    /// The session's gate while no program runs: it holds the thread of an event of the session (<see cref="ISessionTurn.RunsAnEventOfTheSession"/>) where its
    /// handler stops, when <paramref name="tell"/> answers that somebody takes the stop. Any other thread that stops is answered
    /// <see cref="SuspensionDecision.Abandon"/>.
    /// </summary>
    /// <remarks>
    /// The first stop of an event is told, since no request runs the handler: <see cref="IsSuspended"/> is then true, and <see cref="ResumeAsync"/>,
    /// <see cref="StepAsync"/> and <see cref="Abandon"/> act on it as on a program. A stop after a resume or a step answers that request instead, and a resume whose
    /// handlers are done answers that it is over. <see cref="Abandon"/> ends the handler as an <c>End</c> does, and returns once it is unwound.
    /// </remarks>
    /// <param name="session">The session whose events it holds.</param>
    /// <param name="tell">
    /// Tells of the first stop of an event, and answers whether somebody takes it; the thread of the event waits for the answer, and makes the calls asked of it
    /// meanwhile. A stop that is not taken, or whose telling fails, is abandoned.
    /// </param>
    /// <returns>The gate, which the session's <see cref="ISessionHalt.Gate"/> holds while no program runs.</returns>
    public static SuspendableExecution ForSessionEvents(IRuntimeSession session, Func<ExecutionStop, Task<bool>> tell)
    {
        ArgumentNullException.ThrowIfNull(tell);
        return new SuspendableExecution(session, tell);
    }

    // the handlers of an event the gate held are done: the resume that let them go on is answered, and the gate takes the next event's stops.
    private void OnSessionEventHandled(Thread thread)
    {
        TaskCompletionSource<ExecutionStop>? done;
        lock (_gate)
        {
            if (thread != _eventThread)
            {
                return;
            }

            _eventThread = null;
            _abandoned = false;
            done = _next;
            _next = null;
        }

        _eventDone.Set();
        done?.TrySetResult(new ExecutionStop(RuntimeSemanticsEvaluationResult.Success(SDK.Model.Values.VBVoidValue.Void)));
    }

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
            // a stack with room for every activation the call stack takes (RuntimeCallStack.ProgramThreadStackSize), and for a parse or a load the program
            // asks for, which runs on it.
            _thread = SDK.Platform.LargeStackThread.NewThread(() => Run(program), "RDCore suspendable execution");
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

        Decided();
        return next.Task;
    }

    // the stop is over before the thread that waits goes on: an event of the program that waited for it may run, on that thread too.
    private void Decided()
    {
        Interlocked.Exchange(ref _stopped, null)?.Dispose();
        _ = _decided.Release();
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
            _abandoned = true;
            thread = _thread;

            // a program that waits in a handler of an event waits in a call underneath, a modal form's Show: it is ended as an End would end it, which lets the call
            // return, and none of its code runs on the way out (SessionWipe).
            if (_waiter != _thread)
            {
                session.Halt.Request(RuntimeHaltKind.End);
            }
        }

        Decided();
        if (_tell is not null)
        {
            // the session's gate has no thread of its own: the handler is over once its event's handlers are done.
            _eventDone.Wait();
        }
        else
        {
            thread?.Join();
        }

        SessionWipe.Abandon(session);
        session.Halt.Clear();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Abandon();
        if (_tell is not null && session.Turn is SessionTurn turn)
        {
            turn.SessionEventHandled -= OnSessionEventHandled;
        }

        _decided.Dispose();
        _eventDone.Dispose();
    }

    /// <inheritdoc/>
    SuspensionDecision IExecutionGate.Suspend(RuntimeHaltKind kind, SourceLocation? location)
    {
        // Only the program waits: its own thread, or one that runs the handlers of an event of the program - a click on a modal form, while the program waits in its
        // Show. Something else that runs in the session while it does - a statement typed at a stop, an event that statement's call raised - and reaches a Stop is
        // stopped by it, as a program that is not under a debugger is: holding its thread would hold whoever is waiting for the answer, and the program is not its to
        // resume.
        var thread = Thread.CurrentThread;
        if (_tell is not null)
        {
            return SuspendAnEvent(thread, kind, location);
        }

        if (thread != _thread && !session.Turn.RunsAnEventOfTheProgram)
        {
            return SuspensionDecision.Abandon;
        }

        TaskCompletionSource<ExecutionStop> waiting;
        lock (_gate)
        {
            // a program that was ended is unwinding, and nothing waits for it to stop again: whatever would hold it on the way out (the error it waited at, met
            // again by the activation that called the one that raised it) lets it go, or Abandon would wait for a thread that waits for it.
            if (_abandoned)
            {
                return SuspensionDecision.Abandon;
            }

            waiting = _next!;
            _suspended = true;
            _waiter = thread;
            _stopped = session.Turn.Stopped();
        }

        // whoever started or resumed the program is told, and then the program waits: the lock is not held, since the answer is what lets that someone call us back.
        waiting.SetResult(new ExecutionStop(Result: null, kind, location));
        if (thread == _thread || session.Turn.ServeOfTheCallingThread is not { } serve)
        {
            _decided.Wait();
        }
        else
        {
            // the thread of an event is the one its objects are called on: while it waits, what is asked of it - a debugger reading a control - is done, and the turn
            // is given up, so that an event of the program that comes meanwhile can be handled on it once the stop is over (it waits for that, ISessionTurn.Stopped).
            using (session.Turn.Yield())
            {
                while (!_decided.Wait(0))
                {
                    serve(EventThreadServe);
                }
            }
        }

        lock (_gate)
        {
            _waiter = null;
            return _decision;
        }
    }

    // how long the thread of an event that waits at a stop serves its calls before it looks for the answer again.
    private static readonly TimeSpan EventThreadServe = TimeSpan.FromMilliseconds(10);

    // the session's gate: only an event of the session waits, one at a time, and its first stop waits only when somebody takes it.
    private SuspensionDecision SuspendAnEvent(Thread thread, RuntimeHaltKind kind, SourceLocation? location)
    {
        if (!session.Turn.RunsAnEventOfTheSession)
        {
            return SuspensionDecision.Abandon;
        }

        TaskCompletionSource<ExecutionStop>? waiting;
        bool first;
        lock (_gate)
        {
            if (_abandoned || _suspended || _eventThread is { } held && held != thread)
            {
                return SuspensionDecision.Abandon;
            }

            first = _eventThread is null;
            if (first)
            {
                _eventThread = thread;
                _eventDone.Reset();
            }

            waiting = _next;
            _next = null;
            _suspended = true;
            _waiter = thread;
            _stopped = session.Turn.Stopped();
        }

        var stop = new ExecutionStop(Result: null, kind, location);
        var serve = session.Turn.ServeOfTheCallingThread;
        using (session.Turn.Yield())
        {
            if (waiting is not null)
            {
                waiting.SetResult(stop);
            }
            else if (!Told(stop, serve))
            {
                lock (_gate)
                {
                    _suspended = false;
                    _waiter = null;
                    if (first)
                    {
                        _eventThread = null;
                        _eventDone.Set();
                    }
                }

                Interlocked.Exchange(ref _stopped, null)?.Dispose();
                return SuspensionDecision.Abandon;
            }

            while (!_decided.Wait(serve is null ? Timeout.InfiniteTimeSpan : TimeSpan.Zero))
            {
                serve!(EventThreadServe);
            }
        }

        lock (_gate)
        {
            _waiter = null;
            return _decision;
        }
    }

    // tells of a stop nobody asked for, serving the thread meanwhile: whoever is told may ask for what the thread's objects say before answering.
    private bool Told(ExecutionStop stop, Action<TimeSpan>? serve)
    {
        var taken = 0;
        using var told = new ManualResetEventSlim();
        _ = Task.Run(async () =>
        {
            try
            {
                taken = await _tell!(stop) ? 1 : 0;
            }
            catch (Exception)
            {
                // a stop whose telling fails is not taken.
            }
            finally
            {
                told.Set();
            }
        });

        while (!told.Wait(serve is null ? Timeout.InfiniteTimeSpan : TimeSpan.Zero))
        {
            serve!(EventThreadServe);
        }

        return Volatile.Read(ref taken) == 1;
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
