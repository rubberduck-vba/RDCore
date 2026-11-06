using System.Reflection.Emit;

namespace RDCore.Runtime.Compiled;

// where compiled code finds the cell of an object it holds: the frame's, for a variable of the activation, and where the interpreter finds it, for a field.
internal sealed partial class ProcedureEmitter
{
    // the cell of the object in slot, as the frame holds it, or a field's, found with findField: none, for one held otherwise, which compiled code then reads
    // and writes nothing of.
    private void EmitObjectCell(int slot, LocalBuilder cell, string findField)
    {
        _il.Emit(OpCodes.Ldarg_0);
        if (_fieldNumbers.TryGetValue(slot, out var number))
        {
            _il.Emit(OpCodes.Ldc_I4, number);
            _il.Emit(OpCodes.Call, Activation.GetMethod(findField)!);
        }
        else
        {
            _il.Emit(OpCodes.Ldc_I4, slot);
            _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.ObjectCellOf))!);
        }

        _il.Emit(OpCodes.Stloc, cell);
    }
}
