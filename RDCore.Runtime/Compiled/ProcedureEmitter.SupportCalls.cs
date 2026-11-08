using System.Reflection.Emit;

namespace RDCore.Runtime.Compiled;

// a statement the interpreter runs for compiled code, as a call compiled code makes.
internal sealed partial class ProcedureEmitter
{
    // the interpreter's statement, run as a support call (CompiledActivation.RunStatement), every variable written into the frame before it: the error it
    // raises is given to the activation's handler as a call's is (CallFailedAt), the statement not run again. Every variable is read back after it, as after
    // a call the program waited in: a procedure it called may have written one passed to it by reference, and a debugger any. One that is not read back
    // leaves the activation to the interpreter from after the statement; so does the interpreter wanted then, as after a call.
    private void EmitSupportCall(SupportCallStep support)
    {
        EmitSpill();
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, support.Offset);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.RunStatement))!);
        _il.Emit(OpCodes.Brfalse, CallFailedAt(support.Offset));

        // asked as after a call, which the random failed accesses fail the first read-back after a wait by, whatever the seed.
        var read = _il.DefineLabel();
        var unread = _il.DefineLabel();
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, Activation.GetProperty(nameof(CompiledActivation.ReadsBackAfterCall))!.GetMethod!);
        _il.Emit(OpCodes.Pop);
        EmitReload(unread);
        _il.Emit(OpCodes.Br, read);
        _il.MarkLabel(unread);
        _il.Emit(OpCodes.Ldc_I4, support.Offset + 1);
        _il.Emit(OpCodes.Stloc, _pc);
        _il.Emit(OpCodes.Br, _unread);
        _il.MarkLabel(read);

        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, Activation.GetProperty(nameof(CompiledActivation.WantsInterpreterAfterCall))!.GetMethod!);
        _il.Emit(OpCodes.Brtrue, WalkBackAt(support.Offset + 1));
    }
}
