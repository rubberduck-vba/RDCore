using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Instructions;
using System.Runtime.CompilerServices;

namespace RDCore.Runtime.Compiled;

/// <summary>
/// An activation of a procedure that runs compiled: what its compiled code is given, and everything it calls besides the payload
/// operations - reading its parameters, polling for what stops a program, and handing the activation to the interpreter.
/// </summary>
/// <remarks>
/// <para>
/// The activation's frame is laid out, its parameters given and its variables declared, as for an activation the interpreter runs. Compiled code reads
/// its parameters from their cells when it starts, and keeps its variables in its own locals from then on: the cells are written again only when the
/// activation is handed to the interpreter.
/// </para>
/// <para>
/// It is handed to the interpreter - <em>walked back</em> - at an instruction boundary: when an operation declines or raises an error, which the
/// interpreter then evaluates as it always does, with the message and the location it always gives; and when the program is to stop, which the interpreter
/// stops it for. Compiled code writes its variables into their cells and the state of each <c>For</c> loop it is in into the frame, and the interpreter runs
/// the rest of the activation from that instruction.
/// </para>
/// </remarks>
public sealed class CompiledActivation
{
    private readonly IRuntimeSession _session;
    private readonly CallStackFrame _frame;
    private readonly InstructionList _list;
    private readonly ProcedureExecutor _executor;
    private readonly RuntimeEvaluationContext _context;
    private readonly CompiledBody _body;
    private readonly ISessionHalt _halt;

    // what made the activation, which makes the activations of the calls it makes through their callees' typed entries, and ends them (EnterDirectly): the
    // session's own, never one the body was compiled with - a body is shared by every session composed over the same scopes.
    private readonly RuntimeProcedureInvoker _invoker;

    internal CompiledActivation(IRuntimeSession session, CallStackFrame frame, InstructionList list, ProcedureExecutor executor, RuntimeEvaluationContext context,
        CompiledBody body, RuntimeProcedureInvoker invoker)
    {
        (_session, _frame, _list, _executor, _context, _body, _invoker) = (session, frame, list, executor, context, body, invoker);
        _halt = session.Halt;
        _scopes = session.Symbols.Scopes;
        frame.RunsCompiled = true;

        // the safepoint the activation is handed to the interpreter at, under the random walk-back (CompiledBody.NextRandomSafepoint).
        if (CompiledExecution.WalksBackAtRandom)
        {
            _untilPoll = body.NextRandomSafepoint();
        }
    }

    // how many polls go by before the next looks: a program is stopped within a few microseconds all the same.
    private const int PollInterval = 256;

    private int _untilPoll = PollInterval;

    /// <summary>
    /// Whether the program is to stop, or the interpreter is wanted (<see cref="ISessionHalt.WantsInterpreter"/>): the activation is then handed to the
    /// interpreter, which stops it, or runs it under the debugger. Polled where the interpreter polls cancellation - at the back edges of loops - which is
    /// between two instructions; one poll in <c>256</c> looks.
    /// </summary>
    public bool ShouldWalkBack
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => --_untilPoll <= 0 && IsStopping();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool IsStopping()
    {
        _untilPoll = PollInterval;

        // a cancellation the program was resumed from is not asked about again: the interpreter would run the activation on without breaking.
        return CompiledExecution.WalksBackAtRandom || _executor.IsCancellationPending || _halt.WantsInterpreter;
    }

    // the arguments of the call compiled code is making, as it gives them: each a payload's long and double, of the kind of its parameter.
    private (long Integral, double Floating)[] _arguments = [];

    // what the last call that did not return did: the error it raised, or that the program was stopped in it; none when the callee was not one compiled code
    // calls, and the interpreter makes the call.
    private RuntimeExecutionOutcome? _failed;

    private int _suspensionsBeforeCall;

    /// <summary>Gives the argument at <paramref name="index"/> of the call compiled code is about to make.</summary>
    public void SetArgument(int index, long integral, double floating)
    {
        if (index >= _arguments.Length)
        {
            Array.Resize(ref _arguments, Math.Max(index + 1, _arguments.Length * 2));
        }

        _arguments[index] = (integral, floating);
    }

    /// <summary>
    /// Whether compiled code writes its variables into its frame before a call: under a debugger, which shows the frame of a caller while the program waits
    /// in its callee.
    /// </summary>
    public bool SpillsBeforeCalls => _halt.Gate is not null;

