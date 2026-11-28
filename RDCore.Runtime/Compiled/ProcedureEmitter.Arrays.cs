using System.Reflection.Emit;

namespace RDCore.Runtime.Compiled;

// arrays. Each array parameter or variable compiled code reads is a local holding the cell its frame holds it in, fetched at the entry
// and again wherever every variable is read back (EmitReload); an element is found in the array that cell holds (kept from one access to the next while
// nothing can give the variable another array: KeepsArrays), and one that is not found (a subscript outside its dimension) is the interpreter's, at the
// instruction, which raises error 9.
internal sealed partial class ProcedureEmitter
{
    private static readonly Type ArraySupport = typeof(ArrayOperations);

    // the array slots, each by the local the cell its frame holds it in is held in.
    private readonly Dictionary<int, LocalBuilder> _arrays = [];

    // the array each array slot, and each Variant that holds one, holds, as compiled code keeps it with its shape (KeptArray): none until an element is
    // found in it, and none again after anything that may give the holder another array (KeepsArrays) and wherever every variable is read back. An array
    // field of a record is found through its record each time.
    private readonly Dictionary<int, LocalBuilder> _knownArrays = [];

    private void DeclareArrays()
    {
        foreach (var array in _analysis.Arrays)
        {
            _arrays[array.Slot] = _il.DeclareLocal(typeof(object));
            _knownArrays[array.Slot] = _il.DeclareLocal(typeof(KeptArray));
        }

        for (var i = 0; i < _analysis.HeldArrays.Count; i++)
        {
            if (_analysis.HeldArrays[i].Variant is not null)
            {
                _knownArrays[~i] = _il.DeclareLocal(typeof(KeptArray));
            }
        }
    }

    // whether the step being emitted keeps the arrays it finds (KeepsArrays).
    private bool _keepsArrays = true;

    // what an element is found in, onto the stack, for the operation of ArrayOperations named: the array compiled code keeps (by reference) and its holder,
    // for the operation that finds it there, whose name is the operation's prefixed with Kept; or the holder alone. A step that may give a holder another
    // array finds each in its holder. The name of the operation to call is returned.
    private string LoadArrayFor(int array, string operation)
    {
        if (!_keepsArrays || !_knownArrays.TryGetValue(array, out var known))
        {
            LoadArrayHolder(array);
            return operation;
        }

        _il.Emit(OpCodes.Ldloca, known);
        LoadArrayHolder(array);
        return "Kept" + operation;
    }

    // every array compiled code keeps, let go of: each is found in its holder again.
    private void EmitForgetArrays()
    {
        foreach (var known in _knownArrays.Values)
        {
            _il.Emit(OpCodes.Ldloca, known);
            _il.Emit(OpCodes.Initobj, typeof(KeptArray));
        }
    }

    // whether nothing a step does can give an array slot or a Variant another array: a Let of a scalar or of an element, a condition, a jump that leaves no
    // For Each, a For and its Next, none of which calls anything. Such a step keeps the arrays kept before it, and the ones it finds. What fails in one, the
    // interpreter runs, after which every variable is read back (EmitReload), and no array is kept.
    private bool KeepsArrays(CompiledStep step) => step switch
    {
        LetStep let => !MakesACall(let.Value),
        ElementLetStep let => !MakesACall(let.Value),
        VariantElementLetStep let => !MakesACall(let.Value),
        BranchStep branch => !MakesACall(branch.Condition),
        LoopBackStep loop => !MakesACall(loop.Condition),
        JumpStep jump => !_analysis.LeavesForEach(jump.Offset, jump.Target),
        ForOpenStep open => !MakesACall(open.Start) && !MakesACall(open.End) && !MakesACall(open.Step),
        ForNextStep => true,
        _ => false,
    };

