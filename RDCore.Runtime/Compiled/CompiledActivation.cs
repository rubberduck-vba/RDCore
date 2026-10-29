using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.AST.Abstract;
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

    internal CompiledActivation(IRuntimeSession session, CallStackFrame frame, InstructionList list, ProcedureExecutor executor, RuntimeEvaluationContext context,
        CompiledBody body)
    {
        (_session, _frame, _list, _executor, _context, _body) = (session, frame, list, executor, context, body);
        _halt = session.Halt;
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

    private int Suspensions => (_halt as SessionHalt)?.Suspensions ?? 0;

    /// <summary>
    /// Whether the program waited while the last call ran: a debugger may then have changed the variables of the frame, which compiled code reads back.
    /// </summary>
    public bool WaitedDuringCall => Suspensions != _suspensionsBeforeCall;

    /// <summary>Whether the activation is handed to the interpreter after the call it made returns: the interpreter is wanted (<see cref="ISessionHalt.WantsInterpreter"/>).</summary>
    public bool WantsInterpreterAfterCall => _halt.WantsInterpreter;

    /// <summary>
    /// Hands the activation to the interpreter at the call at <paramref name="pc"/> that did not return: with the error the callee raised, which the
    /// interpreter gives the activation's handler as it would have; with the program stopped in the callee, which stops this activation too; or, when compiled
    /// code could not make the call, to make it as it always does. The frame has every variable and every open loop.
    /// </summary>
    public RuntimeExecutionOutcome AfterFailedCall(int pc)
    {
        _frame.RunsCompiled = false;
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

    /// <summary>Writes the variable in <paramref name="slot"/>, of an integral kind, into its cell.</summary>
    public void SpillIntegral(int slot, long value) => Spill(slot, value, 0);

    /// <summary>Writes the variable in <paramref name="slot"/>, of a floating-point kind, into its cell.</summary>
    public void SpillFloating(int slot, double value) => Spill(slot, 0, value);

    private void Spill(int slot, long integral, double floating)
    {
        if (_frame.TryGetSlotCell(slot, out var cell) && cell is ScalarCell scalar)
        {
            scalar.Write(new ScalarPayload(scalar.Type, integral, floating));
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
        (_frame.Pc, _frame.RunsCompiled) = (pc, false);
        var list = Body;
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
internal sealed record class CompiledBody(Func<CompiledActivation, RuntimeExecutionOutcome> Run, ScalarKind? ResultKind, IReadOnlyList<CompiledForLoop> Loops,
    string Name, IReadOnlyList<CompiledCallee> Callees)
{
    private long _walkedBack;
    private int _activations;

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
