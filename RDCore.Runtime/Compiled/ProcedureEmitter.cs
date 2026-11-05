using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using System.Reflection;
using System.Reflection.Emit;

namespace RDCore.Runtime.Compiled;

/// <summary>
/// Writes the IL of a procedure that is compiled: one static method, <c>Run(CompiledActivation)</c>, that does what the interpreter does
/// with its instruction list, instruction for instruction.
/// </summary>
/// <remarks>
/// <para>
/// Each instruction is an IL label, so the control flow is the interpreter's one to one. Each parameter and variable of the activation is a local of the
/// method, a <see cref="long"/> or a <see cref="double"/>, and so are the end and the step of each <c>For</c> loop - but one held in a cell
/// (<see cref="ProcedureAnalysis.CellSlots"/>), which is a local holding the cell, read and written through <see cref="CompiledActivation"/>. Every operation that can fail is a call
/// of a payload operation or of <see cref="Extended"/>, which says so; compiled code then writes the activation into its frame and hands it to the
/// interpreter at the instruction that failed (<see cref="CompiledActivation.Interpret"/>), which evaluates it as it always does.
/// </para>
/// <para>
/// The IL references nothing but the members of <c>RDCore.Runtime.Compiled</c> (the support boundary, A4).
/// </para>
/// </remarks>
internal sealed class ProcedureEmitter
{
    private static readonly Type Payloads = typeof(PayloadOperations);
    private static readonly Type Activation = typeof(CompiledActivation);

    private readonly ProcedureAnalysis _analysis;
    private readonly IReadOnlyList<DirectCall?> _directs;
    private readonly ILGenerator _il;
    private readonly Dictionary<int, LocalBuilder> _slots = [];

    // the slots held in cells, each by the local its cell is held in, and by its number of the activation's cells (CompiledActivation.TakeReference).
    private readonly Dictionary<int, LocalBuilder> _cells = [];
    private readonly Dictionary<int, int> _cellNumbers = [];
    private readonly Dictionary<int, Label> _instructions = [];
    private readonly Dictionary<int, Label> _failures = [];
    private readonly Dictionary<int, Label> _recoveries = [];
    private readonly List<LoopLocals> _loops = [];
    private readonly LocalBuilder _pc;
    private readonly Label _walkBack;

    // the instruction at pc run by the interpreter alone, and the activation gone on with where it left it (CompiledActivation.Recover): every variable
    // written into the frame first; read back after it (the re-entry); and the instruction the activation goes on at found by its offset (the dispatch).
    private readonly Label _recover;
    private readonly Label _reentry;
    private readonly Label _dispatch;
    private bool _recovers;
    private bool _dispatches;
    private LocalBuilder? _result;

    // Opened: whether the loop's statement ever ran, and gave it an end and a step - kept when the loop ends, and when its statement, run again, fails.
    private sealed record class LoopLocals(LocalBuilder End, LocalBuilder Step, LocalBuilder CountsDown, LocalBuilder Opened, ScalarKind Kind);

    private ProcedureEmitter(ProcedureAnalysis analysis, IReadOnlyList<DirectCall?> directs, ILGenerator il)
    {
        (_analysis, _directs, _il) = (analysis, directs, il);
        _pc = il.DeclareLocal(typeof(int));
        _walkBack = il.DefineLabel();
        (_recover, _reentry, _dispatch) = (il.DefineLabel(), il.DefineLabel(), il.DefineLabel());
    }

    /// <summary>
    /// Writes the procedure's two methods from <paramref name="analysis"/>, each a static method that returns a <see cref="RuntimeExecutionOutcome"/>:
    /// <paramref name="entry"/>, the typed entry, whose parameters are the <see cref="CompiledActivation"/> and then each parameter of the procedure as a
    /// <see cref="long"/> or a <see cref="double"/>, which is the procedure's code; and <paramref name="run"/>, of the activation alone, which reads the
    /// parameters from the frame's cells and calls the entry with them.
    /// </summary>
    /// <param name="directs">For each callee of the analysis, the compiled procedure the body calls through its typed entry, or none.</param>
    public static void Emit(ProcedureAnalysis analysis, MethodBuilder run, MethodBuilder entry, IReadOnlyList<DirectCall?> directs)
    {
        EmitRun(analysis, run.GetILGenerator(), entry);
        new ProcedureEmitter(analysis, directs, entry.GetILGenerator()).Emit();
    }

    // the parameters, read from their cells - a cell that holds no payload is the interpreter's to read, and the activation is handed to it before anything
    // runs - and the typed entry called with them. A parameter passed by reference is given its cell, the caller's variable, which compiled code reads as it
    // goes; one bound to an address - a field, an element - is the interpreter's.
    private static void EmitRun(ProcedureAnalysis analysis, ILGenerator il, MethodBuilder entry)
    {
        var interpret = il.DefineLabel();
        var read = new List<LocalBuilder>(analysis.Parameters.Count);
        foreach (var (slot, kind, byReference) in analysis.Parameters)
        {
            var local = il.DeclareLocal(byReference ? typeof(object) : ClrTypeOf(kind));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, slot);
            il.Emit(OpCodes.Ldloca, local);
            il.Emit(OpCodes.Call, Activation.GetMethod(byReference
                ? nameof(CompiledActivation.TryReadReference)
                : ScalarKinds.IsIntegral(kind) ? nameof(CompiledActivation.TryReadIntegral) : nameof(CompiledActivation.TryReadFloating))!);
            il.Emit(OpCodes.Brfalse, interpret);
            read.Add(local);
        }

        il.Emit(OpCodes.Ldarg_0);
        foreach (var local in read)
        {
            il.Emit(OpCodes.Ldloc, local);
        }

