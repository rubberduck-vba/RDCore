using System.Reflection.Emit;

namespace RDCore.Runtime.Compiled;

/// <remarks>
/// A record parameter or variable (IR lowering S2.6a) is a local holding its holder, the cell its frame holds it in, found as the activation begins and again
/// wherever the activation goes on after the interpreter ran an instruction, as an object's cell is. Each access of a field finds the record the holder holds
/// then, and the field in it (<see cref="RecordOperations"/>): nothing is held of the record itself, so nothing is written into the frame or read back of it.
/// A record that is a field of Me or of a module is held by the field's own cell, which is found and found again as any field's is.
/// </remarks>
internal sealed partial class ProcedureEmitter
{
    private static readonly Type Records = typeof(RecordOperations);

    // the record slots, each by the local its holder is held in (IR lowering S2.6a).
    private readonly Dictionary<int, LocalBuilder> _records = [];

    // a record field's holder is its cell, which the field's own (EmitFieldCell) finds and finds again.
    private void DeclareRecords()
    {
        foreach (var slot in _analysis.RecordSlots)
        {
            _records[slot] = _cells.TryGetValue(slot, out var held) ? held : _il.DeclareLocal(typeof(object));
        }
    }

    // each record's holder, as the frame holds it: none, for one held otherwise, whose fields compiled code then reads and writes nothing of.
    private void EmitRecordCells()
    {
        foreach (var (slot, holder) in _records)
        {
            if (_analysis.FieldSlots.Contains(slot))
            {
                continue;
            }

            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, slot);
            _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.RecordCellOf))!);
            _il.Emit(OpCodes.Stloc, holder);
        }
    }

    // each record's holder, found again: what the interpreter ran may have bound the slot to another.
    private void EmitReloadRecordCells(Label unread)
    {
        foreach (var (slot, holder) in _records)
        {
            if (_analysis.FieldSlots.Contains(slot))
            {
                continue;
            }

            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, slot);
            _il.Emit(OpCodes.Ldloca, holder);
            _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.TryReloadRecordCell))!);
            _il.Emit(OpCodes.Brfalse, unread);
        }
    }

    // the record whose field a path names, onto the stack: the one its holder holds, then each field of it that is a record, in turn. Null when there is none,
    // which each operation of a field declines.
    private void LoadRecordOf(FieldPath field)
    {
        _il.Emit(OpCodes.Ldloc, _records[field.Holder]);
        _il.Emit(OpCodes.Call, Records.GetMethod(nameof(RecordOperations.RecordIn))!);
        for (var level = 0; level < field.Indexes.Length - 1; level++)
        {
            _il.Emit(OpCodes.Ldc_I4, field.Indexes[level]);
            _il.Emit(OpCodes.Call, Records.GetMethod(nameof(RecordOperations.FieldRecord))!);
        }

        _il.Emit(OpCodes.Ldc_I4, field.Indexes[^1]);
    }

    // a field of a scalar type, read into a local of its kind; one that holds no payload of it goes to failure.
    private Operand EmitField(FieldNode field, Label failure)
    {
        var read = _il.DeclareLocal(ClrTypeOf(field.Kind));
        LoadRecordOf(field.Field);
        _il.Emit(OpCodes.Ldc_I4, (int)field.Kind);
        _il.Emit(OpCodes.Ldloca, read);
        _il.Emit(OpCodes.Call, Records.GetMethod(ScalarKinds.IsIntegral(field.Kind) ? nameof(RecordOperations.ReadIntegral) : nameof(RecordOperations.ReadFloating))!);
        _il.Emit(OpCodes.Brfalse, failure);
        return new Operand(read, 0, 0);
    }

    // a Let of a field of a scalar type: the value evaluated first, as the interpreter evaluates it before the record (AssignField), then written.
    private void EmitFieldLet(FieldLetStep let)
    {
        var failure = FailureAt(let.Offset);
        var value = EmitPayload(let.Value, failure);
        LoadRecordOf(let.Field);
        _il.Emit(OpCodes.Ldc_I4, (int)let.Value.Kind);
        Load(value, let.Value.Kind);
        _il.Emit(OpCodes.Call, Records.GetMethod(ScalarKinds.IsIntegral(let.Value.Kind) ? nameof(RecordOperations.WriteIntegral) : nameof(RecordOperations.WriteFloating))!);
        _il.Emit(OpCodes.Brfalse, failure);
    }

    // a Variant field, read into result; one that holds no payload goes to failure.
    private void EmitVariantField(VariantFieldNode field, LocalBuilder result, Label failure)
    {
        LoadRecordOf(field.Field);
        _il.Emit(OpCodes.Ldloca, result);
        _il.Emit(OpCodes.Call, Records.GetMethod(nameof(RecordOperations.ReadVariant))!);
        _il.Emit(OpCodes.Brfalse, failure);
    }

    // a Let of a Variant field: what the Variant held decides it, as a Variant variable's (VariantLetStep) - over an object, the interpreter runs it alone.
    private void EmitVariantFieldLet(VariantFieldLetStep let)
    {
        var failure = RecoverAt(let.Offset);
        var value = _il.DeclareLocal(typeof(VariantPayload));
        EmitVariantInto(let.Value, value, failure);
        LoadRecordOf(let.Field);
        _il.Emit(OpCodes.Ldloc, value);
        _il.Emit(OpCodes.Call, Records.GetMethod(nameof(RecordOperations.WriteVariant))!);
        _il.Emit(OpCodes.Brfalse, failure);
    }
}
