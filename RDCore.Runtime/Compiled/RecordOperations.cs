using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using System.Runtime.CompilerServices;

namespace RDCore.Runtime.Compiled;

/// <summary>
/// What compiled code does of a field of a record: it reads and writes the field where the record holds it, as the interpreter does
/// (<c>RuntimeExpressionEvaluator.EvaluateInstanceField</c>, <c>LetAssignmentEvaluator.AssignField</c>).
/// </summary>
/// <remarks>
/// <para>
/// Compiled code holds the cell its frame holds the record in (its <em>holder</em>) and nothing below it: each access finds the record the holder holds
/// now, then the field. Compiled code then sees what the interpreter did to the record in between: a Let of the whole record writes a copy of another
/// into it (<see cref="VBUserDefinedTypeValue.TryLetFrom"/>), and a Let of one of its fields writes the field's cell.
/// </para>
/// <para>
/// What it does not read or write (a holder that holds no record, a field that holds no payload of its type, a <c>Variant</c> field over an object) is the
/// interpreter's: each operation returns <see langword="false"/>, and the interpreter runs the statement.
/// </para>
/// </remarks>
public static class RecordOperations
{
    /// <summary>
    /// The record held in <paramref name="holder"/>, the cell its frame holds a record variable in; <see langword="null"/> when it holds none.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object? RecordIn(object? holder)
        => holder is IBindingHandle { Value: VBRuntimeValue<VBRuntimeUserDefinedTypeValue> boxed } ? boxed.StoredValue.UserDefinedType : null;

    /// <summary>
    /// The record the field at <paramref name="index"/> of <paramref name="record"/> holds, a field declared as a record type: the field itself, whose own
    /// fields are written in place (MS-VBAL §2.1). <see langword="null"/> when it holds none.
    /// </summary>
    public static object? FieldRecord(object? record, int index) => (record as VBUserDefinedTypeValue)?.StoredFieldAt(index) as VBUserDefinedTypeValue;

    /// <summary>
    /// The payload the field at <paramref name="index"/> of <paramref name="record"/>, of an integral kind, holds.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ReadIntegral(object? record, int index, ScalarKind kind, out long value)
    {
        var read = TryRead(record, index, kind, out var payload);
        value = payload.Integral;
        return read;
    }

    /// <summary>
    /// The payload the field at <paramref name="index"/> of <paramref name="record"/>, of a floating-point kind, holds.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ReadFloating(object? record, int index, ScalarKind kind, out double value)
    {
        var read = TryRead(record, index, kind, out var payload);
        value = payload.Floating;
        return read;
    }

    /// <summary>
    /// Let-assigns a payload of the field's integral kind to the field at <paramref name="index"/> of <paramref name="record"/> (MS-VBAL §5.4.3.8). The payload is
    /// written in the field's cell, which a <c>ByRef</c> parameter given the field is bound to, as <c>AssignField</c> writes it
    /// (<see cref="VBUserDefinedTypeValue.TryLetFieldAt"/>).
    /// </summary>
    public static bool WriteIntegral(object? record, int index, ScalarKind kind, long value) => TryWrite(record, index, new ScalarPayload(ScalarKinds.TypeOf(kind), value, 0));

    /// <summary>
    /// Let-assigns a payload of the field's floating-point kind to the field at <paramref name="index"/> of <paramref name="record"/>.
    /// </summary>
    public static bool WriteFloating(object? record, int index, ScalarKind kind, double value) => TryWrite(record, index, new ScalarPayload(ScalarKinds.TypeOf(kind), 0, value));

    /// <summary>
    /// The payload the <c>Variant</c> field at <paramref name="index"/> of <paramref name="record"/> holds, when it holds a known value of a scalar type.
    /// </summary>
    public static bool ReadVariant(object? record, int index, out VariantPayload value)
    {
        value = default;
        if ((record as VBUserDefinedTypeValue)?.StoredFieldAt(index) is not { RuntimeValue: VBRuntimeVariantValue { WrappedValue: { IsIndeterminate: false } held } }
            || !ScalarPayload.IsScalar(held.TypeInfo) || !ScalarPayload.TryOf(held.TypeInfo, held.RuntimeValue, out var payload))
        {
            return false;
        }

        value = new VariantPayload(payload);
        return true;
    }

    /// <summary>
    /// Let-assigns <paramref name="value"/> to the <c>Variant</c> field at <paramref name="index"/> of <paramref name="record"/>, of its own subtype
    /// (MS-VBAL §5.5.1.2.12): not over an object, an array or a record, which the interpreter's assignment lets go of (<c>ObjectReferences.RebindField</c>).
    /// </summary>
    public static bool WriteVariant(object? record, int index, VariantPayload value)
    {
        if (record is not VBUserDefinedTypeValue held || held.StoredFieldAt(index) is not { RuntimeValue: VBRuntimeVariantValue { WrappedValue: var before } }
            || ObjectReferences.CanHoldAnObject(before?.TypeInfo))
        {
            return false;
        }

        return held.TrySetFieldAt(index, VBVariantType.TypeInfo.CreateValue(new ValueBindingHandle(VariantCell.Holding(value.Value.Type, value.Value.Box()))));
    }

    /// <summary>
    /// The cell the object field at <paramref name="index"/> of <paramref name="record"/> holds its object in: the cell the object is counted as held by, which
    /// a <c>Set</c> of the field stores in (<c>LetAssignmentEvaluator.SetField</c>), and which <see cref="ObjectOperations"/> reads the reference in.
    /// <see langword="null"/> when there is no such record.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object? ObjectField(object? record, int index) => (record as VBUserDefinedTypeValue)?.StoredFieldAt(index)?.Handle;

    /// <summary>
    /// The array the array field at <paramref name="index"/> of <paramref name="record"/> is: the record stores the array itself, in no cell of its own,
    /// and <see cref="ArrayOperations"/> finds an element and the bounds in it, as it does in the array an array variable's cell holds. <see langword="null"/>
    /// when there is no such record.
    /// </summary>
    /// <param name="record">The record, as its cell holds it.</param>
    /// <param name="index">The field's place among the record's fields.</param>
    /// <returns>The array, or <see langword="null"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object? ArrayField(object? record, int index) => (record as VBUserDefinedTypeValue)?.StoredFieldAt(index) as VBArrayValue;

    private static bool TryRead(object? record, int index, ScalarKind kind, out ScalarPayload payload)
    {
        payload = default;
        return (record as VBUserDefinedTypeValue)?.StoredFieldAt(index) is { } field && ScalarPayload.TryOf(ScalarKinds.TypeOf(kind), field.RuntimeValue, out payload);
    }

    // the field holds a value of its declared type, as the interpreter's Let makes it: a LongPtr field a LongPtr, of the payload of the type it is an alias of.
    private static bool TryWrite(object? record, int index, ScalarPayload payload)
        => record is VBUserDefinedTypeValue held && held.StoredFieldAt(index) is { } before && ScalarPayload.TryOf(payload.Type, before.RuntimeValue, out _)
            && held.TryLetFieldAt(index, (before.TypeInfo is VBLongPtrType_x64 or VBLongPtrType_x86 ? payload with { Type = before.TypeInfo } : payload).Typed());
}