        il.Emit(OpCodes.Call, entry);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(interpret);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.Interpret))!);
        il.Emit(OpCodes.Ret);
    }

    private void Emit()
    {
        var cellSlots = _analysis.CellSlots;
        for (var i = 0; i < cellSlots.Count; i++)
        {
            _cells[cellSlots[i]] = _il.DeclareLocal(typeof(object));
            _cellNumbers[cellSlots[i]] = i;
        }

        foreach (var (slot, kind) in _analysis.Slots)
        {
            if (!_cells.ContainsKey(slot))
            {
                _slots[slot] = _il.DeclareLocal(ClrTypeOf(kind));
            }
        }

        if (_analysis.ResultKind is { } resultKind)
        {
            _result = _il.DeclareLocal(ClrTypeOf(resultKind));
        }

        foreach (var loop in _analysis.Loops)
        {
            _loops.Add(new LoopLocals(_il.DeclareLocal(ClrTypeOf(loop.Kind)), _il.DeclareLocal(ClrTypeOf(loop.Kind)), _il.DeclareLocal(typeof(bool)),
                _il.DeclareLocal(typeof(bool)), loop.Kind));
        }

        for (var offset = 0; offset <= _analysis.Length; offset++)
        {
            _instructions[offset] = _il.DefineLabel();
        }

        EmitEntry();
        foreach (var step in _analysis.Steps)
        {
            _il.MarkLabel(_instructions[step.Offset]);

            // every instruction boundary is a safepoint: under the random walk-back the activation may be handed to the interpreter at any of them, and not only
            // where it polls for a program to stop - which a step that polls already does, before it.
            if (CompiledExecution.WalksBackAtRandom && step is not (LoopBackStep or ForNextStep) && !(step is JumpStep jump && jump.Target <= jump.Offset))
            {
                EmitPoll(step.Offset);
            }

            EmitStep(step);
        }

        // the end of the body (MS-VBAL §5.4.2.17): as an Exit.
        _il.MarkLabel(_instructions[_analysis.Length]);
        EmitExit();

        // a call that fails in a body with an error handler recovers at it (RecoverAt): its label is emitted with the failures, after it.
        EmitFailedCalls();
        EmitFailures();
        EmitWalkBack();
        EmitRecover();
        EmitDispatch();
    }

    /// <summary>The CLR type a payload of <paramref name="kind"/> is held in: a <see cref="long"/> of an integral kind, a <see cref="double"/> of a floating-point one.</summary>
    internal static Type ClrTypeOf(ScalarKind kind) => ScalarKinds.IsIntegral(kind) ? typeof(long) : typeof(double);

    // the parameters, as the entry is given them: its arguments after the activation - the cell of one passed by reference, which the activation holds for
    // its frame. Then the cell of each other slot held in one: a parameter passed by value given one holding its argument, a variable one holding its
    // type's default; the frame's own, when the activation has a frame that holds one, and the interpreter's from the start when it holds another binding.
    private void EmitEntry()
    {
        for (var i = 0; i < _analysis.Parameters.Count; i++)
        {
            var (slot, kind, byReference) = _analysis.Parameters[i];
            if (byReference)
            {
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldc_I4, _cellNumbers[slot]);
                _il.Emit(OpCodes.Ldarg, (short)(i + 1));
                _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.TakeReference))!);
                _il.Emit(OpCodes.Ldarg, (short)(i + 1));
                _il.Emit(OpCodes.Stloc, _cells[slot]);
            }
            else if (_cells.TryGetValue(slot, out var cell))
            {
                EmitCellOf(slot, kind, cell, () => _il.Emit(OpCodes.Ldarg, (short)(i + 1)));
            }
            else
            {
                _il.Emit(OpCodes.Ldarg, (short)(i + 1));
                _il.Emit(OpCodes.Stloc, _slots[slot]);
            }
        }

        foreach (var slot in _analysis.CellSlots)
        {
            if (!_analysis.Parameters.Exists(parameter => parameter.Slot == slot))
            {
                var kind = _analysis.Slots[slot];
                EmitCellOf(slot, kind, _cells[slot], () => LoadZero(kind));
            }
        }
    }

    // the cell of a slot that is not a parameter passed by reference, holding what loadValue loads: the activation is the interpreter's from the start when
    // its frame holds no cell for it.
    private void EmitCellOf(int slot, ScalarKind kind, LocalBuilder cell, Action loadValue)
    {
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, _cellNumbers[slot]);
        loadValue();
        _il.Emit(OpCodes.Call, Activation.GetMethod(ScalarKinds.IsIntegral(kind) ? nameof(CompiledActivation.CellOfIntegral) : nameof(CompiledActivation.CellOfFloating))!);
        _il.Emit(OpCodes.Stloc, cell);
        _il.Emit(OpCodes.Ldloc, cell);
        _il.Emit(OpCodes.Brfalse, WalkBackAt(0));
    }

    private void EmitStep(CompiledStep step)
    {
        switch (step)
        {
            case LetStep let when let.Slot < 0:
                Store(EmitPayload(let.Value, FailureAt(let.Offset)), let.Value.Kind, _result!);
                break;

            case LetStep let:
                StoreSlot(EmitPayload(let.Value, FailureAt(let.Offset)), let.Value.Kind, let.Slot);
                break;

            case BranchStep branch:
                // true falls through into the branch, false goes to its Else.
                EmitBranch(branch.Condition, branch.Negate, whenFalse: _instructions[branch.Else], FailureAt(branch.Offset));
                break;

            case LoopBackStep loopBack:
                EmitPoll(loopBack.Offset);
                var past = _il.DefineLabel();
                EmitBranch(loopBack.Condition, loopBack.Negate, whenFalse: past, FailureAt(loopBack.Offset));
                _il.Emit(OpCodes.Br, _instructions[loopBack.Target]);
                _il.MarkLabel(past);
                break;

            case JumpStep jump:
                if (jump.Target <= jump.Offset)
                {
                    EmitPoll(jump.Offset);
                }

                _il.Emit(OpCodes.Br, _instructions[jump.Target]);
                break;

            case ForOpenStep open:
                EmitForOpen(open);
                break;

            case ForNextStep next:
                EmitForNext(next);
                break;

            case ExitStep:
                EmitExit();
                break;

            case CallStep call:
                EmitCall(call);
                break;

            case OnErrorStep onError:
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldc_I4, onError.Offset);
                _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.OnError))!);
                _il.Emit(OpCodes.Brfalse, WalkBackAt(onError.Offset));
                break;

            case GoSubStep goSub:
                // a subroutine above its GoSub is a back edge, which a program that never returns from it would go round forever.
                if (goSub.Target <= goSub.Offset)
                {
                    EmitPoll(goSub.Offset);
                }

                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldc_I4, goSub.Offset + 1);
                _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.GoSub))!);
                _il.Emit(OpCodes.Br, _instructions[goSub.Target]);
                break;

            case ReturnStep returnStep:
                // where it returns to may be above it: a back edge.
                EmitPoll(returnStep.Offset);
                _dispatches = true;
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldloca, _pc);
                _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.TryReturnFromGoSub))!);
                _il.Emit(OpCodes.Brtrue, _dispatch);
                _il.Emit(OpCodes.Br, RecoverAt(returnStep.Offset));
                break;

            case SteppedStep stepped:
                _il.Emit(OpCodes.Br, RecoverAt(stepped.Offset));
                break;
        }
    }

    // the arguments evaluated - where one fails, nothing was called, and the interpreter makes the instruction from its start - then the call. Under a
    // debugger the activation's variables and its function result are written into its frame before it, which a debugger shows while the program waits in
    // the callee; when the program waited, what the debugger may have changed of them is read back after it. A call that does not return - an error, the
    // program stopped, a callee compiled code can't call - is the interpreter's at the call (CompiledActivation.AfterFailedCall). After a call returns, the
    // activation is the interpreter's at the next instruction when the interpreter is wanted, or the symbols it was compiled against were defined again.
    private void EmitCall(CallStep call)
    {
        // the arguments, each held where it is - a variable, a constant - or in a local of its own; one that fails fails the instruction before anything is called.
        var failure = FailureAt(call.Offset);
        var arguments = new Operand[call.Arguments.Count];
        for (var i = 0; i < arguments.Length; i++)
        {
            arguments[i] = EmitArgument(call.Arguments[i], failure);
        }

        var called = _il.DefineLabel();
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, Activation.GetProperty(nameof(CompiledActivation.SpillsBeforeCalls))!.GetMethod!);
        _il.Emit(OpCodes.Brfalse, called);
        EmitSpill();
        _il.MarkLabel(called);

        var integral = _il.DeclareLocal(typeof(long));
        var floating = _il.DeclareLocal(typeof(double));
        var returned = _il.DefineLabel();

        // a callee that is compiled is called through its typed entry, with the arguments as they are (IR lowering S2.3b), while the activation can be made that
        // way (CompiledActivation.EnterDirectly); through the invoker otherwise.
        if (_directs[call.Callee] is { } direct)
        {
            var indirect = _il.DefineLabel();
            var callee = _il.DeclareLocal(typeof(CompiledActivation));
            var outcome = _il.DeclareLocal(typeof(RuntimeExecutionOutcome));
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, call.Offset);
            _il.Emit(OpCodes.Ldc_I4, call.Callee);
            _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.EnterDirectly))!);
            _il.Emit(OpCodes.Stloc, callee);
            _il.Emit(OpCodes.Ldloc, callee);
            _il.Emit(OpCodes.Brfalse, indirect);
            var given = EmitTemporaries(call, arguments);

            // a CLR exception the callee lets out - a defect of the platform, which no handler of the program catches - takes its activation off the stack as it
            // goes by (CompiledActivation.AbandonDirectCall): what is on the stack after is what was before the call.
            _il.BeginExceptionBlock();
            _il.Emit(OpCodes.Ldloc, callee);
            for (var i = 0; i < given.Length; i++)
            {
                Load(given[i], call.Arguments[i].Kind);
            }

            _il.Emit(OpCodes.Call, direct.Entry);
            _il.Emit(OpCodes.Stloc, outcome);
            _il.BeginFaultBlock();
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, callee);
            _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.AbandonDirectCall))!);
            _il.EndExceptionBlock();

            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, call.Callee);
            _il.Emit(OpCodes.Ldloc, callee);
            _il.Emit(OpCodes.Ldloc, outcome);
            _il.Emit(OpCodes.Ldloca, integral);
            _il.Emit(OpCodes.Ldloca, floating);
            _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.ReturnedDirectly))!);
            _il.Emit(OpCodes.Brfalse, CallFailedAt(call.Offset));
            _il.Emit(OpCodes.Br, returned);
            _il.MarkLabel(indirect);
        }

        for (var i = 0; i < arguments.Length; i++)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, i);
            if (call.Arguments[i] is ReferenceNode)
            {
                Load(arguments[i], call.Arguments[i].Kind);
                _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.SetReference))!);
                continue;
            }

            LoadAsBoth(arguments[i], call.Arguments[i].Kind);
            _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.SetArgument))!);
        }

        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, call.Offset);
        _il.Emit(OpCodes.Ldc_I4, call.Callee);
        _il.Emit(OpCodes.Ldloca, integral);
        _il.Emit(OpCodes.Ldloca, floating);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.Call))!);
        _il.Emit(OpCodes.Brfalse, CallFailedAt(call.Offset));
        _il.MarkLabel(returned);

        var current = _il.DefineLabel();
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, Activation.GetProperty(nameof(CompiledActivation.WaitedDuringCall))!.GetMethod!);
        _il.Emit(OpCodes.Brfalse, current);
        EmitReload();
        _il.MarkLabel(current);

        if (call.Result != CallStep.NoResult)
        {
            var kind = _analysis.Callees[call.Callee].Result!.Value;
            var returnedValue = new Operand(ScalarKinds.IsIntegral(kind) ? integral : floating, 0, 0);
            if (call.Result < 0)
            {
                Store(returnedValue, kind, _result!);
            }
            else
            {
                StoreSlot(returnedValue, kind, call.Result);
            }
        }

        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, Activation.GetProperty(nameof(CompiledActivation.WantsInterpreterAfterCall))!.GetMethod!);
        // the procedure edited while the program waited in the callee, or the interpreter wanted: the activation is the interpreter's from after the call, in
        // the body it goes on in - never stepped in the body it was compiled from (CompiledActivation.Interpret).
        _il.Emit(OpCodes.Brtrue, WalkBackAt(call.Offset + 1));
    }

    private readonly Dictionary<int, Label> _failedCalls = [];

    // the label a call that did not return goes to: the activation written into its frame - unless the program waited during the call, when the frame is
    // what the debugger left it - and the interpreter given what the call did, at the call.
    private Label CallFailedAt(int offset)
    {
        if (!_failedCalls.TryGetValue(offset, out var label))
        {
            label = _il.DefineLabel();
            _failedCalls[offset] = label;
        }

        return label;
    }

    private void EmitFailedCalls()
    {
        foreach (var (offset, label) in _failedCalls)
        {
            _il.MarkLabel(label);
            var spilled = _il.DefineLabel();
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Call, Activation.GetProperty(nameof(CompiledActivation.WaitedDuringCall))!.GetMethod!);
            _il.Emit(OpCodes.Brtrue, spilled);

            // a body with an error handler gives the callee's error to it, and goes on compiled where it sends the activation; but not after the program
            // waited, when the frame is what the debugger left it.
            if (_analysis.HandlesErrors)
            {
                _il.Emit(OpCodes.Br, RecoverAt(offset));
            }
            else
            {
                EmitSpill();
            }

            _il.MarkLabel(spilled);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, offset);
            _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.AfterFailedCall))!);
            _il.Emit(OpCodes.Ret);
        }
    }

    // every variable read back from its cell, which a debugger may have changed while the program waited - but one held in a cell, which compiled code
    // reads where it is.
    private void EmitReload()
    {
        foreach (var (slot, kind) in _analysis.Slots)
        {
            if (_cells.ContainsKey(slot))
            {
                continue;
            }

            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, slot);
            _il.Emit(OpCodes.Ldloca, _slots[slot]);
            _il.Emit(OpCodes.Call, Activation.GetMethod(ScalarKinds.IsIntegral(kind) ? nameof(CompiledActivation.TryReadIntegral) : nameof(CompiledActivation.TryReadFloating))!);
            _il.Emit(OpCodes.Pop);
        }

        // the function result variable too, which the spill before the call wrote.
        if (_result is { } result)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloca, result);
            _il.Emit(OpCodes.Call, Activation.GetMethod(ScalarKinds.IsIntegral(_analysis.ResultKind!.Value)
                ? nameof(CompiledActivation.TryReadResultIntegral)
                : nameof(CompiledActivation.TryReadResultFloating))!);
            _il.Emit(OpCodes.Pop);
        }
    }

    // the activation is handed to the interpreter between two instructions, before this one, when the program is to stop or a debugger is attached.
    private void EmitPoll(int offset)
    {
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, Activation.GetProperty(nameof(CompiledActivation.ShouldWalkBack))!.GetMethod!);
        _il.Emit(OpCodes.Brtrue, WalkBackAt(offset));
    }

    // MS-VBAL §5.4.2.3, as ExecuteForOpener does it: start, end and step evaluated once, in the counter's kind; the counter given the start; the body skipped
    // when the counter is past the end already - above it counting up, below it counting down.
    private void EmitForOpen(ForOpenStep open)
    {
        var failure = FailureAt(open.Offset);
        var loop = _loops[open.Loop];
        var start = EmitPayload(open.Start, failure);
        var end = EmitPayload(open.End, failure);
        var step = EmitPayload(open.Step, failure);
        Store(end, open.Kind, loop.End);
        Store(step, open.Kind, loop.Step);

        Load(step, open.Kind);
        LoadZero(open.Kind);
        _il.Emit(OpCodes.Clt);
        _il.Emit(OpCodes.Stloc, loop.CountsDown);

        Store(start, open.Kind, _slots[open.Counter]);
        _il.Emit(OpCodes.Ldc_I4_1);
        _il.Emit(OpCodes.Stloc, loop.Opened);

        EmitPastTheEnd(loop, open.Counter, _instructions[open.Past]);
    }

    // TryStepForUnboxed: the counter given its value plus the step - computed in the extended format for a floating-point counter - and the body run again
    // while it is not past the end.
    private void EmitForNext(ForNextStep next)
    {
        EmitPoll(next.Offset);
        var failure = FailureAt(next.Offset);
        var loop = _loops[next.Loop];
        var counter = _slots[next.Counter];
        if (ScalarKinds.IsIntegral(next.Kind))
        {
            var sum = _il.DeclareLocal(typeof(long));
            _il.Emit(OpCodes.Ldc_I4, (int)next.Kind);
            _il.Emit(OpCodes.Ldloc, counter);
            _il.Emit(OpCodes.Ldloc, loop.Step);
            _il.Emit(OpCodes.Ldloca, sum);
            _il.Emit(OpCodes.Call, Payloads.GetMethod(nameof(PayloadOperations.Add))!);
            _il.Emit(OpCodes.Brtrue, failure);
            _il.Emit(OpCodes.Ldloc, sum);
            _il.Emit(OpCodes.Stloc, counter);
        }
        else if (next.Kind is ScalarKind.Double or ScalarKind.Date)
        {
            var sum = EmitExactOperation(Tokens.AdditionOp, new OperandNode(new Operand(counter, 0, 0), next.Kind), new OperandNode(new Operand(loop.Step, 0, 0), next.Kind),
                next.Kind, failure);
            Store(sum, next.Kind, counter);
        }
        else
        {
            var sum = _il.DeclareLocal(typeof(double));
            _il.Emit(OpCodes.Ldloc, counter);
            _il.Emit(OpCodes.Call, typeof(Extended).GetMethod(nameof(Extended.OfFloating))!);
            _il.Emit(OpCodes.Ldloc, loop.Step);
            _il.Emit(OpCodes.Call, typeof(Extended).GetMethod(nameof(Extended.OfFloating))!);
            _il.Emit(OpCodes.Call, typeof(Extended).GetMethod(nameof(Extended.Add))!);
            _il.Emit(OpCodes.Ldc_I4, (int)next.Kind);
            _il.Emit(OpCodes.Ldloca, sum);
            _il.Emit(OpCodes.Call, typeof(Extended).GetMethod(nameof(Extended.ToFloating))!);
            _il.Emit(OpCodes.Brtrue, failure);
            _il.Emit(OpCodes.Ldloc, sum);
            _il.Emit(OpCodes.Stloc, counter);
        }

        // a loop that ends keeps its end and its step, as the interpreter's does: a Resume may go back into it, and its statement, run again, may fail.
        var exit = _il.DefineLabel();
        EmitPastTheEnd(loop, next.Counter, exit);
        _il.Emit(OpCodes.Br, _instructions[next.Body]);
        _il.MarkLabel(exit);
    }

    // goes to past when the counter is past the end: below it counting down, above it counting up.
    private void EmitPastTheEnd(LoopLocals loop, int counter, Label past)
    {
        var up = _il.DefineLabel();
        var within = _il.DefineLabel();
        _il.Emit(OpCodes.Ldloc, loop.CountsDown);
        _il.Emit(OpCodes.Brfalse, up);
        _il.Emit(OpCodes.Ldloc, _slots[counter]);
        _il.Emit(OpCodes.Ldloc, loop.End);
        _il.Emit(OpCodes.Blt, past);
        _il.Emit(OpCodes.Br, within);
        _il.MarkLabel(up);
        _il.Emit(OpCodes.Ldloc, _slots[counter]);
        _il.Emit(OpCodes.Ldloc, loop.End);
        _il.Emit(OpCodes.Bgt, past);
        _il.MarkLabel(within);
    }

    // a condition is true when it is not zero: a comparison is branched on as it is.
    private void EmitBranch(PayloadNode condition, bool negate, Label whenFalse, Label failure)
    {
        if (condition is CompareNode compare)
        {
            EmitCompare(compare, failure);
            _il.Emit(negate ? OpCodes.Brtrue : OpCodes.Brfalse, whenFalse);
            return;
        }

        var value = EmitPayload(condition, failure);
        Load(value, condition.Kind);
        if (ScalarKinds.IsIntegral(condition.Kind))
        {
            _il.Emit(negate ? OpCodes.Brtrue : OpCodes.Brfalse, whenFalse);
            return;
        }

        // a NaN is no zero: it is True.
        LoadZero(condition.Kind);
        if (negate)
        {
            _il.Emit(OpCodes.Bne_Un, whenFalse);
        }
        else
        {
            _il.Emit(OpCodes.Beq, whenFalse);
        }
    }

    private void EmitExit()
    {
        if (_result is { } result)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, result);
            _il.Emit(OpCodes.Call, Activation.GetMethod(ScalarKinds.IsIntegral(_analysis.ResultKind!.Value)
                ? nameof(CompiledActivation.AssignResultIntegral)
                : nameof(CompiledActivation.AssignResultFloating))!);
        }

        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.Return))!);
        _il.Emit(OpCodes.Ret);
    }

    // the label an operation of the instruction at offset goes to when it fails: the instruction is the interpreter's, from there on - or, in a body with an
    // error handler, the interpreter's alone, which gives its error to the handler, and the activation goes on compiled where the handler sends it.
    private Label FailureAt(int offset) => _analysis.HandlesErrors ? RecoverAt(offset) : WalkBackAt(offset);

    // the label the activation is handed to the interpreter at, before the instruction at offset, for good.
    private Label WalkBackAt(int offset)
    {
        if (!_failures.TryGetValue(offset, out var label))
        {
            label = _il.DefineLabel();
            _failures[offset] = label;
        }

        return label;
    }

    // the label the interpreter runs the instruction at offset alone at (CompiledActivation.Recover).
    private Label RecoverAt(int offset)
    {
        if (!_recoveries.TryGetValue(offset, out var label))
        {
            label = _il.DefineLabel();
            _recoveries[offset] = label;
        }

        (_recovers, _dispatches) = (true, true);
        return label;
    }

    private void EmitFailures()
    {
        foreach (var (offset, label) in _failures)
        {
            _il.MarkLabel(label);
            _il.Emit(OpCodes.Ldc_I4, offset);
            _il.Emit(OpCodes.Stloc, _pc);
            _il.Emit(OpCodes.Br, _walkBack);
        }

        foreach (var (offset, label) in _recoveries)
        {
            _il.MarkLabel(label);
            _il.Emit(OpCodes.Ldc_I4, offset);
            _il.Emit(OpCodes.Stloc, _pc);
            _il.Emit(OpCodes.Br, _recover);
        }
    }

    // every variable written into the frame, and the interpreter given the instruction at pc: where it leaves the activation, compiled code reads every
    // variable back and goes on; when it goes on nowhere, the activation returns what the interpreter's run of it did.
    private void EmitRecover()
    {
        if (!_recovers)
        {
            return;
        }

        _il.MarkLabel(_recover);
        EmitSpill();
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, _pc);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.Recover))!);
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Stloc, _pc);
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Bge, _reentry);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.Recovered))!);
        _il.Emit(OpCodes.Ret);

        // the re-entry: every variable and the function result, and the state of each loop - which an opener the interpreter ran gave it.
        _il.MarkLabel(_reentry);
        EmitReload();
        for (var i = 0; i < _loops.Count; i++)
        {
            var loop = _loops[i];
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, i);
            _il.Emit(OpCodes.Ldloca, loop.End);
            _il.Emit(OpCodes.Ldloca, loop.Step);
            _il.Emit(OpCodes.Ldloca, loop.CountsDown);
            _il.Emit(OpCodes.Call, Activation.GetMethod(ScalarKinds.IsIntegral(loop.Kind)
                ? nameof(CompiledActivation.ReloadForLoopIntegral)
                : nameof(CompiledActivation.ReloadForLoopFloating))!);
            _il.Emit(OpCodes.Stloc, loop.Opened);
        }

        _il.Emit(OpCodes.Br, _dispatch);
    }

    // the instruction at pc, found by its offset: where a Return or the interpreter left the activation. Every offset of the body has its instruction, and
    // the end of the body is an Exit.
    private void EmitDispatch()
    {
        if (!_dispatches)
        {
            return;
        }

        _il.MarkLabel(_dispatch);
        _il.Emit(OpCodes.Ldloc, _pc);
        _il.Emit(OpCodes.Switch, Enumerable.Range(0, _analysis.Length + 1).Select(offset => _instructions[offset]).ToArray());
        _il.Emit(OpCodes.Br, _walkBack);
    }

    // every variable written into its cell, the function result into its variable, each loop that was opened into the frame; then the interpreter runs the
    // activation from the instruction at pc.
    private void EmitWalkBack()
    {
        _il.MarkLabel(_walkBack);
        EmitSpill();
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, _pc);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.Interpret))!);
        _il.Emit(OpCodes.Ret);
    }

    // every variable written into its cell, the function result into its variable, each loop that was ever opened into the frame - with the end and the step
    // it last had, as the interpreter keeps them (ExecuteForOpener). A variable held in a cell is in it already, and the frame holds the cell
    // (CompiledActivation.TakeReference).
    private void EmitSpill()
    {
        foreach (var (slot, kind) in _analysis.Slots)
        {
            if (_cells.ContainsKey(slot))
            {
                continue;
            }

            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, slot);
            _il.Emit(OpCodes.Ldloc, _slots[slot]);
            _il.Emit(OpCodes.Call, Activation.GetMethod(ScalarKinds.IsIntegral(kind) ? nameof(CompiledActivation.SpillIntegral) : nameof(CompiledActivation.SpillFloating))!);
        }

        if (_result is { } result)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, result);
            _il.Emit(OpCodes.Call, Activation.GetMethod(ScalarKinds.IsIntegral(_analysis.ResultKind!.Value)
                ? nameof(CompiledActivation.AssignResultIntegral)
                : nameof(CompiledActivation.AssignResultFloating))!);
        }

        for (var i = 0; i < _loops.Count; i++)
        {
            var loop = _loops[i];
            var closed = _il.DefineLabel();
            _il.Emit(OpCodes.Ldloc, loop.Opened);
            _il.Emit(OpCodes.Brfalse, closed);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, i);
            LoadAsBoth(loop.End, loop.Kind);
            LoadAsBoth(loop.Step, loop.Kind);
            _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.SpillForLoop))!);
            _il.MarkLabel(closed);
        }
    }

    private void LoadAsBoth(Operand operand, ScalarKind kind)
    {
        if (ScalarKinds.IsIntegral(kind))
        {
            Load(operand, kind);
            _il.Emit(OpCodes.Ldc_R8, 0.0);
        }
        else
        {
            _il.Emit(OpCodes.Ldc_I8, 0L);
            Load(operand, kind);
        }
    }

    // a payload as the two arguments a support call takes: its long and its double, the other zero.
    private void LoadAsBoth(LocalBuilder local, ScalarKind kind)
    {
        if (ScalarKinds.IsIntegral(kind))
        {
            _il.Emit(OpCodes.Ldloc, local);
            _il.Emit(OpCodes.Ldc_R8, 0.0);
        }
        else
        {
            _il.Emit(OpCodes.Ldc_I8, 0L);
            _il.Emit(OpCodes.Ldloc, local);
        }
    }

    // where a payload is: a local, or a constant.
    private readonly record struct Operand(LocalBuilder? Local, long Integral, double Floating);

    private void Load(Operand operand, ScalarKind kind)
    {
        if (operand.Local is { } local)
        {
            _il.Emit(OpCodes.Ldloc, local);
        }
        else if (ScalarKinds.IsIntegral(kind))
        {
            _il.Emit(OpCodes.Ldc_I8, operand.Integral);
        }
        else
        {
            _il.Emit(OpCodes.Ldc_R8, operand.Floating);
        }
    }

    private void LoadZero(ScalarKind kind)
    {
        if (ScalarKinds.IsIntegral(kind))
        {
            _il.Emit(OpCodes.Ldc_I8, 0L);
        }
        else
        {
            _il.Emit(OpCodes.Ldc_R8, 0.0);
        }
    }

    private void Store(Operand operand, ScalarKind kind, LocalBuilder destination)
    {
        Load(operand, kind);
        _il.Emit(OpCodes.Stloc, destination);
    }

    // a variable of the activation assigned: its local, or the cell it is held in, which can't fail - the value is of its kind.
    private void StoreSlot(Operand operand, ScalarKind kind, int slot)
    {
        if (!_cells.TryGetValue(slot, out var cell))
        {
            Store(operand, kind, _slots[slot]);
            return;
        }

        _il.Emit(OpCodes.Ldloc, cell);
        Load(operand, kind);
        _il.Emit(OpCodes.Call, Activation.GetMethod(ScalarKinds.IsIntegral(kind) ? nameof(CompiledActivation.WriteIntegral) : nameof(CompiledActivation.WriteFloating))!);
    }

    // the argument of a call: a value - which is all a parameter passed by reference is given of one that is no variable, until the call is made directly
    // (EmitTemporaries) - or the cell of a variable passed by reference.
    private Operand EmitArgument(PayloadNode node, Label failure) => node switch
    {
        ReferenceNode reference => new Operand(_cells[reference.Slot], 0, 0),
        TemporaryNode temporary => EmitPayload(temporary.Value, failure),
        _ => EmitPayload(node, failure),
    };

    // the arguments of a call made directly: a parameter passed by reference that is given a value is given a cell of its own holding it, which is the
    // callee's alone (MS-VBAL §5.3.1.11). The invoker is given the value, and the interpreter makes the callee's cell of it, as for a call it makes.
    private Operand[] EmitTemporaries(CallStep call, Operand[] arguments)
    {
        var direct = (Operand[])arguments.Clone();
        for (var i = 0; i < direct.Length; i++)
        {
            if (call.Arguments[i] is not TemporaryNode temporary)
            {
                continue;
            }

            var cell = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Ldc_I4, (int)temporary.Kind);
            LoadAsBoth(arguments[i], temporary.Kind);
            _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.NewCell))!);
            _il.Emit(OpCodes.Stloc, cell);
            direct[i] = new Operand(cell, 0, 0);
        }

        return direct;
    }

    // evaluates a payload into a local - or names the local or the constant it is - leaving nothing on the stack, so that a failure branches from an empty one.
    private Operand EmitPayload(PayloadNode node, Label failure)
    {
        switch (node)
        {
            // a variable held in a cell is read where it is: a cell that holds no payload - what the interpreter wrote into it, of another representation - is
            // the interpreter's to read.
            case SlotNode slot when _cells.TryGetValue(slot.Slot, out var cell):
            {
                var read = _il.DeclareLocal(ClrTypeOf(slot.Kind));
                _il.Emit(OpCodes.Ldloc, cell);
                _il.Emit(OpCodes.Ldloca, read);
                _il.Emit(OpCodes.Call, Activation.GetMethod(ScalarKinds.IsIntegral(slot.Kind) ? nameof(CompiledActivation.ReadIntegral) : nameof(CompiledActivation.ReadFloating))!);
                _il.Emit(OpCodes.Brfalse, failure);
                return new Operand(read, 0, 0);
            }

            case SlotNode slot:
                return new Operand(_slots[slot.Slot], 0, 0);

            case OperandNode evaluated:
                return evaluated.Operand;

            case ResultNode:
                return new Operand(_result!, 0, 0);

            case ConstantNode constant:
                return new Operand(null, constant.Integral, constant.Floating);

            case ConvertNode convert:
            {
                var operand = EmitPayload(convert.Operand, failure);
                var integral = _il.DeclareLocal(typeof(long));
                var floating = _il.DeclareLocal(typeof(double));
                _il.Emit(OpCodes.Ldc_I4, (int)convert.Operand.Kind);
                if (ScalarKinds.IsIntegral(convert.Operand.Kind))
                {
                    Load(operand, convert.Operand.Kind);
                    _il.Emit(OpCodes.Ldc_R8, 0.0);
                }
                else
                {
                    _il.Emit(OpCodes.Ldc_I8, 0L);
                    Load(operand, convert.Operand.Kind);
                }

                _il.Emit(OpCodes.Ldc_I4, (int)convert.Kind);
                _il.Emit(OpCodes.Ldloca, integral);
                _il.Emit(OpCodes.Ldloca, floating);
                _il.Emit(OpCodes.Call, Payloads.GetMethod(nameof(PayloadOperations.Convert))!);
                _il.Emit(OpCodes.Brtrue, failure);
                return new Operand(ScalarKinds.IsIntegral(convert.Kind) ? integral : floating, 0, 0);
            }

            case IntegralNode integral:
            {
                var left = EmitPayload(integral.Left, failure);
                var right = EmitPayload(integral.Right, failure);
                var result = _il.DeclareLocal(typeof(long));
                _il.Emit(OpCodes.Ldc_I4, (int)integral.Kind);
                Load(left, integral.Left.Kind);
                Load(right, integral.Right.Kind);
                _il.Emit(OpCodes.Ldloca, result);
                _il.Emit(OpCodes.Call, Payloads.GetMethod(integral.Token switch
                {
                    Tokens.AdditionOp => nameof(PayloadOperations.Add),
                    Tokens.SubtractionOp => nameof(PayloadOperations.Subtract),
                    Tokens.MultiplicationOp => nameof(PayloadOperations.Multiply),
                    Tokens.IntegerDivisionOp => nameof(PayloadOperations.IntegerDivide),
                    _ => nameof(PayloadOperations.Modulo),
                })!);
                _il.Emit(OpCodes.Brtrue, failure);
                return new Operand(result, 0, 0);
            }

            case LogicalNode logical:
            {
                var left = EmitPayload(logical.Left, failure);
                var right = EmitPayload(logical.Right, failure);
                var result = _il.DeclareLocal(typeof(long));
                _il.Emit(OpCodes.Ldc_I4, (int)logical.Operator);
                _il.Emit(OpCodes.Ldc_I4, (int)logical.Kind);
                Load(left, logical.Left.Kind);
                Load(right, logical.Right.Kind);
                _il.Emit(OpCodes.Ldloca, result);
                _il.Emit(OpCodes.Call, Payloads.GetMethod(nameof(PayloadOperations.Logical))!);
                _il.Emit(OpCodes.Brtrue, failure);
                return new Operand(result, 0, 0);
            }

            case CompareNode compare:
                EmitCompare(compare, failure);
                return AsBoolean();

            case NotNode not:
            {
                var operand = EmitPayload(not.Operand, failure);
                var result = _il.DeclareLocal(typeof(long));
                _il.Emit(OpCodes.Ldc_I4, (int)not.Kind);
                Load(operand, not.Operand.Kind);
                _il.Emit(OpCodes.Ldloca, result);
                _il.Emit(OpCodes.Call, Payloads.GetMethod(nameof(PayloadOperations.Not))!);
                _il.Emit(OpCodes.Brtrue, failure);
                return new Operand(result, 0, 0);
            }

            case NegateNode negate:
            {
                var operand = EmitPayload(negate.Operand, failure);
                var type = ClrTypeOf(negate.Kind);
                var result = _il.DeclareLocal(type);
                _il.Emit(OpCodes.Ldc_I4, (int)negate.Kind);
                Load(operand, negate.Operand.Kind);
                _il.Emit(OpCodes.Ldloca, result);
                _il.Emit(OpCodes.Call, Payloads.GetMethod(nameof(PayloadOperations.Negate), [typeof(ScalarKind), type, type.MakeByRefType()])!);
                _il.Emit(OpCodes.Brtrue, failure);
                return new Operand(result, 0, 0);
            }

            case StoreNode { Value: ExtendedOperationNode { Left: WidenNode left, Right: WidenNode right } operation } store
                when IsExactlyDouble(left.Operand.Kind) && IsExactlyDouble(right.Operand.Kind) && store.Kind is ScalarKind.Double or ScalarKind.Date:
                return EmitExactOperation(operation.Token, left.Operand, right.Operand, store.Kind, failure);

            case StoreNode store:
            {
                var value = EmitExtended(store.Value, failure);
                var integral = ScalarKinds.IsIntegral(store.Kind);
                var result = _il.DeclareLocal(ClrTypeOf(store.Kind));
                _il.Emit(OpCodes.Ldloc, value);
                _il.Emit(OpCodes.Ldc_I4, (int)store.Kind);
                _il.Emit(OpCodes.Ldloca, result);
                _il.Emit(OpCodes.Call, typeof(Extended).GetMethod(integral ? nameof(Compiled.Extended.ToIntegral) : nameof(Compiled.Extended.ToFloating))!);
                _il.Emit(OpCodes.Brtrue, failure);
                return new Operand(result, 0, 0);
            }

            case ExtendedCompareNode compare:
            {
                var left = EmitExtended(compare.Left, failure);
                var right = EmitExtended(compare.Right, failure);
                var isTrue = _il.DeclareLocal(typeof(bool));
                _il.Emit(OpCodes.Ldc_I4, (int)compare.Operator);
                _il.Emit(OpCodes.Ldloc, left);
                _il.Emit(OpCodes.Ldloc, right);
                _il.Emit(OpCodes.Ldc_I4, (int)compare.Effective);
                _il.Emit(OpCodes.Ldloca, isTrue);
                _il.Emit(OpCodes.Call, typeof(Extended).GetMethod(nameof(Compiled.Extended.Compare))!);
                _il.Emit(OpCodes.Brtrue, failure);
                _il.Emit(OpCodes.Ldloc, isTrue);
                return AsBoolean();
            }

            default:
                throw new InvalidOperationException($"No IL for {node.GetType().Name}.");
        }
    }

    // a payload of a kind whose every value is a Double: what an extended operation of two of them is computed in Double for, when its result is exact.
    private static bool IsExactlyDouble(ScalarKind kind) => kind is not (ScalarKind.LongLong or ScalarKind.Currency);

    // one operation of two payloads stored as a Double or a Date: computed in Double when its result is exact (Extended.TryAddExactly), which is what the
    // extended format stores; in the extended format otherwise, from the same operands.
    private Operand EmitExactOperation(string token, PayloadNode left, PayloadNode right, ScalarKind kind, Label failure)
    {
        var l = EmitPayload(left, failure);
        var r = EmitPayload(right, failure);
        var result = _il.DeclareLocal(typeof(double));
        var extended = _il.DefineLabel();
        var done = _il.DefineLabel();

        LoadAsDouble(l, left.Kind);
        LoadAsDouble(r, right.Kind);
        _il.Emit(OpCodes.Ldloca, result);
        _il.Emit(OpCodes.Call, typeof(Extended).GetMethod(token switch
        {
            Tokens.AdditionOp => nameof(Compiled.Extended.TryAddExactly),
            Tokens.SubtractionOp => nameof(Compiled.Extended.TrySubtractExactly),
            Tokens.MultiplicationOp => nameof(Compiled.Extended.TryMultiplyExactly),
            _ => nameof(Compiled.Extended.TryDivideExactly),
        })!);
        _il.Emit(OpCodes.Brfalse, extended);
        if (kind is ScalarKind.Date)
        {
            // a Date that is no date's serial is the extended evaluation's to raise.
            _il.Emit(OpCodes.Ldc_I4, (int)ScalarKind.Date);
            _il.Emit(OpCodes.Ldloc, result);
            _il.Emit(OpCodes.Ldloca, result);
            _il.Emit(OpCodes.Call, Payloads.GetMethod(nameof(PayloadOperations.Floating))!);
            _il.Emit(OpCodes.Brtrue, extended);
        }

        _il.Emit(OpCodes.Br, done);

        _il.MarkLabel(extended);
        var value = EmitExtended(new ExtendedOperationNode(token, new WidenNode(new OperandNode(l, left.Kind)), new WidenNode(new OperandNode(r, right.Kind))), failure);
        _il.Emit(OpCodes.Ldloc, value);
        _il.Emit(OpCodes.Ldc_I4, (int)kind);
        _il.Emit(OpCodes.Ldloca, result);
        _il.Emit(OpCodes.Call, typeof(Extended).GetMethod(nameof(Compiled.Extended.ToFloating))!);
        _il.Emit(OpCodes.Brtrue, failure);

        _il.MarkLabel(done);
        return new Operand(result, 0, 0);
    }

    // a payload already evaluated, where it is: what an operation computed a second way is given again rather than evaluated again.
    private sealed record class OperandNode(Operand Operand, ScalarKind Kind) : PayloadNode(Kind);

    private void LoadAsDouble(Operand operand, ScalarKind kind)
    {
        Load(operand, kind);
        if (ScalarKinds.IsIntegral(kind))
        {
            _il.Emit(OpCodes.Conv_R8);
        }
    }

    // a comparison's bool on the stack, made the -1 or 0 of a Boolean.
    private Operand AsBoolean()
    {
        var result = _il.DeclareLocal(typeof(long));
        _il.Emit(OpCodes.Neg);
        _il.Emit(OpCodes.Conv_I8);
        _il.Emit(OpCodes.Stloc, result);
        return new Operand(result, 0, 0);
    }

    // a comparison of two payloads of one kind, its bool left on the stack.
    private void EmitCompare(CompareNode compare, Label failure)
    {
        var left = EmitPayload(compare.Left, failure);
        var right = EmitPayload(compare.Right, failure);
        var type = ClrTypeOf(compare.Left.Kind);
        _il.Emit(OpCodes.Ldc_I4, (int)compare.Operator);
        Load(left, compare.Left.Kind);
        Load(right, compare.Right.Kind);
        _il.Emit(OpCodes.Call, Payloads.GetMethod(nameof(PayloadOperations.Compare), [typeof(ComparisonOperator), type, type])!);
    }

    // evaluates an extended intermediate into a local.
    private LocalBuilder EmitExtended(ExtendedNode node, Label failure)
    {
        var result = _il.DeclareLocal(typeof(Extended));
        switch (node)
        {
            case WidenNode widen:
            {
                var operand = EmitPayload(widen.Operand, failure);
                if (ScalarKinds.IsIntegral(widen.Operand.Kind))
                {
                    _il.Emit(OpCodes.Ldc_I4, (int)widen.Operand.Kind);
                    Load(operand, widen.Operand.Kind);
                    _il.Emit(OpCodes.Call, typeof(Extended).GetMethod(nameof(Compiled.Extended.OfIntegral))!);
                }
                else
                {
                    Load(operand, widen.Operand.Kind);
                    _il.Emit(OpCodes.Call, typeof(Extended).GetMethod(nameof(Compiled.Extended.OfFloating))!);
                }

                break;
            }

            case ExtendedOperationNode operation:
            {
                var left = EmitExtended(operation.Left, failure);
                var right = EmitExtended(operation.Right, failure);
                _il.Emit(OpCodes.Ldloc, left);
                _il.Emit(OpCodes.Ldloc, right);
                _il.Emit(OpCodes.Call, typeof(Extended).GetMethod(operation.Token switch
                {
                    Tokens.AdditionOp => nameof(Compiled.Extended.Add),
                    Tokens.SubtractionOp => nameof(Compiled.Extended.Subtract),
                    Tokens.MultiplicationOp => nameof(Compiled.Extended.Multiply),
                    _ => nameof(Compiled.Extended.Divide),
                })!);
                break;
            }

            case ExtendedNegateNode negate:
            {
                var operand = EmitExtended(negate.Operand, failure);
                _il.Emit(OpCodes.Ldloc, operand);
                _il.Emit(OpCodes.Call, typeof(Extended).GetMethod(nameof(Compiled.Extended.Negate))!);
                break;
            }
        }

        _il.Emit(OpCodes.Stloc, result);
        return result;
    }
}
