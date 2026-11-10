using System.Reflection.Emit;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a call inside an expression: the call made as a call statement makes one, and, where the instruction is the interpreter's
// after it, the activation handed to the interpreter in the middle of the instruction with what was evaluated of it known (CompiledActivation.Know): a
// call made is not made again, and a variable read before it is what it was.
internal sealed partial class ProcedureEmitter
{
    // the instruction emitted: whether a call inside it is made where the IL is, and what it read and called until there, each in a local of its own.
    private int _knowingAt;
    private bool _called;
    private readonly List<KnownLocal> _known = [];
    private readonly List<(Label Label, KnownLocal[] Known, Label Failure)> _knownFailures = [];

    // a payload of its kind, or the object a call returned (IsObject: CompiledActivation.KnowObject).
    private sealed record class KnownLocal(int Syntax, ScalarKind Kind, LocalBuilder Local, bool IsObject = false);

    private void BeginKnowing(int offset)
    {
        (_knowingAt, _called) = (offset, false);
        _known.Clear();
    }

    // the label a failure goes to from where the IL is: before a call inside the instruction is made, failure - the interpreter makes the instruction from
    // its start; after one, a label that gives the activation what was evaluated until there, and goes to failure.
    private Label Fail(Label failure)
    {
        if (!_called)
        {
            return failure;
        }

        var label = _il.DefineLabel();
        _knownFailures.Add((label, [.. _known], failure));
        return label;
    }

    private void EmitKnownFailures()
    {
        foreach (var (label, known, failure) in _knownFailures)
        {
            _il.MarkLabel(label);
            EmitKnow(known);
            _il.Emit(OpCodes.Br, failure);
        }
    }

    private void EmitKnow(IEnumerable<KnownLocal> known)
    {
        foreach (var each in known)
        {
            if (each.IsObject)
            {
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldc_I4, each.Syntax);
                _il.Emit(OpCodes.Ldloc, each.Local);
                _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.KnowObject))!);
                continue;
            }

            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, each.Syntax);
            _il.Emit(OpCodes.Ldc_I4, (int)each.Kind);
            if (ScalarKinds.IsIntegral(each.Kind))
            {
                _il.Emit(OpCodes.Ldloc, each.Local);
                _il.Emit(OpCodes.Ldc_R8, 0.0);
            }
            else
            {
                _il.Emit(OpCodes.Ldc_I8, 0L);
                _il.Emit(OpCodes.Ldloc, each.Local);
            }

            _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.Know))!);
        }
    }

    // a variable read into a local of its own, as it is when it is read: a call the instruction makes after may change it, or a debugger while the program
    // waits in the callee.
    private Operand EmitKnown(KnownNode known, Label failure)
    {
        var read = EmitPayload(known.Value, failure);
        var local = _il.DeclareLocal(ClrTypeOf(known.Kind));
        Store(read, known.Kind, local);
        _known.Add(new KnownLocal(known.Syntax, known.Kind, local));
        return new Operand(local, 0, 0);
    }

    // the call, then what it returned the operand's value. After a wait, a variable compiled code cannot read back leaves the activation to the interpreter in
    // the middle of the instruction, the frame as it is; and so does a call after which the interpreter is wanted, the procedure was edited, or the symbols
    // were defined again - the activation written into its frame (CompiledActivation.WantsInterpreterAfterCall).
    private Operand EmitNestedCall(CallNode node, Label failure)
    {
        var offset = _knowingAt;
        var (integral, floating) = EmitCallOf(new CallStep(offset, node.Callee, node.Arguments, CallStep.NoResult, node.Receiver)
        {
            WithAt = node.WithAt, ReceiverField = node.ReceiverField, ReceiverCall = node.ReceiverCall,
        }, failure);
        var returned = ScalarKinds.IsIntegral(node.Kind) ? integral : floating;
        _called = true;
        _known.Add(new KnownLocal(node.Syntax, node.Kind, returned));
        EmitAfterNestedCall(offset);
        return new Operand(returned, 0, 0);
    }

    // after a call inside an expression: the activation handed to the interpreter in the middle of the instruction, what was evaluated of it known, when
    // compiled code cannot go on with it.
    private void EmitAfterNestedCall(int offset)
    {
        var current = _il.DefineLabel();
        var unread = _il.DefineLabel();
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, Activation.GetProperty(nameof(CompiledActivation.ReadsBackAfterCall))!.GetMethod!);
        _il.Emit(OpCodes.Brfalse, current);
        EmitReload(unread);
        _il.Emit(OpCodes.Br, current);
        _il.MarkLabel(unread);
        EmitKnow(_known);
        _il.Emit(OpCodes.Ldc_I4, offset);
        _il.Emit(OpCodes.Stloc, _pc);
        _il.Emit(OpCodes.Br, _unread);
        _il.MarkLabel(current);

        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, Activation.GetProperty(nameof(CompiledActivation.WantsInterpreterAfterCall))!.GetMethod!);
        _il.Emit(OpCodes.Brtrue, Fail(WalkBackAt(offset)));

        // under the random walk-back, after the call too: the instruction is the interpreter's from the middle of it.
        if (CompiledExecution.WalksBackAtRandom)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Call, Activation.GetProperty(nameof(CompiledActivation.ShouldWalkBack))!.GetMethod!);
            _il.Emit(OpCodes.Brtrue, Fail(WalkBackAt(offset)));
        }
    }
}
