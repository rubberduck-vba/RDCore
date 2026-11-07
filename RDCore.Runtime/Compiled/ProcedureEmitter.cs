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
internal sealed partial class ProcedureEmitter
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

    // the Variant slots, each by the local the cell its frame holds it in is held in.
    private readonly Dictionary<int, LocalBuilder> _variants = [];
    private static readonly Type Variants = typeof(VariantOperations);

    // the object slots, each by the local the cell its frame holds it in is held in.
    private readonly Dictionary<int, LocalBuilder> _objects = [];
    private static readonly Type Objects = typeof(ObjectOperations);

    // each loop of a Variant counter whose subtype is speculated, by its For statement: what its Next is stepped with in that subtype, fetched by the
    // first Next after compiled code took the activation up (State 0; 1 once fetched, -1 when each Next is stepped as any is).
    private readonly Dictionary<int, VariantLoopLocals> _variantLoops = [];

    private sealed record class VariantLoopLocals(ScalarKind Kind, LocalBuilder State, LocalBuilder Step, LocalBuilder End, LocalBuilder CountsDown);
    private readonly Dictionary<int, Label> _instructions = [];
    private readonly Dictionary<int, Label> _failures = [];
    private readonly Dictionary<int, Label> _recoveries = [];
    private readonly List<LoopLocals> _loops = [];
    private readonly LocalBuilder _pc;
    private readonly Label _walkBack;

    // the instruction at pc run by the interpreter alone, and the activation gone on with where it left it (CompiledActivation.Recover): every variable
    // written into the frame first; read back after it (the re-entry); and the instruction the activation goes on at found by its offset (the dispatch).
    private readonly Label _recover;
    private readonly Label _recovered;
    private readonly Label _reentry;
    private readonly Label _dispatch;
    private bool _recovers;
    private bool _dispatches;
    private LocalBuilder? _result;

    // a variable not read back from the frame (EmitReload): the interpreter runs the activation from pc, as the frame has it.
    private readonly Label _unread;
    private bool _unreads;

    // Opened: whether the loop's statement ever ran, and gave it an end and a step - kept when the loop ends, and when its statement, run again, fails.
    // Spilled: whether the frame has the end and the step the statement last gave it, which nothing but the statement changes: written once, not at each
    // instruction the interpreter runs alone.
    private sealed record class LoopLocals(LocalBuilder End, LocalBuilder Step, LocalBuilder CountsDown, LocalBuilder Opened, LocalBuilder Spilled, ScalarKind Kind);

    // a Select Case's selector, and the same two flags: whether its Select ever ran, and whether the frame has what it last gave the selector.
    private sealed record class SelectLocals(LocalBuilder Selector, LocalBuilder Opened, LocalBuilder Spilled, ScalarKind Kind);

    private readonly List<SelectLocals> _selects = [];

    private ProcedureEmitter(ProcedureAnalysis analysis, IReadOnlyList<DirectCall?> directs, ILGenerator il)
    {
        (_analysis, _directs, _il) = (analysis, directs, il);
        _pc = il.DeclareLocal(typeof(int));
        (_walkBack, _unread) = (il.DefineLabel(), il.DefineLabel());
        (_recover, _recovered, _reentry, _dispatch) = (il.DefineLabel(), il.DefineLabel(), il.DefineLabel(), il.DefineLabel());
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

        foreach (var slot in _analysis.VariantSlots)
        {
            _variants[slot] = _il.DeclareLocal(typeof(object));
        }

        foreach (var slot in _analysis.ObjectSlots)
        {
            _objects[slot] = _il.DeclareLocal(typeof(object));
        }

        DeclareTextCells();

        foreach (var next in _analysis.Steps.OfType<VariantForNextStep>())
        {
            if (_analysis.SpeculatedKinds.TryGetValue(next.Counter, out var counting) && !_variantLoops.ContainsKey(next.Opener))
            {
                var clr = ClrTypeOf(counting);
                _variantLoops[next.Opener] = new VariantLoopLocals(counting, _il.DeclareLocal(typeof(int)), _il.DeclareLocal(clr), _il.DeclareLocal(clr),
                    _il.DeclareLocal(typeof(bool)));
            }
        }

        if (_analysis.ResultKind is { } resultKind)
        {
            _result = _il.DeclareLocal(ClrTypeOf(resultKind));
        }

        foreach (var loop in _analysis.Loops)
        {
            _loops.Add(new LoopLocals(_il.DeclareLocal(ClrTypeOf(loop.Kind)), _il.DeclareLocal(ClrTypeOf(loop.Kind)), _il.DeclareLocal(typeof(bool)),
                _il.DeclareLocal(typeof(bool)), _il.DeclareLocal(typeof(bool)), loop.Kind));
        }

        foreach (var select in _analysis.Selects)
        {
            _selects.Add(new SelectLocals(_il.DeclareLocal(ClrTypeOf(select.Kind)), _il.DeclareLocal(typeof(bool)), _il.DeclareLocal(typeof(bool)), select.Kind));
        }

        for (var offset = 0; offset <= _analysis.Length; offset++)
        {
            _instructions[offset] = _il.DefineLabel();
        }

        EmitEntry();
        foreach (var step in _analysis.Steps)
        {
            _il.MarkLabel(_instructions[step.Offset]);

            // a jump out of a For Each lands here: what the block held is let go of before the instruction, as the interpreter does.
            if (_analysis.BlockExits.Contains(step.Offset))
            {
                EmitLeaveBlocks(() => _il.Emit(OpCodes.Ldc_I4, step.Offset));
            }

            // every instruction boundary is a safepoint: under the random walk-back the activation may be handed to the interpreter at any of them, and not only
            // where it polls for a program to stop - which a step that polls already does, before it.
            if (CompiledExecution.WalksBackAtRandom && step is not (LoopBackStep or ForNextStep or VariantForNextStep) && !(step is JumpStep jump && jump.Target <= jump.Offset))
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
        EmitUnread();
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
            // a field of Me is in the cell the object holds it in: one held otherwise makes the activation the interpreter's from the start.
            if (_analysis.FieldSlots.Contains(slot))
            {
                EmitFieldCell(slot, WalkBackAt(0), nameof(CompiledActivation.FieldCellOf));
            }
            else if (!_analysis.Parameters.Exists(parameter => parameter.Slot == slot))
            {
                var kind = _analysis.Slots[slot];
                EmitCellOf(slot, kind, _cells[slot], () => LoadZero(kind));
            }
        }

        // each Variant's cell, as the frame holds it: none, for one held otherwise, which compiled code then reads and writes nothing of - each statement
        // that does is the interpreter's.
        foreach (var (slot, cell) in _variants)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, slot);
            _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.VariantCellOf))!);
            _il.Emit(OpCodes.Stloc, cell);
        }

        // each object's cell, as each Variant's is.
        foreach (var (slot, cell) in _objects)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, slot);
            _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.ObjectCellOf))!);
            _il.Emit(OpCodes.Stloc, cell);
        }

        EmitTextCells();
    }

    // the cell of a field of Me, found by the activation's object; none goes to orElse.
    private void EmitFieldCell(int slot, Label orElse, string find)
    {
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, _cellNumbers[slot]);
        _il.Emit(OpCodes.Call, Activation.GetMethod(find)!);
        _il.Emit(OpCodes.Stloc, _cells[slot]);
        _il.Emit(OpCodes.Ldloc, _cells[slot]);
        _il.Emit(OpCodes.Brfalse, orElse);
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
                Store(EmitPayload(let.Value, FailureOf(let.Value, let.Offset)), let.Value.Kind, _result!);
                break;

            case LetStep let:
                StoreSlot(EmitPayload(let.Value, FailureOf(let.Value, let.Offset)), let.Value.Kind, let.Slot);
                break;

            case BranchStep branch:
                // true falls through into the branch, false goes to its Else.
                EmitBranch(branch.Condition, branch.Negate, whenFalse: _instructions[branch.Else], FailureOf(branch.Condition, branch.Offset));
                break;

            case LoopBackStep loopBack:
                EmitPoll(loopBack.Offset);
                var past = _il.DefineLabel();
                EmitBranch(loopBack.Condition, loopBack.Negate, whenFalse: past, FailureOf(loopBack.Condition, loopBack.Offset));
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
                _il.Emit(OpCodes.Brfalse, RecoverAt(returnStep.Offset));

                // a Return out of a For Each lets go of what it held, as a jump out of it does.
                if (_analysis.HasForEach)
                {
                    EmitLeaveBlocks(() => _il.Emit(OpCodes.Ldloc, _pc));
                }

                _il.Emit(OpCodes.Br, _dispatch);
                break;

            case SteppedStep stepped:
                EmitStepped(stepped.Offset);
                break;

            case SelectStep select:
            {
                var locals = _selects[select.Select];
                Store(EmitPayload(select.Selector, FailureAt(select.Offset)), select.Selector.Kind, locals.Selector);
                _il.Emit(OpCodes.Ldc_I4_1);
                _il.Emit(OpCodes.Stloc, locals.Opened);
                _il.Emit(OpCodes.Ldc_I4_0);
                _il.Emit(OpCodes.Stloc, locals.Spilled);
                break;
            }

            case CaseStep caseStep:
                EmitCase(caseStep);
                break;

            // what the Variant holds decides what its Let is: one the evaluation on payloads declines - of a String, over an object - the interpreter runs
            // alone, and compiled code goes on after it.
            case VariantLetStep variantLet:
            {
                var failure = RecoverAt(variantLet.Offset);

                // a value evaluated in the subtype it is speculated to be of is written as a payload of it; one that is not, as any is.
                Label? assigned = null;
                if (variantLet.Value is not VariantSlotNode && SpeculatedKindOf(variantLet.Value) is { } speculated)
                {
                    var slow = _il.DefineLabel();
                    var typed = EmitSpeculated(variantLet.Value, speculated, slow);
                    _il.Emit(OpCodes.Ldloc, _variants[variantLet.Slot]);
                    _il.Emit(OpCodes.Ldc_I4, (int)speculated);
                    _il.Emit(OpCodes.Ldloc, typed);
                    _il.Emit(OpCodes.Call, Variants.GetMethod(ScalarKinds.IsIntegral(speculated) ? nameof(VariantOperations.WriteIntegralAs) : nameof(VariantOperations.WriteFloatingAs))!);
                    _il.Emit(OpCodes.Brfalse, slow);
                    assigned = _il.DefineLabel();
                    _il.Emit(OpCodes.Br, assigned.Value);
                    _il.MarkLabel(slow);
                }

                var value = _il.DeclareLocal(typeof(VariantPayload));
                EmitVariantInto(variantLet.Value, value, failure);
                _il.Emit(OpCodes.Ldloc, _variants[variantLet.Slot]);
                _il.Emit(OpCodes.Ldloc, value);
                _il.Emit(OpCodes.Call, Variants.GetMethod(nameof(VariantOperations.Write))!);
                _il.Emit(OpCodes.Brfalse, failure);
                if (assigned is { } written)
                {
                    _il.MarkLabel(written);
                }

                break;
            }

            case VariantForNextStep variantNext:
                EmitVariantForNext(variantNext);
                break;

            case TextLetStep textLet:
                EmitTextLet(textLet);
                break;
        }
    }

    // the Next of a loop of a Variant counter, stepped with the state the frame holds (CompiledActivation.StepVariantFor): the body again, or past the loop;
    // a step it declines, the interpreter runs alone.
    private void EmitVariantForNext(VariantForNextStep next)
    {
        EmitPoll(next.Offset);
        if (_variantLoops.TryGetValue(next.Opener, out var loop))
        {
            EmitSpeculatedForNext(next, loop);
        }

        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, next.Opener);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.StepVariantFor))!);
        _il.Emit(OpCodes.Switch, new[] { _instructions[next.Body], _instructions[next.Offset + 1] });
        EmitStepped(next.Offset);
    }

    // the Next of a loop of a Variant counter whose subtype is speculated, while the counter holds it: counter + step in that subtype, written into the
    // counter's cell, and compared with the end - TryStepVariantFor's plans, made when the step and the end were fetched (CompiledActivation.VariantForBounds).
    // A counter of another subtype, a sum that overflows it, a loop not stepped in it, fall through into the Next as any is stepped.
    private void EmitSpeculatedForNext(VariantForNextStep next, VariantLoopLocals loop)
    {
        var generic = _il.DefineLabel();
        var fetched = _il.DefineLabel();
        var cell = _variants[next.Counter];
        var integral = ScalarKinds.IsIntegral(loop.Kind);

        _il.Emit(OpCodes.Ldloc, loop.State);
        _il.Emit(OpCodes.Ldc_I4_1);
        _il.Emit(OpCodes.Beq, fetched);
        _il.Emit(OpCodes.Ldloc, loop.State);
        _il.Emit(OpCodes.Brtrue, generic);

        var unusedIntegral = _il.DeclareLocal(typeof(long));
        var unusedFloating = _il.DeclareLocal(typeof(double));
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, next.Opener);
        _il.Emit(OpCodes.Ldloc, cell);
        _il.Emit(OpCodes.Ldc_I4, (int)loop.Kind);
        _il.Emit(OpCodes.Ldloca, integral ? loop.Step : unusedIntegral);
        _il.Emit(OpCodes.Ldloca, integral ? unusedFloating : loop.Step);
        _il.Emit(OpCodes.Ldloca, integral ? loop.End : unusedIntegral);
        _il.Emit(OpCodes.Ldloca, integral ? unusedFloating : loop.End);
        _il.Emit(OpCodes.Ldloca, loop.CountsDown);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.VariantForBounds))!);
        _il.Emit(OpCodes.Stloc, loop.State);
        _il.Emit(OpCodes.Ldloc, loop.State);
        _il.Emit(OpCodes.Ldc_I4_1);
        _il.Emit(OpCodes.Bne_Un, generic);

        _il.MarkLabel(fetched);
        var counter = _il.DeclareLocal(ClrTypeOf(loop.Kind));
        var sum = _il.DeclareLocal(ClrTypeOf(loop.Kind));
        _il.Emit(OpCodes.Ldloc, cell);
        _il.Emit(OpCodes.Ldc_I4, (int)loop.Kind);
        _il.Emit(OpCodes.Ldloca, counter);
        _il.Emit(OpCodes.Call, Variants.GetMethod(integral ? nameof(VariantOperations.ReadIntegralAs) : nameof(VariantOperations.ReadFloatingAs))!);
        _il.Emit(OpCodes.Brfalse, generic);
        EmitPlannedOperation(Tokens.AdditionOp, loop.Kind, counter, loop.Step, sum, generic);
        _il.Emit(OpCodes.Ldloc, cell);
        _il.Emit(OpCodes.Ldc_I4, (int)loop.Kind);
        _il.Emit(OpCodes.Ldloc, sum);
        _il.Emit(OpCodes.Call, Variants.GetMethod(integral ? nameof(VariantOperations.WriteIntegralAs) : nameof(VariantOperations.WriteFloatingAs))!);
        _il.Emit(OpCodes.Brfalse, generic);

        // past the end: below it when the loop counts down (MS-VBAL §5.4.2.3).
        var down = _il.DefineLabel();
        _il.Emit(OpCodes.Ldloc, loop.CountsDown);
        _il.Emit(OpCodes.Brtrue, down);
        EmitPlannedComparison(ComparisonOperator.Greater, loop.Kind, sum, loop.End);
        _il.Emit(OpCodes.Brtrue, _instructions[next.Offset + 1]);
        _il.Emit(OpCodes.Br, _instructions[next.Body]);
        _il.MarkLabel(down);
        EmitPlannedComparison(ComparisonOperator.Less, loop.Kind, sum, loop.End);
        _il.Emit(OpCodes.Brtrue, _instructions[next.Offset + 1]);
        _il.Emit(OpCodes.Br, _instructions[next.Body]);
        _il.MarkLabel(generic);
    }

    // an arithmetic operation of two payloads of the subtype it is done in, as TryOperate does it: the payload operation of an integral kind; a Double's
    // result checked as finite (PayloadOperations.Floating). One that overflows goes to slow.
    private void EmitPlannedOperation(string token, ScalarKind kind, LocalBuilder left, LocalBuilder right, LocalBuilder result, Label slow)
    {
        _il.Emit(OpCodes.Ldc_I4, (int)kind);
        _il.Emit(OpCodes.Ldloc, left);
        _il.Emit(OpCodes.Ldloc, right);
        if (ScalarKinds.IsIntegral(kind))
        {
            _il.Emit(OpCodes.Ldloca, result);
            _il.Emit(OpCodes.Call, Payloads.GetMethod(token switch
            {
                Tokens.AdditionOp => nameof(PayloadOperations.Add),
                Tokens.SubtractionOp => nameof(PayloadOperations.Subtract),
                _ => nameof(PayloadOperations.Multiply),
            })!);
        }
        else
        {
            _il.Emit(token switch
            {
                Tokens.AdditionOp => OpCodes.Add,
                Tokens.SubtractionOp => OpCodes.Sub,
                _ => OpCodes.Mul,
            });
            _il.Emit(OpCodes.Ldloca, result);
            _il.Emit(OpCodes.Call, Payloads.GetMethod(nameof(PayloadOperations.Floating))!);
        }

        _il.Emit(OpCodes.Brtrue, slow);
    }

    // a comparison of two payloads of the subtype they are compared in, as TryCompare does it: whether it holds, on the stack.
    private void EmitPlannedComparison(ComparisonOperator comparison, ScalarKind kind, LocalBuilder left, LocalBuilder right)
    {
        _il.Emit(OpCodes.Ldc_I4, (int)comparison);
        _il.Emit(OpCodes.Ldloc, left);
        _il.Emit(OpCodes.Ldloc, right);
        _il.Emit(OpCodes.Call, Payloads.GetMethod(nameof(PayloadOperations.Compare), [typeof(ComparisonOperator), ClrTypeOf(kind), ClrTypeOf(kind)])!);
    }

    private void EmitLeaveBlocks(Action loadPc)
    {
        _il.Emit(OpCodes.Ldarg_0);
        loadPc();
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.LeaveBlocks))!);
    }

    // CaseMatchEvaluator.TryMatchUnboxed: the first clause that matches falls through into the Case's block - the two comparisons of a To range both made -
    // and none goes to the next Case. A comparison that fails fails the whole Case, which the interpreter matches from its first clause.
    private void EmitCase(CaseStep caseStep)
    {
        var failure = FailureAt(caseStep.Offset);
        var matched = _il.DefineLabel();
        foreach (var clause in caseStep.Clauses)
        {
            EmitCompare(clause.Lower, failure);
            if (clause.Upper is { } upper)
            {
                EmitCompare(upper, failure);
                _il.Emit(OpCodes.And);
            }

            _il.Emit(OpCodes.Brtrue, matched);
        }

        _il.Emit(OpCodes.Br, _instructions[caseStep.Else]);
        _il.MarkLabel(matched);
    }

    // the instruction at offset run by the interpreter alone (CompiledActivation.Recover). When it falls through to the next instruction, nothing changed
    // the state of a loop of compiled code - only its statement does, and a Next, which jumps - and only the variables and the function result are read
    // back; anywhere else, the activation goes on as it does after any recovery (EmitRecover).
    private void EmitStepped(int offset)
    {
        _ = RecoverAt(offset);
        EmitSpill();
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, offset);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.Recover))!);
        _il.Emit(OpCodes.Stloc, _pc);
        _il.Emit(OpCodes.Ldloc, _pc);
        _il.Emit(OpCodes.Ldc_I4, offset + 1);
        _il.Emit(OpCodes.Bne_Un, _recovered);
        EmitReload(_unread);
        _il.Emit(OpCodes.Br, _instructions[offset + 1]);
    }

    // the arguments evaluated - where one fails, nothing was called, and the interpreter makes the instruction from its start - then the call. Under a
    // debugger the activation's variables and its function result are written into its frame before it, which a debugger shows while the program waits in
    // the callee; when the program waited, what the debugger may have changed of them is read back after it. A call that does not return - an error, the
    // program stopped, a callee compiled code can't call - is the interpreter's at the call (CompiledActivation.AfterFailedCall). After a call returns, the
    // activation is the interpreter's at the next instruction when the interpreter is wanted, or the symbols it was compiled against were defined again.
    private void EmitCall(CallStep call)
    {
        // the arguments, each held where it is - a variable, a constant - or in a local of its own; one that fails fails the instruction before anything is called.
        // A String compiled code reads nothing of - one that ends with a lone byte, or is held in no cell of its own - is the interpreter's: the instruction
        // is run by it alone, as a Let of one is.
        var failure = FailureAt(call.Offset);
        var arguments = new Operand[call.Arguments.Count];
        for (var i = 0; i < arguments.Length; i++)
        {
            arguments[i] = EmitArgument(call.Arguments[i], call.Arguments[i] is TextArgumentNode or TextReferenceNode ? RecoverAt(call.Offset) : failure);
        }

        // a member of an object: the object the variable refers to, and the member its class invokes, found before anything is called.
        if (call.Receiver is { } receiver)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, call.Callee);
            _il.Emit(OpCodes.Ldloc, _objects[receiver]);
            _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.CanCallMember))!);
            _il.Emit(OpCodes.Brfalse, failure);
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
        // way (CompiledActivation.EnterDirectly); through the invoker otherwise. A member of an object is the object's class's to say: its typed entry is the
        // one EnterMemberDirectly entered, called through its pointer, of the signature the member it was bound to has.
        var direct = _directs[call.Callee];
        if (direct is not null || call.Receiver is not null)
        {
            var indirect = _il.DefineLabel();
            var callee = _il.DeclareLocal(typeof(CompiledActivation));
            var outcome = _il.DeclareLocal(typeof(RuntimeExecutionOutcome));
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, call.Offset);
            _il.Emit(OpCodes.Ldc_I4, call.Callee);
            _il.Emit(OpCodes.Call, Activation.GetMethod(direct is null ? nameof(CompiledActivation.EnterMemberDirectly) : nameof(CompiledActivation.EnterDirectly))!);
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

            if (direct is not null)
            {
                _il.Emit(OpCodes.Call, direct.Entry);
            }
            else
            {
                var member = _analysis.Callees[call.Callee];
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Call, Activation.GetProperty(nameof(CompiledActivation.MemberEntry))!.GetMethod!);
                _il.EmitCalli(OpCodes.Calli, CallingConventions.Standard, typeof(RuntimeExecutionOutcome),
                    [typeof(CompiledActivation), .. member.Parameters.Select((kind, i) => member.ByReference[i] ? typeof(object) : ClrTypeOf(kind!.Value))], null);
            }

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

            // a String: the address of the variable given, or the text given.
            if (call.Arguments[i] is TextReferenceNode reference)
            {
                _il.Emit(OpCodes.Ldc_I4, reference.Slot);
                _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.SetTextReference))!);
                _il.Emit(OpCodes.Brfalse, RecoverAt(call.Offset));
                continue;
            }

            if (call.Arguments[i] is TextArgumentNode)
            {
                _il.Emit(OpCodes.Ldloc, arguments[i].Local!);
                _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.SetText))!);
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
        _il.Emit(OpCodes.Call, Activation.GetMethod(call.Receiver is null ? nameof(CompiledActivation.Call) : nameof(CompiledActivation.CallMember))!);
        _il.Emit(OpCodes.Brfalse, CallFailedAt(call.Offset));
        _il.MarkLabel(returned);

        // after a wait, a variable compiled code cannot read back (EmitReload): the call's result written into the frame, and the activation the interpreter's
        // from after the call.
        var current = _il.DefineLabel();
        var unread = _il.DefineLabel();
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, Activation.GetProperty(nameof(CompiledActivation.ReadsBackAfterCall))!.GetMethod!);
        _il.Emit(OpCodes.Brfalse, current);
        EmitReload(unread);
        _il.Emit(OpCodes.Br, current);
        _il.MarkLabel(unread);
        if (call.Result != CallStep.NoResult)
        {
            var kind = _analysis.Callees[call.Callee].Result!.Value;
            _il.Emit(OpCodes.Ldarg_0);
            if (call.Result >= 0)
            {
                _il.Emit(OpCodes.Ldc_I4, call.Result);
            }

            _il.Emit(OpCodes.Ldloc, ScalarKinds.IsIntegral(kind) ? integral : floating);
            _il.Emit(OpCodes.Call, Activation.GetMethod((call.Result >= 0, ScalarKinds.IsIntegral(kind)) switch
            {
                (true, true) => nameof(CompiledActivation.SpillIntegral),
                (true, false) => nameof(CompiledActivation.SpillFloating),
                (false, true) => nameof(CompiledActivation.AssignResultIntegral),
                (false, false) => nameof(CompiledActivation.AssignResultFloating),
            })!);
        }

        _il.Emit(OpCodes.Ldc_I4, call.Offset + 1);
        _il.Emit(OpCodes.Stloc, _pc);
        _il.Emit(OpCodes.Br, _unread);
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
    // reads where it is. A debugger's write at a variable's address is written into its cell (SessionStorage.TryPoke), which compiled code reads here. One
    // whose cell holds no payload it knows goes to unread, the frame left as it is: what the interpreter reads from there on.
    private void EmitReload(Label unread)
    {
        _unreads = true;
        foreach (var (slot, kind) in _analysis.Slots)
        {
            if (_cells.ContainsKey(slot))
            {
                continue;
            }

            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, slot);
            _il.Emit(OpCodes.Ldloca, _slots[slot]);
            _il.Emit(OpCodes.Call, Activation.GetMethod(ScalarKinds.IsIntegral(kind) ? nameof(CompiledActivation.TryReloadIntegral) : nameof(CompiledActivation.TryReloadFloating))!);
            _il.Emit(OpCodes.Brfalse, unread);
        }

        // what each loop of a Variant counter is stepped with, fetched again by its next Next: the interpreter may have run its For statement.
        foreach (var loop in _variantLoops.Values)
        {
            _il.Emit(OpCodes.Ldc_I4_0);
            _il.Emit(OpCodes.Stloc, loop.State);
        }

        // each field's cell, found again (FieldCellOf): the body's cache gives the cell of the same object as it is, and finds it again on the object when a
        // callee ran the body on another object meanwhile, and the cache is that object's. An edit made while the program waited hands the activation to the
        // interpreter, so no edit is read here.
        foreach (var slot in _analysis.FieldSlots)
        {
            EmitFieldCell(slot, unread, nameof(CompiledActivation.ReloadFieldCell));
        }

        // each Variant's cell, found again. Nothing the interpreter runs binds a Variant's slot to another cell: a ReDim keeps the Variant in its cell, and
        // an edit of the procedure while it waits leaves the rest of its activation to the interpreter. So this finds the same cell. It is the reload's own
        // read: one that fails (FailsAccessAtRandom) goes to unread like any other, and a statement that came to rebind a slot would be read here.
        foreach (var (slot, cell) in _variants)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, slot);
            _il.Emit(OpCodes.Ldloca, cell);
            _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.TryReloadVariantCell))!);
            _il.Emit(OpCodes.Brfalse, unread);
        }

        // each object's cell, found again as each Variant's is. A Set the interpreter ran stores the reference into the cell the slot holds, so this finds
        // the same cell; it is read from the frame again rather than assumed kept.
        foreach (var (slot, cell) in _objects)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, slot);
            _il.Emit(OpCodes.Ldloca, cell);
            _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.TryReloadObjectCell))!);
            _il.Emit(OpCodes.Brfalse, unread);
        }

        EmitReloadTextCells(unread);

        // the function result variable too, which the spill before the call wrote.
        if (_result is { } result)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloca, result);
            _il.Emit(OpCodes.Call, Activation.GetMethod(ScalarKinds.IsIntegral(_analysis.ResultKind!.Value)
                ? nameof(CompiledActivation.TryReadResultIntegral)
                : nameof(CompiledActivation.TryReadResultFloating))!);
            _il.Emit(OpCodes.Brfalse, unread);
        }
    }

    // the activation the interpreter's from pc, the frame as it is: a variable was not read back from it (EmitReload).
    private void EmitUnread()
    {
        if (!_unreads)
        {
            return;
        }

        _il.MarkLabel(_unread);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, _pc);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.Interpret))!);
        _il.Emit(OpCodes.Ret);
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
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Stloc, loop.Spilled);

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

    // the label the evaluation of value in the instruction at offset goes to when it fails: a Variant it reads declines when it holds what compiled code
    // does not evaluate, which is no error - the interpreter runs the instruction alone, as it does a Let of the Variant, and compiled code goes on after it.
    private Label FailureOf(PayloadNode value, int offset) => ReadsVariant(value) ? RecoverAt(offset) : FailureAt(offset);

    private static bool ReadsVariant(PayloadNode node) => node switch
    {
        UnvariantNode or TruthNode => true,
        ConvertNode convert => ReadsVariant(convert.Operand),
        IntegralNode integral => ReadsVariant(integral.Left) || ReadsVariant(integral.Right),
        LogicalNode logical => ReadsVariant(logical.Left) || ReadsVariant(logical.Right),
        CompareNode compare => ReadsVariant(compare.Left) || ReadsVariant(compare.Right),
        NotNode not => ReadsVariant(not.Operand),
        NegateNode negate => ReadsVariant(negate.Operand),
        StoreNode store => ReadsVariant(store.Value),
        ExtendedCompareNode compare => ReadsVariant(compare.Left) || ReadsVariant(compare.Right),
        TemporaryNode temporary => ReadsVariant(temporary.Value),
        _ => false,
    };

    private static bool ReadsVariant(ExtendedNode node) => node switch
    {
        WidenNode widen => ReadsVariant(widen.Operand),
        ExtendedOperationNode operation => ReadsVariant(operation.Left) || ReadsVariant(operation.Right),
        ExtendedNegateNode negate => ReadsVariant(negate.Operand),
        _ => false,
    };

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
        _il.Emit(OpCodes.Stloc, _pc);

        // where Recover left the activation, pc: a step that did not fall through goes on from here too (EmitStepped).
        _il.MarkLabel(_recovered);
        _il.Emit(OpCodes.Ldloc, _pc);
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Bge, _reentry);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.Recovered))!);
        _il.Emit(OpCodes.Ret);

        // the re-entry: every variable and the function result, and the state of each loop - which an opener the interpreter ran gave it.
        _il.MarkLabel(_reentry);
        EmitReload(_unread);
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

            // what was read back is what the frame has.
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Stloc, loop.Opened);
            _il.Emit(OpCodes.Stloc, loop.Spilled);
        }

        // and each selector: a Select the interpreter ran gave it, on payloads - one it evaluated boxed is no place compiled code goes on (CanGoOnAt).
        for (var i = 0; i < _selects.Count; i++)
        {
            var select = _selects[i];
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, i);
            _il.Emit(OpCodes.Ldloca, select.Selector);
            _il.Emit(OpCodes.Call, Activation.GetMethod(ScalarKinds.IsIntegral(select.Kind)
                ? nameof(CompiledActivation.ReloadSelectorIntegral)
                : nameof(CompiledActivation.ReloadSelectorFloating))!);
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Stloc, select.Opened);
            _il.Emit(OpCodes.Stloc, select.Spilled);
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
            _il.Emit(OpCodes.Ldloc, loop.Spilled);
            _il.Emit(OpCodes.Brtrue, closed);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, i);
            LoadAsBoth(loop.End, loop.Kind);
            LoadAsBoth(loop.Step, loop.Kind);
            _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.SpillForLoop))!);
            _il.Emit(OpCodes.Ldc_I4_1);
            _il.Emit(OpCodes.Stloc, loop.Spilled);
            _il.MarkLabel(closed);
        }

        // each selector a Select gave, which its Case headers compare when the interpreter runs them (CompiledActivation.SpillSelector).
        for (var i = 0; i < _selects.Count; i++)
        {
            var select = _selects[i];
            var closed = _il.DefineLabel();
            _il.Emit(OpCodes.Ldloc, select.Opened);
            _il.Emit(OpCodes.Brfalse, closed);
            _il.Emit(OpCodes.Ldloc, select.Spilled);
            _il.Emit(OpCodes.Brtrue, closed);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, i);
            LoadAsBoth(select.Selector, select.Kind);
            _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.SpillSelector))!);
            _il.Emit(OpCodes.Ldc_I4_1);
            _il.Emit(OpCodes.Stloc, select.Spilled);
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
    // (EmitTemporaries) - or the cell of a variable passed by reference. A String's text, or its cell, is in a local of its own.
    private Operand EmitArgument(PayloadNode node, Label failure)
    {
        switch (node)
        {
            case ReferenceNode reference:
                return new Operand(_cells[reference.Slot], 0, 0);
            case TemporaryNode temporary:
                return EmitPayload(temporary.Value, failure);
            case TextArgumentNode text:
                return new Operand(EmitTextInto(text.Value, failure), 0, 0);
            case TextReferenceNode reference:
                _il.Emit(OpCodes.Ldloc, _texts[reference.Slot]);
                _il.Emit(OpCodes.Brfalse, failure);
                return new Operand(_texts[reference.Slot], 0, 0);
            default:
                return EmitPayload(node, failure);
        }
    }

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

            case SelectorNode selector:
                return new Operand(_selects[selector.Select].Selector, 0, 0);

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
                var negation = negate.OfAConstantExpression && type == typeof(long) ? nameof(PayloadOperations.NegateConstant) : nameof(PayloadOperations.Negate);
                _il.Emit(OpCodes.Call, Payloads.GetMethod(negation, [typeof(ScalarKind), type, type.MakeByRefType()])!);
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

            // a Variant of the subtype it is speculated to hold is converted as its payload is (TryConvertExactly's TryConvertPayload); one of another, as any is.
            case UnvariantNode unvariant:
            {
                var integral = _il.DeclareLocal(typeof(long));
                var floating = _il.DeclareLocal(typeof(double));
                var converted = _il.DefineLabel();
                if (SpeculatedKindOf(unvariant.Value) is { } speculated)
                {
                    var slow = _il.DefineLabel();
                    var typed = EmitSpeculated(unvariant.Value, speculated, slow);
                    var exact = speculated == unvariant.Kind ? typed : EmitConverted(typed, speculated, unvariant.Kind, slow);
                    _il.Emit(OpCodes.Ldloc, exact);
                    _il.Emit(OpCodes.Stloc, ScalarKinds.IsIntegral(unvariant.Kind) ? integral : floating);
                    _il.Emit(OpCodes.Br, converted);
                    _il.MarkLabel(slow);
                }

                var value = _il.DeclareLocal(typeof(VariantPayload));
                EmitVariantInto(unvariant.Value, value, failure);
                _il.Emit(OpCodes.Ldloc, value);
                _il.Emit(OpCodes.Ldc_I4, (int)unvariant.Kind);
                _il.Emit(OpCodes.Ldloca, integral);
                _il.Emit(OpCodes.Ldloca, floating);
                _il.Emit(OpCodes.Call, Variants.GetMethod(nameof(VariantOperations.Convert))!);
                _il.Emit(OpCodes.Brfalse, failure);
                _il.MarkLabel(converted);
                return new Operand(ScalarKinds.IsIntegral(unvariant.Kind) ? integral : floating, 0, 0);
            }

            // two object references compared, or one compared with Nothing (ObjectOperations); a cell compiled code reads no reference from, the interpreter's.
            case IsNode { Left: null, Right: null }:
                _il.Emit(OpCodes.Ldc_I4_1);
                return AsBoolean();

            case IsNode compared:
            {
                var same = _il.DeclareLocal(typeof(bool));
                if (compared is { Left: { } left, Right: { } right })
                {
                    _il.Emit(OpCodes.Ldloc, _objects[left]);
                    _il.Emit(OpCodes.Ldloc, _objects[right]);
                    _il.Emit(OpCodes.Ldloca, same);
                    _il.Emit(OpCodes.Call, Objects.GetMethod(nameof(ObjectOperations.Is))!);
                }
                else
                {
                    _il.Emit(OpCodes.Ldloc, _objects[compared.Left ?? compared.Right!.Value]);
                    _il.Emit(OpCodes.Ldloca, same);
                    _il.Emit(OpCodes.Call, Objects.GetMethod(nameof(ObjectOperations.IsNothing))!);
                }

                _il.Emit(OpCodes.Brfalse, failure);
                _il.Emit(OpCodes.Ldloc, same);
                return AsBoolean();
            }

            case TextCompareNode compared:
                EmitTextCompare(compared, failure);
                return AsBoolean();

            // TypeOf, as the interpreter tests it; of Nothing, which raises error 91, the interpreter's.
            case TypeOfNode typeOf:
            {
                var isOf = _il.DeclareLocal(typeof(bool));
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldloc, _objects[typeOf.Slot]);
                _il.Emit(OpCodes.Ldc_I4, typeOf.Class);
                _il.Emit(OpCodes.Ldloca, isOf);
                _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.TypeOf))!);
                _il.Emit(OpCodes.Brfalse, failure);
                _il.Emit(OpCodes.Ldloc, isOf);
                return AsBoolean();
            }

            // a comparison of Variants of the subtypes they are speculated to hold is the comparison of their payloads (TryCompare), and a Variant of the
            // subtype it is speculated to hold is true when its payload is not zero (ScalarPayload.IsTrue); one of another subtype is evaluated as any is.
            case TruthNode truth:
            {
                var isTrue = _il.DeclareLocal(typeof(bool));
                var decided = _il.DefineLabel();
                var slow = _il.DefineLabel();
                var speculatedTruth = true;
                if (truth.Value is VariantBinaryNode binary && OperatorTokens.ComparisonOf(binary.Token) is { } comparison
                    && VariantSpeculation.ComparisonOf(binary.Token, SpeculatedKindOf(binary.Left), SpeculatedKindOf(binary.Right)) is { } compared)
                {
                    var left = EmitSpeculatedAs(binary.Left, compared, slow);
                    var right = EmitSpeculatedAs(binary.Right, compared, slow);
                    EmitPlannedComparison(comparison, compared, left, right);
                }
                else if (SpeculatedKindOf(truth.Value) is { } speculated)
                {
                    var typed = EmitSpeculated(truth.Value, speculated, slow);
                    _il.Emit(OpCodes.Ldloc, typed);
                    LoadZero(speculated);
                    _il.Emit(OpCodes.Ceq);
                    _il.Emit(OpCodes.Ldc_I4_0);
                    _il.Emit(OpCodes.Ceq);
                }
                else
                {
                    speculatedTruth = false;
                }

                if (speculatedTruth)
                {
                    _il.Emit(OpCodes.Stloc, isTrue);
                    _il.Emit(OpCodes.Br, decided);
                }

                _il.MarkLabel(slow);
                var value = _il.DeclareLocal(typeof(VariantPayload));
                EmitVariantInto(truth.Value, value, failure);
                _il.Emit(OpCodes.Ldloc, value);
                _il.Emit(OpCodes.Call, Variants.GetMethod(nameof(VariantOperations.IsTrue))!);
                _il.Emit(OpCodes.Stloc, isTrue);
                _il.MarkLabel(decided);
                _il.Emit(OpCodes.Ldloc, isTrue);
                return AsBoolean();
            }

            default:
                throw new InvalidOperationException($"No IL for {node.GetType().Name}.");
        }
    }

    // evaluates what a Variant is into a local: each operation planned for the subtypes of its operands, and one the evaluation on
    // payloads declines going to failure.
    private LocalBuilder EmitVariant(VariantNode node, Label failure)
    {
        var result = _il.DeclareLocal(typeof(VariantPayload));

        // an operation whose operands hold the subtypes they are speculated to hold is done as it was planned when it was compiled; the evaluation as any is,
        // where one does not.
        Label? done = null;
        if (node is VariantBinaryNode && SpeculatedKindOf(node) is { } speculated)
        {
            var slow = _il.DefineLabel();
            var typed = EmitSpeculated(node, speculated, slow);
            _il.Emit(OpCodes.Ldc_I4, (int)speculated);
            LoadAsBoth(typed, speculated);
            _il.Emit(OpCodes.Call, typeof(VariantPayload).GetMethod(nameof(VariantPayload.Of))!);
            _il.Emit(OpCodes.Stloc, result);
            done = _il.DefineLabel();
            _il.Emit(OpCodes.Br, done.Value);
            _il.MarkLabel(slow);
        }

        EmitVariantInto(node, result, failure);
        if (done is { } evaluated)
        {
            _il.MarkLabel(evaluated);
        }

        return result;
    }

    // the subtype of what a Variant expression evaluates to, as it is speculated (VariantSpeculation).
    private ScalarKind? SpeculatedKindOf(VariantNode node) => VariantSpeculation.KindOf(node, _analysis.SpeculatedKinds);

    // a Variant expression evaluated in the subtypes it is speculated to be of, into a local of its kind: each Variant read guarded on the subtype it is
    // speculated to hold, each operation the one planned for the subtypes of its operands - both converted to the subtype it is done in
    // (TryConvertPayload's PayloadOperations.Convert), then done as TryOperate does it. A Variant of another subtype, an operation that overflows, go to slow.
    private LocalBuilder EmitSpeculated(VariantNode node, ScalarKind kind, Label slow)
    {
        var result = _il.DeclareLocal(ClrTypeOf(kind));
        switch (node)
        {
            case VariantSlotNode slot:
                _il.Emit(OpCodes.Ldloc, _variants[slot.Slot]);
                _il.Emit(OpCodes.Ldc_I4, (int)kind);
                _il.Emit(OpCodes.Ldloca, result);
                _il.Emit(OpCodes.Call, Variants.GetMethod(ScalarKinds.IsIntegral(kind) ? nameof(VariantOperations.ReadIntegralAs) : nameof(VariantOperations.ReadFloatingAs))!);
                _il.Emit(OpCodes.Brfalse, slow);
                break;

            case VariantOfNode of:
                Load(EmitPayload(of.Value, slow), kind);
                _il.Emit(OpCodes.Stloc, result);
                break;

            case VariantBinaryNode binary:
            {
                var left = EmitSpeculatedAs(binary.Left, kind, slow);
                var right = EmitSpeculatedAs(binary.Right, kind, slow);
                EmitPlannedOperation(binary.Token, kind, left, right, result, slow);
                break;
            }

            default:
                throw new InvalidOperationException($"No speculation of {node.GetType().Name}.");
        }

        return result;
    }

    // a Variant expression speculated, converted to the subtype an operation of it is done in.
    private LocalBuilder EmitSpeculatedAs(VariantNode node, ScalarKind destination, Label slow)
    {
        var kind = SpeculatedKindOf(node)!.Value;
        var value = EmitSpeculated(node, kind, slow);
        return kind == destination ? value : EmitConverted(value, kind, destination, slow);
    }

    // a payload of one kind converted to another, exactly, as TryConvertPayload converts it; one that is not exact goes to slow.
    private LocalBuilder EmitConverted(LocalBuilder value, ScalarKind from, ScalarKind to, Label slow)
    {
        var integral = _il.DeclareLocal(typeof(long));
        var floating = _il.DeclareLocal(typeof(double));
        _il.Emit(OpCodes.Ldc_I4, (int)from);
        LoadAsBoth(value, from);
        _il.Emit(OpCodes.Ldc_I4, (int)to);
        _il.Emit(OpCodes.Ldloca, integral);
        _il.Emit(OpCodes.Ldloca, floating);
        _il.Emit(OpCodes.Call, Payloads.GetMethod(nameof(PayloadOperations.Convert))!);
        _il.Emit(OpCodes.Brtrue, slow);
        return ScalarKinds.IsIntegral(to) ? integral : floating;
    }

    // a Variant expression evaluated as any is, into result: each operation planned for the subtypes its operands hold, and one the evaluation on payloads
    // declines going to failure.
    private void EmitVariantInto(VariantNode node, LocalBuilder result, Label failure)
    {
        switch (node)
        {
            case VariantSlotNode slot:
                _il.Emit(OpCodes.Ldloc, _variants[slot.Slot]);
                _il.Emit(OpCodes.Ldloca, result);
                _il.Emit(OpCodes.Call, Variants.GetMethod(nameof(VariantOperations.Read))!);
                _il.Emit(OpCodes.Brfalse, failure);
                break;

            case VariantOfNode of:
            {
                var value = EmitPayload(of.Value, failure);
                _il.Emit(OpCodes.Ldc_I4, (int)of.Value.Kind);
                LoadAsBoth(value, of.Value.Kind);
                _il.Emit(OpCodes.Call, typeof(VariantPayload).GetMethod(nameof(VariantPayload.Of))!);
                _il.Emit(OpCodes.Stloc, result);
                break;
            }

            case VariantBinaryNode binary:
            {
                var left = EmitVariant(binary.Left, failure);
                var right = EmitVariant(binary.Right, failure);
                _il.Emit(OpCodes.Ldstr, binary.Token);
                _il.Emit(OpCodes.Ldloc, left);
                _il.Emit(OpCodes.Ldloc, right);
                _il.Emit(OpCodes.Ldloca, result);
                _il.Emit(OpCodes.Call, Variants.GetMethod(nameof(VariantOperations.Binary))!);
                _il.Emit(OpCodes.Brfalse, failure);
                break;
            }

            case VariantUnaryNode unary:
            {
                var operand = EmitVariant(unary.Operand, failure);
                _il.Emit(OpCodes.Ldstr, unary.Token);
                _il.Emit(OpCodes.Ldloc, operand);
                _il.Emit(unary.OfAConstantExpression ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                _il.Emit(OpCodes.Ldloca, result);
                _il.Emit(OpCodes.Call, Variants.GetMethod(nameof(VariantOperations.Unary))!);
                _il.Emit(OpCodes.Brfalse, failure);
                break;
            }
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
