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
/// method, a <see cref="long"/> or a <see cref="double"/>, and so are the end and the step of each <c>For</c> loop. Every operation that can fail is a call
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
    private readonly ILGenerator _il;
    private readonly Dictionary<int, LocalBuilder> _slots = [];
    private readonly Dictionary<int, Label> _instructions = [];
    private readonly Dictionary<int, Label> _failures = [];
    private readonly List<LoopLocals> _loops = [];
    private readonly LocalBuilder _pc;
    private readonly Label _walkBack;
    private LocalBuilder? _result;

    private sealed record class LoopLocals(LocalBuilder End, LocalBuilder Step, LocalBuilder CountsDown, LocalBuilder IsOpen, ScalarKind Kind);

    private ProcedureEmitter(ProcedureAnalysis analysis, ILGenerator il)
    {
        (_analysis, _il) = (analysis, il);
        _pc = il.DeclareLocal(typeof(int));
        _walkBack = il.DefineLabel();
    }

    /// <summary>
    /// Writes the body of <paramref name="method"/>, a static method of one <see cref="CompiledActivation"/> parameter that returns a
    /// <see cref="RuntimeExecutionOutcome"/>, from <paramref name="analysis"/>.
    /// </summary>
    public static void Emit(ProcedureAnalysis analysis, MethodBuilder method) => new ProcedureEmitter(analysis, method.GetILGenerator()).Emit();

    private void Emit()
    {
        foreach (var (slot, kind) in _analysis.Slots)
        {
            _slots[slot] = _il.DeclareLocal(ClrTypeOf(kind));
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
            EmitStep(step);
        }

        // the end of the body (MS-VBAL §5.4.2.17): as an Exit.
        _il.MarkLabel(_instructions[_analysis.Length]);
        EmitExit();

        EmitFailures();
        EmitWalkBack();
    }

    private static Type ClrTypeOf(ScalarKind kind) => ScalarKinds.IsIntegral(kind) ? typeof(long) : typeof(double);

    // the parameters, read from their cells: a cell that holds no payload is the interpreter's to read, and the activation is handed to it before anything runs.
    private void EmitEntry()
    {
        var interpret = _il.DefineLabel();
        foreach (var (slot, kind) in _analysis.Parameters)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, slot);
            _il.Emit(OpCodes.Ldloca, _slots[slot]);
            _il.Emit(OpCodes.Call, Activation.GetMethod(ScalarKinds.IsIntegral(kind) ? nameof(CompiledActivation.TryReadIntegral) : nameof(CompiledActivation.TryReadFloating))!);
            _il.Emit(OpCodes.Brfalse, interpret);
        }

        var body = _il.DefineLabel();
        _il.Emit(OpCodes.Br, body);
        _il.MarkLabel(interpret);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.Interpret))!);
        _il.Emit(OpCodes.Ret);
        _il.MarkLabel(body);
    }

    private void EmitStep(CompiledStep step)
    {
        switch (step)
        {
            case LetStep let:
                Store(EmitPayload(let.Value, FailureAt(let.Offset)), let.Value.Kind, let.Slot < 0 ? _result! : _slots[let.Slot]);
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
        }
    }

    // the activation is handed to the interpreter between two instructions, before this one, when the program is to stop or a debugger is attached.
    private void EmitPoll(int offset)
    {
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, Activation.GetProperty(nameof(CompiledActivation.ShouldWalkBack))!.GetMethod!);
        _il.Emit(OpCodes.Brtrue, FailureAt(offset));
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
        _il.Emit(OpCodes.Stloc, loop.IsOpen);

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

        var exit = _il.DefineLabel();
        EmitPastTheEnd(loop, next.Counter, exit);
        _il.Emit(OpCodes.Br, _instructions[next.Body]);
        _il.MarkLabel(exit);
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Stloc, loop.IsOpen);
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

    // the label an operation of the instruction at offset goes to when it fails: the instruction is the interpreter's, from there on.
    private Label FailureAt(int offset)
    {
        if (!_failures.TryGetValue(offset, out var label))
        {
            label = _il.DefineLabel();
            _failures[offset] = label;
        }

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
    }

    // every variable written into its cell, the function result into its variable, each loop that was opened into the frame; then the interpreter runs the
    // activation from the instruction at pc.
    private void EmitWalkBack()
    {
        _il.MarkLabel(_walkBack);
        foreach (var (slot, kind) in _analysis.Slots)
        {
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
            _il.Emit(OpCodes.Ldloc, loop.IsOpen);
            _il.Emit(OpCodes.Brfalse, closed);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, i);
            LoadAsBoth(loop.End, loop.Kind);
            LoadAsBoth(loop.Step, loop.Kind);
            _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.SpillForLoop))!);
            _il.MarkLabel(closed);
        }

        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, _pc);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.Interpret))!);
        _il.Emit(OpCodes.Ret);
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

    // evaluates a payload into a local - or names the local or the constant it is - leaving nothing on the stack, so that a failure branches from an empty one.
    private Operand EmitPayload(PayloadNode node, Label failure)
    {
        switch (node)
        {
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
