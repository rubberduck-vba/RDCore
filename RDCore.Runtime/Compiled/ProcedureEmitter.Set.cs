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
}
