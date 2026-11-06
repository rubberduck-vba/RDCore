using System.Reflection.Emit;

namespace RDCore.Runtime.Compiled;

// arrays. Each array parameter or variable compiled code reads is a local holding the cell its frame holds it in, fetched at the entry
// and again wherever every variable is read back (EmitReload); an element is found in the array that cell holds each time it is read or written, and one
// that isn't found - a subscript outside its dimension - is the interpreter's, at the instruction, which raises error 9.
internal sealed partial class ProcedureEmitter
{
    private static readonly Type ArraySupport = typeof(ArrayOperations);

    // the array slots, each by the local the cell its frame holds it in is held in.
    private readonly Dictionary<int, LocalBuilder> _arrays = [];

    private void DeclareArrays()
    {
        foreach (var array in _analysis.Arrays)
        {
            _arrays[array.Slot] = _il.DeclareLocal(typeof(object));
        }
    }

    // each array's cell, as the frame holds it: none, for one the frame holds no array in yet, whose elements compiled code then finds none of.
    private void EmitArrayEntry()
    {
        foreach (var slot in _arrays.Keys)
        {
            EmitArrayCellOf(slot);
        }
    }

    // each array's cell, found again: what the interpreter ran may have bound the slot to another.
    private void EmitArrayReload(Label unread)
    {
        foreach (var (slot, holder) in _arrays)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, slot);
            _il.Emit(OpCodes.Ldloca, holder);
            _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.TryReloadArrayCell))!);
            _il.Emit(OpCodes.Brfalse, unread);
        }
    }

    private void EmitArrayCellOf(int slot)
    {
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, slot);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.ArrayCellOf))!);
        _il.Emit(OpCodes.Stloc, _arrays[slot]);
    }

    private void EmitArrayStep(CompiledStep step)
    {
        switch (step)
        {
            // the element's cell found, then the value evaluated and written into it: neither does anything the interpreter would see, when either fails.
            case ElementLetStep let:
            {
                var failure = FailureAt(let.Offset);
                var cell = EmitElementCell(let.Element, failure);
                var value = EmitPayload(let.Value, failure);
                _il.Emit(OpCodes.Ldloc, cell);
                Load(value, let.Element.Kind);
                _il.Emit(OpCodes.Call, Activation.GetMethod(ScalarKinds.IsIntegral(let.Element.Kind) ? nameof(CompiledActivation.WriteIntegral) : nameof(CompiledActivation.WriteFloating))!);
                break;
            }

            // what the Variant element holds decides what its Let is, as a Variant variable's does (VariantLetStep): one the evaluation on payloads declines
            // - of a String, over an object - the interpreter runs alone, and compiled code goes on after it.
            case VariantElementLetStep let:
            {
                var failure = RecoverAt(let.Offset);
                var cell = EmitElementCellOf(let.Element.Array, let.Element.Subscripts, nameof(ArrayOperations.VariantElement), null, failure);
                var value = _il.DeclareLocal(typeof(VariantPayload));
                EmitVariantInto(let.Value, value, failure);
                _il.Emit(OpCodes.Ldloc, cell);
                _il.Emit(OpCodes.Ldloc, value);
                _il.Emit(OpCodes.Call, Variants.GetMethod(nameof(VariantOperations.Write))!);
                _il.Emit(OpCodes.Brfalse, failure);
                break;
            }

            // the statement run by the interpreter, every variable written into the frame its bounds are read from; the error it raises is given to the
            // activation's handler as a call's is (CallFailedAt), the statement not run again. The cell of each array is found again after it.
            case ArrayStatementStep statement:
                EmitSpill();
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldc_I4, statement.Offset);
                _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.RunArrayStatement))!);
                _il.Emit(OpCodes.Brfalse, CallFailedAt(statement.Offset));
                foreach (var slot in statement.Arrays)
                {
                    EmitArrayCellOf(slot);
                }

                break;

            default:
                throw new InvalidOperationException($"No IL for {step.GetType().Name}.");
        }
    }

    private Operand EmitArrayPayload(PayloadNode node, Label failure)
    {
        switch (node)
        {
            case ElementNode element:
            {
                var cell = EmitElementCell(element, failure);
                var read = _il.DeclareLocal(ClrTypeOf(element.Kind));
                _il.Emit(OpCodes.Ldloc, cell);
                _il.Emit(OpCodes.Ldloca, read);
                _il.Emit(OpCodes.Call, Activation.GetMethod(ScalarKinds.IsIntegral(element.Kind) ? nameof(CompiledActivation.ReadIntegral) : nameof(CompiledActivation.ReadFloating))!);
                _il.Emit(OpCodes.Brfalse, failure);
                return new Operand(read, 0, 0);
            }

            case ArrayBoundNode bound:
            {
                var dimension = bound.Dimension is { } given ? EmitPayload(given, failure) : new Operand(null, 1, 0);
                var result = _il.DeclareLocal(typeof(long));
                _il.Emit(OpCodes.Ldloc, _arrays[bound.Array]);
                _il.Emit(bound.Upper ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                Load(dimension, ScalarKind.Long);
                _il.Emit(OpCodes.Ldloca, result);
                _il.Emit(OpCodes.Call, ArraySupport.GetMethod(nameof(ArrayOperations.Bound))!);
                _il.Emit(OpCodes.Brfalse, failure);
                return new Operand(result, 0, 0);
            }

            // Is of an element of an array of objects, as IsNode compares variables: an element no object was ever Set to is the interpreter's.
            case ElementIsNode compared:
            {
                var left = compared.Left is { } l ? EmitObjectOperand(l, failure) : null;
                var right = compared.Right is { } r ? EmitObjectOperand(r, failure) : null;
                var same = _il.DeclareLocal(typeof(bool));
                if (left is not null && right is not null)
                {
                    _il.Emit(OpCodes.Ldloc, left);
                    _il.Emit(OpCodes.Ldloc, right);
                    _il.Emit(OpCodes.Ldloca, same);
                    _il.Emit(OpCodes.Call, Objects.GetMethod(nameof(ObjectOperations.Is))!);
                }
                else
                {
                    _il.Emit(OpCodes.Ldloc, left ?? right!);
                    _il.Emit(OpCodes.Ldloca, same);
                    _il.Emit(OpCodes.Call, Objects.GetMethod(nameof(ObjectOperations.IsNothing))!);
                }

                _il.Emit(OpCodes.Brfalse, failure);
                _il.Emit(OpCodes.Ldloc, same);
                return AsBoolean();
            }

            case ElementTypeOfNode typeOf:
            {
                var cell = EmitObjectOperand(typeOf.Element, failure);
                var isOf = _il.DeclareLocal(typeof(bool));
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldloc, cell);
                _il.Emit(OpCodes.Ldc_I4, typeOf.Class);
                _il.Emit(OpCodes.Ldloca, isOf);
                _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.TypeOf))!);
                _il.Emit(OpCodes.Brfalse, failure);
                _il.Emit(OpCodes.Ldloc, isOf);
                return AsBoolean();
            }

            default:
                throw new InvalidOperationException($"No IL for {node.GetType().Name}.");
        }
    }

    private void EmitArrayVariantInto(VariantNode node, LocalBuilder result, Label failure)
    {
        if (node is not VariantElementNode element)
        {
            throw new InvalidOperationException($"No IL for {node.GetType().Name}.");
        }

        var cell = EmitElementCellOf(element.Array, element.Subscripts, nameof(ArrayOperations.VariantElement), null, failure);
        _il.Emit(OpCodes.Ldloc, cell);
        _il.Emit(OpCodes.Ldloca, result);
        _il.Emit(OpCodes.Call, Variants.GetMethod(nameof(VariantOperations.Read))!);
        _il.Emit(OpCodes.Brfalse, failure);
    }

    // the cell an object operand of Is or TypeOf is held in, in a local: an object variable's, or an element's - none when it is outside its dimension, which
    // ObjectOperations reads no reference in.
    private LocalBuilder EmitObjectOperand(ObjectOperand operand, Label failure)
    {
        switch (operand)
        {
            case ObjectSlotOperand slot:
                return _objects[slot.Slot];

            case ObjectElementNode element:
                return EmitElementCellOf(element.Array, element.Subscripts, nameof(ArrayOperations.ObjectElement), null, failure, orNone: true);

            default:
                throw new InvalidOperationException($"No IL for {operand.GetType().Name}.");
        }
    }

    // the cell of an element of an array of a scalar kind, in a local: its subscripts evaluated first, none outside a dimension.
    private LocalBuilder EmitElementCell(ElementNode element, Label failure)
        => EmitElementCellOf(element.Array, element.Subscripts, nameof(ArrayOperations.ScalarElement), element.Kind, failure);

    // the cell ArrayOperations' find gives of the element at the subscripts, each evaluated first; none goes to failure, unless orNone keeps it.
    private LocalBuilder EmitElementCellOf(int array, IReadOnlyList<PayloadNode> elementSubscripts, string find, ScalarKind? kind, Label failure, bool orNone = false)
    {
        var subscripts = new Operand[elementSubscripts.Count];
        for (var i = 0; i < subscripts.Length; i++)
        {
            subscripts[i] = EmitPayload(elementSubscripts[i], failure);
        }

        var cell = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Ldloc, _arrays[array]);
        if (kind is { } scalar)
        {
            _il.Emit(OpCodes.Ldc_I4, (int)scalar);
        }

        _il.Emit(OpCodes.Ldc_I4, subscripts.Length);
        for (var i = 0; i < 3; i++)
        {
            if (i < subscripts.Length)
            {
                Load(subscripts[i], ScalarKind.Long);
            }
            else
            {
                _il.Emit(OpCodes.Ldc_I8, 0L);
            }
        }

        _il.Emit(OpCodes.Call, ArraySupport.GetMethod(find)!);
        _il.Emit(OpCodes.Stloc, cell);
        if (!orNone)
        {
            _il.Emit(OpCodes.Ldloc, cell);
            _il.Emit(OpCodes.Brfalse, failure);
        }

        return cell;
    }
}
