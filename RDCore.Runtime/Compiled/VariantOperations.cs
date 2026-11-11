using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics;
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
    /// Let-assigns <paramref name="value"/> to the Variant held in <paramref name="cell"/>, as the interpreter's assignment on payloads does: not over an
    /// object, an array or a record, which the assignment lets go of; nor into a Variant its frame holds in no Variant's cell (<see cref="Read"/>).
    /// </summary>
    /// <param name="cell">The cell of the Variant; <see langword="null"/> when its frame holds it in no Variant's cell.</param>
    /// <param name="value">The payload assigned.</param>
    /// <returns>Whether the payload was assigned: when not, the interpreter runs the assignment.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Write(object? cell, VariantPayload value) => WritePayload(cell, value.Value);

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
