using System.Reflection.Emit;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a With block.
internal sealed partial class ProcedureEmitter
{
    // CompiledActivation.TryWith of the cell the object is held in: a With it declines is the interpreter's alone, and compiled code goes on after it. A With
    // of what a call returns makes the call first (EmitObjectCall), and one of a New makes the object first (EmitNew): one it declines after is the
    // interpreter's with the object known (Fail), which does not make the call, or the object, again.
    private void EmitWith(WithStep with)
    {
        var failure = RecoverAt(with.Offset);
        var source = with.SourceCall is { } call ? EmitObjectCall(call, FailureAt(with.Offset))
            : with.SourceNew is { } made ? EmitNew(made)
            : EmitObjectOperand(with.Source!, failure);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, with.Offset);
        _il.Emit(OpCodes.Ldloc, source);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.TryWith))!);
        _il.Emit(OpCodes.Brfalse, Fail(failure));
    }

    // CompiledActivation.TryEndWith: an End With it declines is the interpreter's alone, and compiled code goes on after it.
    private void EmitEndWith(EndWithStep endWith)
    {
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, endWith.Opener);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.TryEndWith))!);
        _il.Emit(OpCodes.Brfalse, RecoverAt(endWith.Offset));
    }
}