    /// <summary>
    /// Calls the procedure numbered <paramref name="callee"/> of the body with the arguments given (<see cref="SetArgument"/>), as the interpreter calls a
    /// procedure every parameter of which is a scalar passed by value: the instruction at <paramref name="pc"/> is the one that makes the call, which the
    /// frame says while the callee runs.
    /// </summary>
    /// <returns>Whether the call returned: what it returned is then a payload of its kind. When not, the interpreter is given the call (<see cref="AfterFailedCall"/>).</returns>
    public bool Call(int pc, int callee, out long integral, out double floating)
    {
        (integral, floating) = (0, 0);
        _frame.Pc = pc;
        _failed = null;
        _body.CountIndirectCall();
        var called = _body.Callees[callee];
        if (_executor.Expressions.ScalarInvokerOf(called.Procedure) is not { } invoker)
        {
            return false;
        }

        var arguments = new ScalarPayload[called.Parameters.Count];
        for (var i = 0; i < arguments.Length; i++)
        {
            arguments[i] = new ScalarPayload(ScalarKinds.TypeOf(called.Parameters[i]), _arguments[i].Integral, _arguments[i].Floating);
        }

        _suspensionsBeforeCall = Suspensions;
        var result = invoker.InvokeScalars(called.Procedure, arguments);
        if (!result.IsSuccess)
        {
            _failed = result.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(result.ErrorInfo!);
            return false;
        }

        if (called.Result is null)
        {
            return true;
        }

        // a function of a scalar type returns a known value of it.
        if (result.Result is not { } value || !ScalarPayload.TryOf(value.TypeInfo, value.RuntimeValue, out var payload))
        {
            _failed = RuntimeExecutionOutcome.InternalError;
            return false;
        }

        (integral, floating) = (payload.Integral, payload.Floating);
        return true;
    }

    /// <summary>
    /// The activation of the procedure numbered <paramref name="callee"/> of the body, a compiled one, which compiled code is about to call through its typed
    /// entry (<see cref="DirectCall"/>), with the arguments as it holds them: the instruction at <paramref name="pc"/> is the one that makes the call, which the
    /// frame says while the callee runs. The call is the invoker's (<see cref="Call"/>) when the callee is not called this way, or not now: the session runs
    /// no compiled code any more (its engine was changed); the session's symbols were defined again since this activation began, or the callee's body was
    /// loaded again, either of which the body it was compiled against may no longer be; the interpreter is wanted (<see cref="ISessionHalt.WantsInterpreter"/>);
    /// the session is being analyzed; or the call stack has no room for another frame.
    /// </summary>
    /// <returns>The callee's activation, which its entry is called with; <see langword="null"/> when the call is to be made through the invoker.</returns>
    public CompiledActivation? EnterDirectly(int pc, int callee)
    {
        if (_body.Directs[callee] is not { Body: { } body } direct || !ReferenceEquals(_session.Symbols.Scopes, _scopes) || _halt.WantsInterpreter
            || _executor.Observation is not null || _session.Errors.Untrappable is not null
            || CompiledExecution.EngineFor(_session.Environment.ExecutionEngine) is not VBExecutionEngine.Compiled
            || !ReferenceEquals(_invoker.BodyOf(direct.Procedure), direct.List))
        {
            return null;
        }

        _frame.Pc = pc;
        _failed = null;
        if (_invoker.PushCompiledActivation(direct.Procedure, direct.List) is not { } frame)
        {
            return null;
        }

        _body.CountDirectCall();
        _suspensionsBeforeCall = Suspensions;
        return new CompiledActivation(_session, frame, direct.List, _executor, direct.Context, body, _invoker);
    }

