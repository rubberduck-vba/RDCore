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
/// What it does not read or write (a holder that holds no record, a field that holds no payload of its type or no known text, a <c>Variant</c> field
/// over an object) is the interpreter's: each operation returns <see langword="false"/>, and the interpreter runs the statement.
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
    public static bool WriteIntegral(object? record, int index, ScalarKind kind, long value)
        => TryWrite(record, index, kind, new ScalarPayload(ScalarKinds.TypeOf(kind), value, 0));

    /// <summary>
    /// Let-assigns a payload of the field's floating-point kind to the field at <paramref name="index"/> of <paramref name="record"/>.
    /// </summary>
    public static bool WriteFloating(object? record, int index, ScalarKind kind, double value)
        => TryWrite(record, index, kind, new ScalarPayload(ScalarKinds.TypeOf(kind), 0, value));

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
    /// The text the <c>String</c> field of variable length at <paramref name="index"/> of <paramref name="record"/> holds, as the interpreter reads it
    /// (<c>RuntimeExpressionEvaluator.EvaluateInstanceField</c>).
    /// </summary>
    /// <param name="record">The record, as its cell holds it.</param>
    /// <param name="index">The field's place among the record's fields.</param>
    /// <param name="text">The text read; <see langword="null"/> for <c>vbNullString</c>, which reads as the zero-length string.</param>
    /// <returns>
    /// <see langword="false"/> when there is no such record, or the field holds no known text: the interpreter runs the statement.
    /// </returns>
    public static bool ReadText(object? record, int index, out string? text)
    {
        if ((record as VBUserDefinedTypeValue)?.StoredFieldAt(index) is VBStringValue { IsIndeterminate: false, RuntimeValue: VBRuntimeValue<string> held } field
            && field is not VBFixedStringValue)
        {
            text = held.StoredValue;
            return true;
        }

        text = null;
        return false;
    }

    /// <summary>
    /// Let-assigns <paramref name="text"/> to the <c>String</c> field of variable length at <paramref name="index"/> of <paramref name="record"/>
    /// (MS-VBAL §5.4.3.8): the field holds the String it is assigned, as it is, written in the field's cell, which a <c>ByRef</c> parameter given the field
    /// is bound to, as <c>AssignField</c> writes it (<see cref="VBUserDefinedTypeValue.TryLetFieldAt"/>).
    /// </summary>
    /// <param name="record">The record, as its cell holds it.</param>
    /// <param name="index">The field's place among the record's fields.</param>
    /// <param name="text">The text assigned; <see langword="null"/> for <c>vbNullString</c>.</param>
    /// <returns>
    /// <see langword="false"/> when there is no such record, or the field holds no String of variable length: the interpreter runs the statement.
    /// </returns>
    public static bool WriteText(object? record, int index, string? text)
        => record is VBUserDefinedTypeValue held && held.StoredFieldAt(index) is VBStringValue and not VBFixedStringValue
            && held.TryLetFieldAt(index, new VBStringValue(new ValueBindingHandle(new VBRuntimeValue<string>(text!))));

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

    // a field held as bytes of the kind's encoding, or as a payload (DeclaredVariableDefaults), is read where it is, with no value boxed; any other, of
    // the value it holds.
    private static bool TryRead(object? record, int index, ScalarKind kind, out ScalarPayload payload)
    {
        payload = default;
        if (record is not VBUserDefinedTypeValue held)
        {
            return false;
        }

        if (held.TryReadBytesField(index, EncodingOf(kind), out var integral, out var floating))
        {
            payload = new ScalarPayload(ScalarKinds.TypeOf(kind), integral, floating);
            return true;
        }

        if (held.StoredFieldAt(index) is not { } field)
        {
            return false;
        }

        return field.Handle is ScalarCell cell && ScalarKinds.Of(cell.Type) == kind
            ? cell.TryRead(out payload)
            : ScalarPayload.TryOf(ScalarKinds.TypeOf(kind), field.RuntimeValue, out payload);
    }

    // the field holds a value of its declared type, as the interpreter's Let makes it: a LongPtr field a LongPtr, of the payload of the type it is an alias of.
    // A field held as bytes of the kind's encoding is written in them, which is what letting it a value of its type writes, with no cell made of it.
    private static bool TryWrite(object? record, int index, ScalarKind kind, ScalarPayload payload)
    {
        if (record is not VBUserDefinedTypeValue held)
        {
            return false;
        }

        if (held.TryWriteBytesField(index, EncodingOf(kind), payload.Integral, payload.Floating))
        {
            return true;
        }

        if (held.StoredFieldAt(index) is not { } before)
        {
            return false;
        }

        // a field held as a payload of the payload's type is written where it is: the value is boxed only when something reads it as one.
        if (before.Handle is ScalarCell scalar && scalar.Type.Equals(payload.Type))
        {
            scalar.Write(payload);
            return true;
        }

        if (!ScalarPayload.TryOf(payload.Type, before.RuntimeValue, out _))
        {
            return false;
        }

        if (before.TypeInfo is VBLongPtrType_x64 or VBLongPtrType_x86)
        {
            return held.TryLetFieldAt(index, (payload with { Type = before.TypeInfo }).Typed());
        }

        // a field of the payload's own type is written in its cell, as TryLetFieldAt writes a value of the field's type: the value stored there, with no value
        // of the type made around it.
        if (before.Handle is IKnowledgeBinding cell && before.GetType() == ValueClassOf(payload.Type) && Equals(before.TypeInfo, payload.Type))
        {
            cell.Store(payload.Box(), isKnown: true);
            return true;
        }

        return held.TryLetFieldAt(index, payload.Typed());
    }

    // how a record holds a field of a kind as bytes (RecordShape): a Date as the Double that is its serial.
    private static RecordFieldEncoding EncodingOf(ScalarKind kind) => kind switch
    {
        ScalarKind.Byte => RecordFieldEncoding.Byte,
        ScalarKind.Integer => RecordFieldEncoding.Int16,
        ScalarKind.Long => RecordFieldEncoding.Int32,
        ScalarKind.LongLong => RecordFieldEncoding.Int64,
        ScalarKind.Boolean => RecordFieldEncoding.Boolean,
        ScalarKind.Currency => RecordFieldEncoding.Currency,
        ScalarKind.Single => RecordFieldEncoding.Single,
        ScalarKind.Double or ScalarKind.Date => RecordFieldEncoding.Double,
        _ => RecordFieldEncoding.None,
    };

    // the class of the value of a scalar type, as the type makes one (ScalarPayload.Typed): what a field of the type holds.
    private static Type? ValueClassOf(VBType type) => type switch
    {
        VBByteType => typeof(VBByteValue),
        VBIntegerType => typeof(VBIntegerValue),
        VBLongType => typeof(VBLongValue),
        VBLongLongType => typeof(VBLongLongValue),
        VBSingleType => typeof(VBSingleValue),
        VBDoubleType => typeof(VBDoubleValue),
        VBBooleanType => typeof(VBBooleanValue),
        VBCurrencyType => typeof(VBCurrencyValue),
        VBDateType => typeof(VBDateValue),
        _ => null,
    };
}
