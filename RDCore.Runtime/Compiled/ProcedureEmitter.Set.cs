using System.Reflection.Emit;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a Set statement.
internal sealed partial class ProcedureEmitter
{
    // CompiledActivation.TrySet of the cells the activation holds the objects in, or TrySetResult: a Set it declines is the interpreter's alone, and compiled
    // code goes on after it.
    private void EmitSet(SetStep set)
    {
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, set.Offset);
        if (set.Target != SetStep.Result)
        {
            _il.Emit(OpCodes.Ldc_I4, set.Target);
            _il.Emit(OpCodes.Ldloc, _objects[set.Target]);
        }

        if (set.Source is { } source)
        {
            _il.Emit(OpCodes.Ldloc, _objects[source]);
            _il.Emit(OpCodes.Ldc_I4_0);
        }
        else
        {
            _il.Emit(OpCodes.Ldnull);
            _il.Emit(OpCodes.Ldc_I4_1);
        }

        _il.Emit(OpCodes.Call, Activation.GetMethod(set.Target == SetStep.Result ? nameof(CompiledActivation.TrySetResult) : nameof(CompiledActivation.TrySet))!);
        _il.Emit(OpCodes.Brfalse, RecoverAt(set.Offset));
    }

    // the interpreter's statement, run as a support call (CompiledActivation.RunSetStatement), every variable written into the frame before it: the error it
    // raises is given to the activation's handler as a call's is (CallFailedAt), the statement not run again. Every variable is read back after it, as after
    // a call the program waited in: a procedure it called may have written one passed to it by reference, and a debugger any. One that is not read back
    // leaves the activation to the interpreter from after the statement; so does the interpreter wanted then, as after a call.
    private void EmitSetStatement(SetStatementStep set)
    {
        EmitSpill();
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, set.Offset);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.RunSetStatement))!);
        _il.Emit(OpCodes.Brfalse, CallFailedAt(set.Offset));

        // asked as after a call, which the random failed accesses fail the first read-back after a wait by, whatever the seed.
        var read = _il.DefineLabel();
        var unread = _il.DefineLabel();
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, Activation.GetProperty(nameof(CompiledActivation.ReadsBackAfterCall))!.GetMethod!);
        _il.Emit(OpCodes.Pop);
        EmitReload(unread);
        _il.Emit(OpCodes.Br, read);
        _il.MarkLabel(unread);
        _il.Emit(OpCodes.Ldc_I4, set.Offset + 1);
        _il.Emit(OpCodes.Stloc, _pc);
        _il.Emit(OpCodes.Br, _unread);
        _il.MarkLabel(read);

        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, Activation.GetProperty(nameof(CompiledActivation.WantsInterpreterAfterCall))!.GetMethod!);
        _il.Emit(OpCodes.Brtrue, WalkBackAt(set.Offset + 1));
    }
}