    /// <summary>
    /// Ends the activation <paramref name="callee"/> of the call numbered <paramref name="call"/> of the body, which compiled code made through the callee's
    /// typed entry (<see cref="EnterDirectly"/>), and which returned <paramref name="outcome"/>: the frame off the stack, as the invoker takes it off.
    /// </summary>
    /// <returns>
    /// Whether the call returned: what it returned is then a payload of its kind, when what it returns is read. When not, the interpreter is given the call
    /// (<see cref="AfterFailedCall"/>), with the error the callee raised, or with the program stopped in it.
    /// </returns>
    public bool ReturnedDirectly(int call, CompiledActivation callee, RuntimeExecutionOutcome outcome, out long integral, out double floating)
    {
        (integral, floating) = (0, 0);
        outcome = _invoker.PopCompiledActivation(outcome);
        if (outcome.Kind is not RuntimeExecutionOutcomeKind.ExitProcedure)
        {
            // as the invoker answers it (InvokeScalars): an error is the call's, and a call that was stopped has no answer.
            _failed = outcome.Kind is RuntimeExecutionOutcomeKind.Error ? outcome : RuntimeExecutionOutcome.InternalError;
            return false;
        }

        // what the call returns is read only where the call reads it, as Call reads it: a Function called as a statement returns what is let go of.
        if (_body.Callees[call].Result is null)
        {
            return true;
        }

        // a function of a scalar type returns a payload of it: its compiled code assigned it when it returned, and so did the interpreter it was handed to.
        if (!callee._frame.TryReadResult(out var payload))
        {
            _failed = RuntimeExecutionOutcome.InternalError;
            return false;
        }

        (integral, floating) = (payload.Integral, payload.Floating);
        return true;
    }

    private int Suspensions => (_halt as SessionHalt)?.Suspensions ?? 0;

    /// <summary>
    /// Whether the program waited while the last call ran: a debugger may then have changed the variables of the frame, which compiled code reads back.
    /// </summary>
    public bool WaitedDuringCall => Suspensions != _suspensionsBeforeCall;

    // the symbols compiled code was compiled against: a procedure it calls, defined again since - its parameters retyped, passed another way, one added - is
    // another call than the one compiled.
    private readonly ScopeTree _scopes;

    /// <summary>
    /// Whether the activation is handed to the interpreter after the call it made returns: the interpreter is wanted (<see cref="ISessionHalt.WantsInterpreter"/>);
    /// the procedure was edited while the program waited in the callee, and the activation goes on in its edited body (<see cref="CallStackFrame.HasPendingRebase"/>);
    /// or the session's symbols were defined again while the callee ran - an edit made while the program waited - and what compiled code calls, and how,
    /// may not be what it was compiled for. The interpreter goes on with them as they are now, from the instruction after the call.
    /// </summary>
    public bool WantsInterpreterAfterCall => _halt.WantsInterpreter || _frame.HasPendingRebase || !ReferenceEquals(_session.Symbols.Scopes, _scopes);

    /// <summary>
    /// Hands the activation to the interpreter at the call at <paramref name="pc"/> that did not return: with the error the callee raised, which the
    /// interpreter gives the activation's handler as it would have; with the program stopped in the callee, which stops this activation too; or, when compiled
    /// code could not make the call, to make it as it always does. The frame has every variable and every open loop.
    /// </summary>
    public RuntimeExecutionOutcome AfterFailedCall(int pc)
    {
        _frame.RunsCompiled = false;

        // the procedure edited while the program waited in the callee: the error is the call's, where the call now is.
        if (_frame.ApplyPendingRebase(atTheCall: true))
        {
            pc = _frame.Pc;
        }

        var list = Body;
        _body.WalkedBack(pc < list.Items.Length ? list.Items[pc].Location : null);
        if (_failed is not { } outcome)
        {
            _frame.Pc = pc;
            return _executor.Run(_session, _frame, list, _context);
        }

        return _executor.RunAfter(_session, _frame, list, _context, pc, outcome);
    }

    /// <summary>The payload of the parameter in <paramref name="slot"/>, of an integral kind, when its cell holds one.</summary>
    public bool TryReadIntegral(int slot, out long value)
    {
        value = 0;
        if (!_frame.TryGetSlotCell(slot, out var cell) || cell is not ScalarCell scalar || !scalar.TryRead(out var payload))
        {
            return false;
        }

        value = payload.Integral;
        return true;
    }

    /// <summary>The payload of the parameter in <paramref name="slot"/>, of a floating-point kind, when its cell holds one.</summary>
    public bool TryReadFloating(int slot, out double value)
    {
        value = 0;
        if (!_frame.TryGetSlotCell(slot, out var cell) || cell is not ScalarCell scalar || !scalar.TryRead(out var payload))
        {
            return false;
        }

        value = payload.Floating;
        return true;
    }

