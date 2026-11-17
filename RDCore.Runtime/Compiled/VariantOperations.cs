using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using System.Runtime.CompilerServices;

namespace RDCore.Runtime.Compiled;

/// <summary>
/// What a <c>Variant</c> holds when it holds a value of a scalar type, as compiled code holds it: the payload, of the Variant's subtype,
/// which is known only when it is read.
/// </summary>
/// <remarks>
/// It is of a type a <c>Variant</c> holds, never a <c>LongPtr</c> (<see cref="ScalarPayload.HeldByAVariant"/>): one of compiled code's kinds, what a
/// Variant held, or what an operation or a call made of those. It is written into a Variant as it is (<see cref="VariantCell.WriteHeld"/>).
/// </remarks>
public readonly struct VariantPayload
{
    internal VariantPayload(ScalarPayload value) => Value = value;

    internal ScalarPayload Value { get; }

    /// <summary>
    /// A payload of <paramref name="kind"/>, as a Variant holds it: of its own subtype (MS-VBAL §5.5.1.2.12).
    /// </summary>
    /// <param name="kind">The subtype of the payload.</param>
    /// <param name="integral">The value, when <paramref name="kind"/> is integral.</param>
    /// <param name="floating">The value, when <paramref name="kind"/> is floating-point.</param>
    /// <returns>The payload.</returns>
    public static VariantPayload Of(ScalarKind kind, long integral, double floating) => new(new ScalarPayload(ScalarKinds.TypeOf(kind), integral, floating));
}

/// <summary>
/// The operations compiled code does on a <c>Variant</c>: each is the interpreter's own on payloads (the cell the frame holds the
/// Variant in read and written, an operation planned for the subtypes of its operands), and declines where the interpreter's evaluation on payloads
/// declines, which hands the instruction to the interpreter.
/// </summary>
/// <remarks>
/// A Variant variable stays in the cell its frame holds it in (<see cref="VariantCell"/>): compiled code holds the cell, and has nothing of it to write
/// into the frame or to read back from it when the interpreter runs an instruction.
/// </remarks>
public static class VariantOperations
{
    /// <summary>
    /// The payload the Variant held in <paramref name="cell"/> holds, when it holds one: a known value of a scalar type. A Variant its frame holds in no
    /// Variant's cell (<see langword="null"/>, <see cref="CompiledActivation.VariantCellOf"/>) holds none compiled code reads.
    /// </summary>
    /// <param name="cell">The cell of the Variant; <see langword="null"/> when its frame holds it in no Variant's cell.</param>
    /// <param name="value">The payload read.</param>
    /// <returns>Whether the Variant holds a payload compiled code reads.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Read(object? cell, out VariantPayload value)
    {
        if (cell is null)
        {
            value = default;
            return false;
        }

        // a cell these operations are given is null or a VariantCell, by construction: only the IL ProcedureEmitter emits calls them, with what
        // CompiledActivation.VariantCellOf found for a Variant slot, so the cast is not checked.
        var read = Unsafe.As<VariantCell>(cell).TryRead(out var payload);
        value = new VariantPayload(payload);
        return read;
    }

