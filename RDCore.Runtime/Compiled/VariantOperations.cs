using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics;
using System.Runtime.CompilerServices;

namespace RDCore.Runtime.Compiled;

/// <summary>
/// What a <c>Variant</c> holds when it holds a value of a scalar type, as compiled code holds it (IR lowering S2.5c): the payload, of the Variant's subtype,
/// which is known only when it is read.
/// </summary>
public readonly struct VariantPayload
{
    internal VariantPayload(ScalarPayload value) => Value = value;

    internal ScalarPayload Value { get; }

    /// <summary>A payload of <paramref name="kind"/>, as a Variant holds it: of its own subtype (MS-VBAL §5.5.1.2.12).</summary>
    public static VariantPayload Of(ScalarKind kind, long integral, double floating) => new(new ScalarPayload(ScalarKinds.TypeOf(kind), integral, floating));
}

/// <summary>
/// The operations compiled code does on a <c>Variant</c> (IR lowering S2.5c): each is the interpreter's own on payloads - the cell the frame holds the
/// Variant in read and written, an operation planned for the subtypes of its operands - and declines where the interpreter's evaluation on payloads
/// declines, which hands the instruction to the interpreter.
/// </summary>
/// <remarks>
/// A Variant variable stays in the cell its frame holds it in (<see cref="VariantCell"/>): compiled code holds the cell, and has nothing of it to write
/// into the frame or to read back from it when the interpreter runs an instruction.
/// </remarks>
public static class VariantOperations
{
    /// <summary>The payload the Variant held in <paramref name="cell"/> holds, when it holds one: a known value of a scalar type.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Read(object cell, out VariantPayload value)
    {
        var read = Unsafe.As<VariantCell>(cell).TryRead(out var payload);
        value = new VariantPayload(payload);
        return read;
    }

    /// <summary>
    /// Let-assigns <paramref name="value"/> to the Variant held in <paramref name="cell"/>, as the interpreter's assignment on payloads does: not over an
    /// object, an array or a record, which the assignment lets go of.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Write(object cell, VariantPayload value) => RuntimeExpressionEvaluator.TryWriteVariant(Unsafe.As<VariantCell>(cell), value.Value);

    /// <summary>A binary operation of <paramref name="left"/> and <paramref name="right"/>, planned for their subtypes (MS-VBAL §5.6.9).</summary>
    /// <returns><see langword="false"/> when it is the interpreter's: it raises an error, or its result is no payload.</returns>
    public static bool Binary(string token, VariantPayload left, VariantPayload right, out VariantPayload result)
    {
        var done = RuntimeExpressionEvaluator.TryOperateDeferred(token, left.Value, right.Value, out var payload);
        result = new VariantPayload(payload);
        return done;
    }

    /// <summary>A negation or a <c>Not</c> of <paramref name="operand"/>, planned for its subtype.</summary>
    public static bool Unary(string token, VariantPayload operand, bool ofAConstantExpression, out VariantPayload result)
    {
        var done = RuntimeExpressionEvaluator.TryOperateDeferred(token, operand.Value, ofAConstantExpression, out var payload);
        result = new VariantPayload(payload);
        return done;
    }

    /// <summary><paramref name="value"/> let-coerced to <paramref name="kind"/>, when the conversion is exact.</summary>
    public static bool Convert(VariantPayload value, ScalarKind kind, out long integral, out double floating)
    {
        var converted = RuntimeExpressionEvaluator.TryConvertExactly(value.Value, ScalarKinds.TypeOf(kind), out var payload);
        (integral, floating) = (payload.Integral, payload.Floating);
        return converted;
    }

    /// <summary>Whether <paramref name="value"/> is not zero: what it is let-coerced to a <c>Boolean</c> as (MS-VBAL §5.5.1.2.2).</summary>
    public static bool IsTrue(VariantPayload value) => value.Value.IsTrue;
}