    /// <summary>The payload of the function result variable, of an integral kind, when it holds one: what a debugger may have assigned it while the program waited.</summary>
    public bool TryReadResultIntegral(out long value)
    {
        var read = _frame.TryReadResult(out var payload);
        value = read ? payload.Integral : 0;
        return read;
    }

    /// <summary>The payload of the function result variable, of a floating-point kind, when it holds one.</summary>
    public bool TryReadResultFloating(out double value)
    {
        var read = _frame.TryReadResult(out var payload);
        value = read ? payload.Floating : 0;
        return read;
    }

    /// <summary>Writes the variable in <paramref name="slot"/>, of an integral kind, into its cell.</summary>
    public void SpillIntegral(int slot, long value) => Spill(slot, value, 0);

    /// <summary>Writes the variable in <paramref name="slot"/>, of a floating-point kind, into its cell.</summary>
    public void SpillFloating(int slot, double value) => Spill(slot, 0, value);

    private void Spill(int slot, long integral, double floating)
    {
        if (_frame.TryGetSlotCell(slot, out var cell))
        {
            if (cell is ScalarCell scalar)
            {
                scalar.Write(new ScalarPayload(scalar.Type, integral, floating));
            }

            return;
        }

        // an activation of a call compiled code made through the callee's typed entry holds no cell until it needs one (EnterDirectly): it is given the cell
        // it would have been given at the call, holding what the variable holds now.
        if (_body.SlotSymbols.TryGetValue(slot, out var symbol) && symbol is ITypedSymbol { ResolvedType: var type } && ScalarPayload.IsScalar(type))
        {
            _frame.PushPayload(symbol, new ScalarPayload(type, integral, floating), slot);
        }
    }

    /// <summary>Assigns the function result variable a payload of an integral kind.</summary>
    public void AssignResultIntegral(long value) => _frame.AssignResult(new ScalarPayload(ScalarKinds.TypeOf(_body.ResultKind!.Value), value, 0));

    /// <summary>Assigns the function result variable a payload of a floating-point kind.</summary>
    public void AssignResultFloating(double value) => _frame.AssignResult(new ScalarPayload(ScalarKinds.TypeOf(_body.ResultKind!.Value), 0, value));

    /// <summary>
    /// Writes the state of the <c>For</c> loop numbered <paramref name="loop"/> into the frame, as its opener would have stashed it: its end and its step,
    /// of the kind it counts in.
    /// </summary>
    public void SpillForLoop(int loop, long endIntegral, double endFloating, long stepIntegral, double stepFloating)
    {
        var info = _body.Loops[loop];
        var type = ScalarKinds.TypeOf(info.Kind);
        var end = new ScalarPayload(type, endIntegral, endFloating);
        var step = new ScalarPayload(type, stepIntegral, stepFloating);
        _frame.SetForLoopState(info.Opener, new ForLoopState(info.Counter, info.Control, type.CreateValue(new ValueBindingHandle(end.Box())),
            type.CreateValue(new ValueBindingHandle(step.Box())))
        {
            CountsDown = ScalarKinds.IsIntegral(info.Kind) ? stepIntegral < 0 : stepFloating < 0,
            RuntimeState = new ForLoopPayloads(),
        });
    }

    /// <summary>
    /// Hands the activation to the interpreter, which runs it from the instruction at <paramref name="pc"/>: what the activation returns is what the
    /// interpreter's run of it does. Every variable and every open loop has been written into the frame.
    /// </summary>
    public RuntimeExecutionOutcome Interpret(int pc)
    {
        // the procedure edited while the program waited in a callee: the activation goes on after the call, in the edited body.
        (_frame.Pc, _frame.RunsCompiled) = (pc, false);
        _ = _frame.ApplyPendingRebase(atTheCall: false);
        var list = Body;
        pc = _frame.Pc;
        _body.WalkedBack(pc < list.Items.Length ? list.Items[pc].Location : null);
        return _executor.Run(_session, _frame, list, _context);
    }

    // the body the interpreter goes on in: the frame's, which an edit made while the program waited may have given it, the same instructions at the same
    // offsets as the one compiled code was compiled from (CallStackFrame.TryRebase).
    private InstructionList Body => _frame.Body ?? _list;

    /// <summary>What an activation that reached its end, or an <c>Exit</c>, returns.</summary>
    public RuntimeExecutionOutcome Return()
    {
        _frame.RunsCompiled = false;
        return RuntimeExecutionOutcome.ExitProcedure;
    }
}