    /// <summary>
    /// <c>IsEmpty</c> of the Variant held in <paramref name="cell"/> (MS-VBAL §6.1.2.7.1.4): whether it holds <c>Empty</c>. A Variant that holds a value of
    /// a scalar type, a <c>String</c>, <c>Null</c> or an error does not.
    /// </summary>
    /// <param name="cell">The cell of the Variant; <see langword="null"/> when its frame holds it in no Variant's cell.</param>
    /// <param name="empty">Whether the Variant holds <c>Empty</c>.</param>
    /// <returns>
    /// <see langword="false"/> when the answer is the standard library's: the Variant holds anything else (an object, whose default member's value
    /// <c>IsEmpty</c> looks at, or an array), its value is not known, or its frame holds it in no Variant's cell.
    /// </returns>
    public static bool IsEmpty(object? cell, out bool empty)
    {
        empty = false;
        if (cell is null || Unsafe.As<VariantCell>(cell) is not { IsKnown: true } variant)
        {
            return false;
        }

        if (variant.TryRead(out _))
        {
            return true;
        }

        switch (variant.Value)
        {
            case VBRuntimeVariantValue { WrappedValue: VBEmptyValue }:
                empty = true;
                return true;
            case VBRuntimeVariantValue { WrappedValue: VBStringValue or VBNullValue or VBErrorValue }:
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// What the Variant held in <paramref name="cell"/> holds, given by value to a <c>Variant</c> parameter of a function of the standard library as the
    /// interpreter gives it (MS-VBAL §5.3.1.11): a value of a scalar type, <c>Empty</c>, <c>Null</c>, a <c>String</c>, an error (<c>Missing</c> among
    /// them) or an object. It is what the Variant holds when it is read: an assignment to the Variant after it gives the Variant another value and leaves
    /// this one as it is.
    /// </summary>
    /// <param name="cell">The cell of the Variant; <see langword="null"/> when its frame holds it in no Variant's cell.</param>
    /// <returns>
    /// The value; <see langword="null"/> when the Variant holds an array or a record, which a later assignment to an element or a field would change in
    /// place, when its value is not known, or when its frame holds it in no Variant's cell. The interpreter is given the call.
    /// </returns>
    public static IRuntimeValue? ValueOf(object? cell)
    {
        if (cell is null)
        {
            return null;
        }

        var variant = Unsafe.As<VariantCell>(cell);
        return variant.IsKnown && (variant.HoldsNoObject || variant.Value is VBRuntimeVariantValue { WrappedValue.TypeInfo: not (VBArrayType or VBUserDefinedType) })
            ? variant.Value
            : null;
    }

    /// <summary>
    /// The text the Variant held in <paramref name="cell"/> is let-coerced to as a <c>String</c>, as the interpreter's let-coercion writes it
    /// (<see cref="TextOf"/>).
    /// </summary>
    /// <param name="cell">The cell of the Variant; <see langword="null"/> when its frame holds it in no Variant's cell.</param>
    /// <param name="text">The text; <see langword="null"/> for <c>vbNullString</c>.</param>
    /// <returns>
    /// <see langword="false"/> when the interpreter let-coerces it: the Variant holds anything <see cref="TextOf"/> writes no text of, its value is not
    /// known, or its frame holds it in no Variant's cell.
    /// </returns>
    public static bool ReadText(object? cell, out string? text)
    {
        text = null;
        if (cell is null || Unsafe.As<VariantCell>(cell) is not { IsKnown: true } variant)
        {
            return false;
        }

        return variant.TryRead(out var payload) ? TryWriteWhole(payload, out text) : TextOf(variant.Value, out text);
    }

    /// <summary>
    /// The text a Variant that holds <paramref name="value"/> is let-coerced to as a <c>String</c>, as the interpreter's let-coercion writes it: a copy of
    /// the <c>String</c> it holds (<strong>MS-VBAL §5.5.1.2.4</strong>), the zero-length string of <c>Empty</c> (<strong>MS-VBAL §5.5.1.2.11</strong>),
    /// and the digits of a <c>Byte</c>, an <c>Integer</c>, a <c>Long</c> or a <c>LongLong</c>, after a minus sign when it is negative, which no regional
    /// setting changes (<see cref="StringOperations.OfWhole"/>).
    /// </summary>
    /// <param name="value">What the Variant holds (<see cref="CompiledActivation.VariantValueOf"/>); <see langword="null"/> when it is not known.</param>
    /// <param name="text">The text; <see langword="null"/> for <c>vbNullString</c>.</param>
    /// <returns>
    /// <see langword="false"/> when the interpreter let-coerces it: the Variant holds <c>Null</c>, which raises error 94
    /// (<strong>MS-VBAL §5.5.1.2.10</strong>), a <c>Date</c>, a <c>Boolean</c> or any other number, written in the regional settings of the session, an
    /// error or an object; or its value is not known.
    /// </returns>
    public static bool TextOf(IRuntimeValue? value, out string? text)
    {
        text = null;
        switch (value)
        {
            case VBRuntimeVariantValue { WrappedValue: VBEmptyValue }:
                text = string.Empty;
                return true;
            case VBRuntimeVariantValue { WrappedValue: VBStringValue { IsIndeterminate: false, RuntimeValue: VBRuntimeValue<string> held } }:
                text = held.StoredValue;
                return true;
            case VBRuntimeVariantValue { WrappedValue: { IsIndeterminate: false } scalar }
                when ScalarPayload.IsScalar(scalar.TypeInfo) && ScalarPayload.TryOf(scalar.TypeInfo, scalar.RuntimeValue, out var payload):
                return TryWriteWhole(payload, out text);
            default:
                return false;
        }
    }

    // a whole number's digits (StringOperations.OfWhole); none of any other payload.
    private static bool TryWriteWhole(ScalarPayload payload, out string? text)
    {
        text = ScalarKinds.Of(payload.Type) is ScalarKind.Byte or ScalarKind.Integer or ScalarKind.Long or ScalarKind.LongLong
            ? StringOperations.OfWhole(payload.Integral)
            : null;
        return text is not null;
    }

    /// <summary>
    /// Let-assigns <paramref name="value"/> to the Variant held in <paramref name="cell"/>, as the interpreter's assignment on payloads does: not over an
    /// object, an array or a record, which the assignment lets go of; nor into a Variant its frame holds in no Variant's cell (<see cref="Read"/>).
    /// </summary>
    /// <param name="cell">The cell of the Variant; <see langword="null"/> when its frame holds it in no Variant's cell.</param>
    /// <param name="value">The payload assigned.</param>
    /// <returns>Whether the payload was assigned: when not, the interpreter runs the assignment.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Write(object? cell, VariantPayload value) => WritePayload(cell, value.Value);

    /// <summary>
    /// Let-assigns the <c>String</c> <paramref name="text"/> to the Variant held in <paramref name="cell"/>, which then holds a copy of it
    /// (<strong>MS-VBAL §5.5.1.2.12</strong>), as the interpreter's assignment makes it: not over an object, an array or a record, which the assignment lets
    /// go of; nor into a Variant its frame holds in no Variant's cell (<see cref="Read"/>).
    /// </summary>
    /// <param name="cell">The cell of the Variant; <see langword="null"/> when its frame holds it in no Variant's cell.</param>
    /// <param name="text">The text assigned; <see langword="null"/> for <c>vbNullString</c>.</param>
    /// <returns>Whether the text was assigned: when not, the interpreter runs the assignment.</returns>
    public static bool WriteText(object? cell, string? text)
        => Store(cell, new VBRuntimeVariantValue(VBStringType.TypeInfo.VarType(), new VBStringValue(text!)));

    /// <summary>
    /// Let-assigns <c>Null</c> to the Variant held in <paramref name="cell"/>, which then holds it (<strong>MS-VBAL §5.5.1.2.12</strong>), as the
    /// interpreter's assignment makes it: not over an object, an array or a record; nor into a Variant its frame holds in no Variant's cell.
    /// </summary>
    /// <param name="cell">The cell of the Variant; <see langword="null"/> when its frame holds it in no Variant's cell.</param>
    /// <returns>Whether <c>Null</c> was assigned: when not, the interpreter runs the assignment.</returns>
    public static bool WriteNull(object? cell) => Store(cell, new VBRuntimeVariantValue(VBNullType.TypeInfo.VarType(), new VBNullValue()));

    // a String or Null, stored in the Variant's cell when it holds no object, array or record (VariantCell.HoldsNoObject), as the interpreter's
    // assignment stores it.
    private static bool Store(object? cell, VBRuntimeVariantValue value)
    {
        if (cell is null || !Unsafe.As<VariantCell>(cell).HoldsNoObject)
        {
            return false;
        }

        Unsafe.As<VariantCell>(cell).Store(value, isKnown: true);
        return true;
    }

    /// <summary>
    /// A binary operation of <paramref name="left"/> and <paramref name="right"/>, planned for their subtypes (MS-VBAL §5.6.9).
    /// </summary>
    /// <param name="token">The operator's token.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <param name="result">The operation's result.</param>
    /// <returns><see langword="false"/> when it is the interpreter's: it raises an error, or its result is no payload.</returns>
    public static bool Binary(string token, VariantPayload left, VariantPayload right, out VariantPayload result)
    {
        var done = RuntimeExpressionEvaluator.TryOperateDeferred(token, left.Value, right.Value, out var payload);
        result = new VariantPayload(payload);
        return done;
    }

    /// <summary>
    /// A negation or a <c>Not</c> of <paramref name="operand"/>, planned for its subtype.
    /// </summary>
    /// <param name="token">The operator's token.</param>
    /// <param name="operand">The operand.</param>
    /// <param name="ofAConstantExpression">Whether the operation is part of a constant expression.</param>
    /// <param name="result">The operation's result.</param>
    /// <returns><see langword="false"/> when it is the interpreter's, as <see cref="Binary"/> says.</returns>
    public static bool Unary(string token, VariantPayload operand, bool ofAConstantExpression, out VariantPayload result)
    {
        var done = RuntimeExpressionEvaluator.TryOperateDeferred(token, operand.Value, ofAConstantExpression, out var payload);
        result = new VariantPayload(payload);
        return done;
    }

    /// <summary>
    /// The payload the Variant held in <paramref name="cell"/> holds, when it holds one of the subtype <paramref name="kind"/>, an integral one: what compiled
    /// code reads a Variant as where the analysis speculates its subtype (<c>VariantSpeculation</c>). Any other is read by
    /// <see cref="Read"/>.
    /// </summary>
    /// <param name="cell">The cell of the Variant; <see langword="null"/> when its frame holds it in no Variant's cell.</param>
    /// <param name="kind">The integral subtype speculated.</param>
    /// <param name="value">The payload's value.</param>
    /// <returns>Whether the Variant holds a payload of <paramref name="kind"/>.</returns>
    public static bool ReadIntegralAs(object? cell, ScalarKind kind, out long value)
    {
        if (cell is not null && Unsafe.As<VariantCell>(cell).TryRead(out var payload) && ReferenceEquals(payload.Type, TypeOf[(int)kind]))
        {
            value = payload.Integral;
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>
    /// The payload of a Variant of the subtype <paramref name="kind"/>, a floating-point one (<see cref="ReadIntegralAs"/>).
    /// </summary>
    /// <param name="cell">The cell of the Variant; <see langword="null"/> when its frame holds it in no Variant's cell.</param>
    /// <param name="kind">The floating-point subtype speculated.</param>
    /// <param name="value">The payload's value.</param>
    /// <returns>Whether the Variant holds a payload of <paramref name="kind"/>.</returns>
    public static bool ReadFloatingAs(object? cell, ScalarKind kind, out double value)
    {
        if (cell is not null && Unsafe.As<VariantCell>(cell).TryRead(out var payload) && ReferenceEquals(payload.Type, TypeOf[(int)kind]))
        {
            value = payload.Floating;
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>
    /// Let-assigns the Variant held in <paramref name="cell"/> a payload of <paramref name="kind"/>, an integral one, as <see cref="Write"/> does.
    /// </summary>
    /// <param name="cell">The cell of the Variant; <see langword="null"/> when its frame holds it in no Variant's cell.</param>
    /// <param name="kind">The integral subtype of the payload.</param>
    /// <param name="value">The payload's value.</param>
    /// <returns>Whether the payload was assigned: when not, the interpreter runs the assignment.</returns>
    public static bool WriteIntegralAs(object? cell, ScalarKind kind, long value) => WritePayload(cell, new ScalarPayload(TypeOf[(int)kind], value, 0));

    /// <summary>
    /// Let-assigns the Variant held in <paramref name="cell"/> a payload of <paramref name="kind"/>, a floating-point one, as <see cref="Write"/> does.
    /// </summary>
    /// <param name="cell">The cell of the Variant; <see langword="null"/> when its frame holds it in no Variant's cell.</param>
    /// <param name="kind">The floating-point subtype of the payload.</param>
    /// <param name="value">The payload's value.</param>
    /// <returns>Whether the payload was assigned: when not, the interpreter runs the assignment.</returns>
    public static bool WriteFloatingAs(object? cell, ScalarKind kind, double value) => WritePayload(cell, new ScalarPayload(TypeOf[(int)kind], 0, value));

    // RuntimeExpressionEvaluator.TryWritePayload's Variant: over what holds no object. The cell is null or a VariantCell, by construction (Read). What
    // compiled code writes is of a type a Variant holds (VariantPayload), so it is written as it is.
    private static bool WritePayload(object? cell, ScalarPayload payload)
    {
        if (cell is null || !Unsafe.As<VariantCell>(cell).HoldsNoObject)
        {
            return false;
        }

        Unsafe.As<VariantCell>(cell).WriteHeld(payload);
        return true;
    }

    // the type of each kind, by its number: what a payload of the kind is of.
    private static readonly SDK.Model.Types.Abstract.VBType[] TypeOf = TypesOfKinds();

    // the kinds are numbered from 0, one after the other: each element is the type of one.
    private static SDK.Model.Types.Abstract.VBType[] TypesOfKinds()
    {
        var kinds = Enum.GetValues<ScalarKind>();
        var types = new SDK.Model.Types.Abstract.VBType[kinds.Max(kind => (int)kind) + 1];
        foreach (var kind in kinds)
        {
            types[(int)kind] = ScalarKinds.TypeOf(kind);
        }

        return types;
    }

    /// <summary>
    /// <paramref name="value"/> let-coerced to <paramref name="kind"/>, when the conversion is exact.
    /// </summary>
    /// <param name="value">The payload converted.</param>
    /// <param name="kind">The kind it is converted to.</param>
    /// <param name="integral">The converted value, when <paramref name="kind"/> is integral.</param>
    /// <param name="floating">The converted value, when <paramref name="kind"/> is floating-point.</param>
    /// <returns>Whether the conversion is exact: when not, the interpreter converts it.</returns>
    public static bool Convert(VariantPayload value, ScalarKind kind, out long integral, out double floating)
    {
        var converted = RuntimeExpressionEvaluator.TryConvertExactly(value.Value, ScalarKinds.TypeOf(kind), out var payload);
        (integral, floating) = (payload.Integral, payload.Floating);
        return converted;
    }

    /// <summary>
    /// Whether <paramref name="value"/> is not zero: what it is let-coerced to a <c>Boolean</c> as (MS-VBAL §5.5.1.2.2).
    /// </summary>
    /// <param name="value">The payload tested.</param>
    /// <returns>Whether it is not zero.</returns>
    public static bool IsTrue(VariantPayload value) => value.Value.IsTrue;
}
