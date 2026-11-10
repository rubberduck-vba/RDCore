using System.Reflection.Emit;

namespace RDCore.Runtime.Compiled;

// a For Each of the body, whose statement and Next the interpreter runs for compiled code.
internal sealed partial class ProcedureEmitter
{
    // the For Each statement, run by the interpreter as a support call is (EmitSupportCall): every variable written into the frame before it, which the
    // collection is evaluated with, and read back after it: a function it calls may have written one passed to it by reference, and a debugger any. One that
    // is not read back leaves the activation to the interpreter from where the statement sent it; so does the interpreter wanted then, as after a call.
    private void EmitForEachOpen(ForEachOpenStep open)
    {
        EmitSpill();
        var next = EmitStepForEach(open.Offset);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, Activation.GetProperty(nameof(CompiledActivation.ReadsBackAfterCall))!.GetMethod!);
        _il.Emit(OpCodes.Pop);
        EmitGoOnAt(next, open.Offset + 1, open.Past, readBack: true);
    }

    // the Next, run by the interpreter with the state the frame holds of the loop. It reads no variable but the control variable, which the frame holds, so
    // the variables are written into the frame before it only under a debugger, and read back after it only when the program waited, as around a call (a
    // Terminate it runs may wait at a breakpoint).
    private void EmitForEachNext(ForEachNextStep next)
    {
        EmitPoll(next.Offset);
        var spilled = _il.DefineLabel();
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, Activation.GetProperty(nameof(CompiledActivation.SpillsBeforeCalls))!.GetMethod!);
        _il.Emit(OpCodes.Brfalse, spilled);
        EmitSpill();
        _il.MarkLabel(spilled);

        var at = EmitStepForEach(next.Offset);
        var current = _il.DefineLabel();
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, Activation.GetProperty(nameof(CompiledActivation.ReadsBackAfterCall))!.GetMethod!);
        _il.Emit(OpCodes.Brfalse, current);
        EmitGoOnAt(at, next.Body, next.Offset + 1, readBack: true);
        _il.MarkLabel(current);
        EmitGoOnAt(at, next.Body, next.Offset + 1, readBack: false);
    }

    // CompiledActivation.StepForEach: the offset the program goes on at, in a local; an error the instruction raised is given the activation's handler as a
    // call's is.
    private LocalBuilder EmitStepForEach(int offset)
    {
        var next = _il.DeclareLocal(typeof(int));
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, offset);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.StepForEach))!);
        _il.Emit(OpCodes.Stloc, next);
        _il.Emit(OpCodes.Ldloc, next);
        _il.Emit(OpCodes.Ldc_I4_M1);
        _il.Emit(OpCodes.Beq, CallFailedAt(offset));
        return next;
    }

    // where the program goes on after the instruction: the variables read back first, when asked (one that is not leaves the activation to the interpreter
    // there), and the activation the interpreter's there when it is wanted; else one of the two instructions the instruction goes on at.
    private void EmitGoOnAt(LocalBuilder next, int first, int second, bool readBack)
    {
        if (readBack)
        {
            var read = _il.DefineLabel();
            var unread = _il.DefineLabel();
            EmitReload(unread);
            _il.Emit(OpCodes.Br, read);
            _il.MarkLabel(unread);
            _il.Emit(OpCodes.Ldloc, next);
            _il.Emit(OpCodes.Stloc, _pc);
            _il.Emit(OpCodes.Br, _unread);
            _il.MarkLabel(read);
        }

        var compiled = _il.DefineLabel();
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, Activation.GetProperty(nameof(CompiledActivation.WantsInterpreterAfterCall))!.GetMethod!);
        _il.Emit(OpCodes.Brfalse, compiled);
        _il.Emit(OpCodes.Ldloc, next);
        _il.Emit(OpCodes.Stloc, _pc);
        _il.Emit(OpCodes.Br, _walkBack);
        _il.MarkLabel(compiled);

        // an offset that is neither is the interpreter's to go on at.
        _il.Emit(OpCodes.Ldloc, next);
        _il.Emit(OpCodes.Ldc_I4, first);
        _il.Emit(OpCodes.Beq, _instructions[first]);
        _il.Emit(OpCodes.Ldloc, next);
        _il.Emit(OpCodes.Ldc_I4, second);
        _il.Emit(OpCodes.Beq, _instructions[second]);
        _il.Emit(OpCodes.Ldloc, next);
        _il.Emit(OpCodes.Stloc, _pc);
        _il.Emit(OpCodes.Br, _walkBack);
    }
}
