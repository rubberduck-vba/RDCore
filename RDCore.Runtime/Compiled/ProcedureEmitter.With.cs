using System.Reflection.Emit;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a With block.
internal sealed partial class ProcedureEmitter
{
    // CompiledActivation.TryEndWith: an End With it declines is the interpreter's alone, and compiled code goes on after it.
    private void EmitEndWith(EndWithStep endWith)
    {
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, endWith.Opener);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.TryEndWith))!);
        _il.Emit(OpCodes.Brfalse, RecoverAt(endWith.Offset));
    }
}