/// <summary>
/// The compiled body of a procedure: its code, and what the activation needs to know of it to hand it to the interpreter.
/// </summary>
/// <param name="Run">The compiled code.</param>
/// <param name="ResultKind">The kind of the function result variable; <see langword="null"/> for a <c>Sub</c>.</param>
/// <param name="Loops">The <c>For</c> loops, by the number compiled code knows each by.</param>
/// <param name="Name">The procedure's name: what the random walk-back tells one body from another by, the same in every run.</param>
/// <param name="Callees">The procedures the body calls, by the number compiled code knows each by.</param>
/// <param name="Directs">For each of <paramref name="Callees"/>, the compiled callee it calls directly; <see langword="null"/> for one it calls through the invoker.</param>
/// <param name="SlotSymbols">The parameter or the variable each slot of the body's activations holds: what a cell is made for, when the activation has none yet.</param>
internal sealed record class CompiledBody(Func<CompiledActivation, RuntimeExecutionOutcome> Run, ScalarKind? ResultKind, IReadOnlyList<CompiledForLoop> Loops,
    string Name, IReadOnlyList<CompiledCallee> Callees, IReadOnlyList<DirectCall?> Directs, IReadOnlyDictionary<int, Symbol> SlotSymbols)
{
    private long _walkedBack;
    private int _activations;

    private long _directCalls;
    private long _indirectCalls;

    /// <summary>How many calls the body made of a compiled procedure directly, through its typed entry.</summary>
    public long DirectCalls => Interlocked.Read(ref _directCalls);

    /// <summary>How many calls the body made through the invoker: of a procedure that is not compiled, or not when it was.</summary>
    public long IndirectCalls => Interlocked.Read(ref _indirectCalls);

    internal void CountDirectCall() => Interlocked.Increment(ref _directCalls);

    internal void CountIndirectCall() => Interlocked.Increment(ref _indirectCalls);

    /// <summary>The typed entry of the body: its compiled activation, then each parameter. Set once the body's type is created.</summary>
    public System.Reflection.MethodInfo? Entry { get; init; }

    /// <summary>
    /// The safepoint the next activation of the body is handed to the interpreter at, under the random walk-back: how many safepoints it passes first -
    /// under that mode every instruction boundary is one. It is chosen from the body's name, how many activations of it came before, and the seed
    /// (<see cref="CompiledExecution.WalkBackSeed"/>), so that a run can be replayed whatever else runs beside it; and from 1 to 3,969 (a 5-bit count
    /// times a power of two up to 128), so that the late iterations of a loop are reached as well as the first.
    /// </summary>
    internal int NextRandomSafepoint()
    {
        var hash = 2166136261u;
        foreach (var character in Name)
        {
            hash = (hash ^ character) * 16777619u;
        }

        hash = (hash ^ (uint)CompiledExecution.WalkBackSeed) * 16777619u;
        hash = (hash ^ (uint)Interlocked.Increment(ref _activations)) * 2654435761u;
        return 1 + (int)((hash >> 27) << (int)((hash >> 8) % 8));
    }

    /// <summary>How many times an activation of the body was handed to the interpreter.</summary>
    public long WalkBacks => Interlocked.Read(ref _walkedBack);

    /// <summary>
    /// Where the statement is that an activation of the body was last handed to the interpreter at: the one that failed or declined, or a back edge where the
    /// program was to stop, or the first when a parameter's cell held no payload. <see langword="null"/> before any was.
    /// </summary>
    public SDK.Model.Source.SourceLocation? LastWalkedBackAt { get; private set; }

    internal void WalkedBack(SDK.Model.Source.SourceLocation? at)
    {
        LastWalkedBackAt = at;
        _ = Interlocked.Increment(ref _walkedBack);
    }
}

/// <summary>
/// A <c>For</c> loop of a compiled body.
/// </summary>
/// <param name="Opener">The offset of its opener, which its state is stashed by.</param>
/// <param name="Counter">The variable it counts with.</param>
/// <param name="Control">The counter's expression.</param>
/// <param name="Kind">The kind it counts in, the counter's.</param>
internal sealed record class CompiledForLoop(int Opener, Symbol Counter, ExpressionNode Control, ScalarKind Kind);