    // each array's cell, as the frame holds it: none, for one the frame holds no array in yet, whose elements compiled code then finds none of.
    private void EmitArrayEntry()
    {
        foreach (var slot in _arrays.Keys)
        {
            EmitArrayCellOf(slot);
        }

        EmitForgetArrays();
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

    // the cell an array is held in, onto the stack: an array slot's local; the array an array field is, found through its record now, none when there is no
    // record; or the cell of the Variant that holds it. ArrayOperations finds nothing in what holds no array.
    private void LoadArrayHolder(int array)
    {
        if (array >= 0)
        {
            _il.Emit(OpCodes.Ldloc, _arrays[array]);
            return;
        }

        switch (_analysis.HeldArrays[~array])
        {
            case { Field: { } field }:
                LoadRecordOf(field);
                _il.Emit(OpCodes.Call, Records.GetMethod(nameof(RecordOperations.ArrayField))!);
                break;

            case { Variant: { } variant }:
                _il.Emit(OpCodes.Ldloc, _variants[variant]);
                break;

            default:
                throw new InvalidOperationException("A held array is held somewhere.");
        }
    }

    // the element a Let writes, when its value calls nothing: the element is found first and the value evaluated after it, after the calls of the subscripts,
    // as the interpreter evaluates them, where MS-VBAL §5.4.3.8 evaluates the value first (as ElementLetStep does for a value that calls). The same element
    // read in the value, at subscripts that are the same expressions, is then that cell. Nothing in between finds another array: the value calls nothing
    // (pure), so no variable a subscript reads changes, and the holder holds the same array. Subscripts are compared as the nodes they are; one made of a
    // list (an element in a subscript) is never the same, and its element is found again.
    private (int Array, IReadOnlyList<PayloadNode> Subscripts, LocalBuilder Cell, LocalBuilder? Index)? _found;

    private T EmitFoundIn<T>(int array, IReadOnlyList<PayloadNode> subscripts, LocalBuilder cell, LocalBuilder? index, bool pure, Func<T> emitValue)
    {
        var outer = _found;
        _found = pure ? (array, subscripts, cell, index) : null;
        try
        {
            return emitValue();
        }
        finally
        {
            _found = outer;
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
            // the value evaluated, then the element's cell found and the value written into it (MS-VBAL §5.4.3.8), as the interpreter evaluates the value
            // before it finds the element (AssignValue): after a call inside the value, the subscripts are read as the call left them. A value that calls
            // nothing is evaluated once the cell is found, so that the same element read in it is that cell (EmitFoundIn): after the calls of the subscripts,
            // which the interpreter makes before it reads what the value reads. A value that reads a Variant declines what compiled code does not evaluate, as
            // a variable's Let does (FailureOf): the interpreter runs the Let.
            case ElementLetStep let:
            {
                Operand value;
                LocalBuilder array, index;
                if (MakesACall(let.Value))
                {
                    value = EmitPayload(let.Value, FailureOf(let.Value, let.Offset));
                    (array, index) = EmitElement(let.Element, FailureAt(let.Offset));
                }
                else
                {
                    (array, index) = EmitElement(let.Element, FailureAt(let.Offset));
                    value = EmitFoundIn(let.Element.Array, let.Element.Subscripts, array, index, pure: true,
                        () => EmitPayload(let.Value, FailureOf(let.Value, let.Offset)));
                }

                _il.Emit(OpCodes.Ldloc, array);
                _il.Emit(OpCodes.Ldloc, index);
                _il.Emit(OpCodes.Ldc_I4, (int)let.Element.Kind);
                Load(value, let.Element.Kind);
                var write = ScalarKinds.IsIntegral(let.Element.Kind) ? nameof(ArrayOperations.WriteIntegral) : nameof(ArrayOperations.WriteFloating);
                _il.Emit(OpCodes.Call, ArraySupport.GetMethod(write)!);
                _il.Emit(OpCodes.Brfalse, Fail(FailureAt(let.Offset)));
                break;
            }

            // what the Variant element holds decides what its Let is, as a Variant variable's does (VariantLetStep): one the evaluation on payloads declines
            // (of a String, over an object) the interpreter runs alone, and compiled code goes on after it. The value and the element are evaluated in the
            // order of an element of a scalar type. Of the array a Variant holds, a Let whose subscripts call, as its value does, is the interpreter's
            // (ProcedureAnalysis.CallsAsAVariantAccessor).
            case VariantElementLetStep let:
            {
                var failure = RecoverAt(let.Offset);
                var value = _il.DeclareLocal(typeof(VariantPayload));
                LocalBuilder cell;
                if (MakesACall(let.Value))
                {
                    EmitVariantInto(let.Value, value, failure);
                    cell = EmitElementCellOf(let.Element.Array, let.Element.Subscripts, nameof(ArrayOperations.VariantElement), null, failure);
                }
                else
                {
                    cell = EmitElementCellOf(let.Element.Array, let.Element.Subscripts, nameof(ArrayOperations.VariantElement), null, failure);
                    EmitFoundIn(let.Element.Array, let.Element.Subscripts, cell, null, pure: true, () =>
                    {
                        EmitVariantInto(let.Value, value, failure);
                        return 0;
                    });
                }

                _il.Emit(OpCodes.Ldloc, cell);
                _il.Emit(OpCodes.Ldloc, value);
                _il.Emit(OpCodes.Call, Variants.GetMethod(nameof(VariantOperations.Write))!);
                _il.Emit(OpCodes.Brfalse, Fail(failure));
                break;
            }

            // the statement run by the interpreter, every variable written into the frame its bounds are read from; the error it raises is given to the
            // activation's handler as a call's is (CallFailedAt), the statement not run again. The cell of each array is found again after it; after one
            // that may have let go of an object, every variable is read back, as after a call (EmitReadBackAfterStatement), which finds each array's too.
            case ArrayStatementStep statement:
                EmitSpill();
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldc_I4, statement.Offset);
                _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.RunArrayStatement))!);
                _il.Emit(OpCodes.Brfalse, CallFailedAfterSpillAt(statement.Offset));
                if (statement.Releases)
                {
                    EmitReadBackAfterStatement(statement.Offset);
                    break;
                }

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
                var (array, index) = EmitElement(element, failure);
                var read = _il.DeclareLocal(ClrTypeOf(element.Kind));
                _il.Emit(OpCodes.Ldloc, array);
                _il.Emit(OpCodes.Ldloc, index);
                _il.Emit(OpCodes.Ldc_I4, (int)element.Kind);
                _il.Emit(OpCodes.Ldloca, read);
                var reader = ScalarKinds.IsIntegral(element.Kind) ? nameof(ArrayOperations.ReadIntegral) : nameof(ArrayOperations.ReadFloating);
                _il.Emit(OpCodes.Call, ArraySupport.GetMethod(reader)!);
                _il.Emit(OpCodes.Brfalse, Fail(failure));
                return new Operand(read, 0, 0);
            }

            case ArrayBoundNode bound:
            {
                var dimension = bound.Dimension is { } given ? EmitPayload(given, failure) : new Operand(null, 1, 0);
                var result = _il.DeclareLocal(typeof(long));
                var operation = LoadArrayFor(bound.Array, nameof(ArrayOperations.Bound));
                _il.Emit(bound.Upper ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                Load(dimension, ScalarKind.Long);
                _il.Emit(OpCodes.Ldloca, result);
                _il.Emit(OpCodes.Call, ArraySupport.GetMethod(operation)!);
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

                _il.Emit(OpCodes.Brfalse, Fail(failure));
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

            // IsEmpty, IsNull or VarType of a Variant, in its cell: what the cell does not say is the standard library's, which compiled code calls, or the
            // interpreter.
            case VariantInformationNode information:
            {
                var answer = _il.DeclareLocal(information.Function is VariantInformation.VarType ? typeof(long) : typeof(bool));
                var result = _il.DeclareLocal(typeof(long));
                var called = _il.DefineLabel();
                var done = _il.DefineLabel();
                _il.Emit(OpCodes.Ldloc, EmitVariantCellOf(information.Variant, failure));
                _il.Emit(OpCodes.Ldloca, answer);
                _il.Emit(OpCodes.Call, Variants.GetMethod(information.Function switch
                {
                    VariantInformation.IsEmpty => nameof(VariantOperations.IsEmpty),
                    VariantInformation.IsNull => nameof(VariantOperations.IsNull),
                    _ => nameof(VariantOperations.VarType),
                })!);
                _il.Emit(OpCodes.Brfalse, information.Library is null ? failure : called);
                _il.Emit(OpCodes.Ldloc, answer);
                if (information.Function is not VariantInformation.VarType)
                {
                    _il.Emit(OpCodes.Neg);
                    _il.Emit(OpCodes.Conv_I8);
                }

                _il.Emit(OpCodes.Stloc, result);
                _il.Emit(OpCodes.Br, done);
                _il.MarkLabel(called);
                if (information.Library is { } library)
                {
                    Load(EmitLibraryCall(library, failure), information.Kind);
                    _il.Emit(OpCodes.Stloc, result);
                }

                _il.MarkLabel(done);
                return new Operand(result, 0, 0);
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

    // the cell a Variant compiled code reads in its cell is held in, in a local: a Variant's of the activation, or an element's of an array of Variants.
    private LocalBuilder EmitVariantCellOf(VariantNode variant, Label failure) => variant is VariantElementNode element
        ? EmitElementCellOf(element.Array, element.Subscripts, nameof(ArrayOperations.VariantElement), null, failure)
        : _variants[((VariantSlotNode)variant).Slot];

    // the cell an object operand of Is or TypeOf is held in, in a local: an object variable's, a Variant's, or an element's, none when it is outside its
    // dimension, which ObjectOperations reads no reference in.
    private LocalBuilder EmitObjectOperand(ObjectOperand operand, Label failure)
    {
        switch (operand)
        {
            case ObjectSlotOperand slot:
                return _objects[slot.Slot];

            case VariantObjectOperand variant:
                return _variants[variant.Slot];

            case ObjectElementNode element:
                return EmitElementCellOf(element.Array, element.Subscripts, nameof(ArrayOperations.ObjectElement), null, failure, orNone: true);

            case ObjectFieldNode field:
                return EmitObjectFieldCell(field);

            case ObjectCallOperand returned:
                return EmitObjectCall(returned.Call, failure);

            default:
                throw new InvalidOperationException($"No IL for {operand.GetType().Name}.");
        }
    }

    // an element of an array of a scalar kind, in locals: the array, which holds it as bytes, and its flat index; its subscripts evaluated first, none
    // outside a dimension.
    private (LocalBuilder Array, LocalBuilder Index) EmitElement(ElementNode element, Label failure)
    {
        var array = EmitElementOf(element.Array, element.Subscripts, nameof(ArrayOperations.ScalarElement), element.Kind, failure, orNone: false,
            out var index);
        return (array, index!);
    }

    // the cell ArrayOperations' find gives of the element at the subscripts, each evaluated first; none goes to failure, unless orNone keeps it. After a call
    // inside the instruction, the interpreter is given what was evaluated (Fail), and the call is not made again.
    private LocalBuilder EmitElementCellOf(int array, IReadOnlyList<PayloadNode> elementSubscripts, string find, ScalarKind? kind, Label failure, bool orNone = false)
        => EmitElementOf(array, elementSubscripts, find, kind, failure, orNone, out _);

    // what the find gives, as EmitElementCellOf says: of an element of a scalar kind, the array, with the element's flat index in index.
    private LocalBuilder EmitElementOf(int array, IReadOnlyList<PayloadNode> elementSubscripts, string find, ScalarKind? kind, Label failure, bool orNone,
        out LocalBuilder? index)
    {
        if (_found is { } found && found.Array == array && found.Subscripts.SequenceEqual(elementSubscripts))
        {
            index = found.Index;
            return found.Cell;
        }

        var subscripts = new Operand[elementSubscripts.Count];
        for (var i = 0; i < subscripts.Length; i++)
        {
            subscripts[i] = EmitPayload(elementSubscripts[i], failure);
        }

        var cell = _il.DeclareLocal(typeof(object));
        var operation = LoadArrayFor(array, find);
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

        index = kind is null ? null : _il.DeclareLocal(typeof(int));
        if (index is not null)
        {
            _il.Emit(OpCodes.Ldloca, index);
        }

        _il.Emit(OpCodes.Call, ArraySupport.GetMethod(operation)!);
        _il.Emit(OpCodes.Stloc, cell);
        if (!orNone)
        {
            _il.Emit(OpCodes.Ldloc, cell);
            _il.Emit(OpCodes.Brfalse, Fail(failure));
        }

        return cell;
    }
}
