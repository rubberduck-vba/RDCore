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
    }

    // how many polls go by before the next looks: a program is stopped within a few microseconds all the same.
    private const int PollInterval = 256;

    private int _untilPoll = PollInterval;

    /// <summary>
    /// Whether the program is to stop, or a debugger is attached: the activation is then handed to the interpreter, which stops it, or runs it under the
    /// debugger. Polled where the interpreter polls cancellation - at the back edges of loops - which is between two instructions; one poll in
    /// <c>256</c> looks.
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
        return _executor.IsCancellationPending || _halt.Gate is not null;
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
        _frame.Pc = pc;
        _body.WalkedBack(pc < _list.Items.Length ? _list.Items[pc].Node?.SourceLocation : null);
        return _executor.Run(_session, _frame, _list, _context);
    }

    /// <summary>What an activation that reached its end, or an <c>Exit</c>, returns.</summary>
    public RuntimeExecutionOutcome Return() => RuntimeExecutionOutcome.ExitProcedure;
}

/// <summary>
/// The compiled body of a procedure: its code, and what the activation needs to know of it to hand it to the interpreter.
/// </summary>
/// <param name="Run">The compiled code.</param>
/// <param name="ResultKind">The kind of the function result variable; <see langword="null"/> for a <c>Sub</c>.</param>
/// <param name="Loops">The <c>For</c> loops, by the number compiled code knows each by.</param>
internal sealed record class CompiledBody(Func<CompiledActivation, RuntimeExecutionOutcome> Run, ScalarKind? ResultKind, IReadOnlyList<CompiledForLoop> Loops)
{
    private long _walkedBack;

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
