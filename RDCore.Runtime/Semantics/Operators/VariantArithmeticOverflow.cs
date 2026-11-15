using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Runtime.Semantics.Operators;

/// <summary>
/// <strong>MS-VBAL 5.6.9.3</strong>: what an arithmetic operation with a Variant operand yields when its value is outside the range of its effective type.
/// </summary>
/// <remarks>
/// <para>
/// "If one or both operands have a declared type of Variant": for an effective type of Integer, Long, Single or Double, "the operator's value type is the
/// narrowest type of either Integer, Long or Double such that the operator value is within the valid range of the type. If the result does not fit within
/// Double, runtime error 6 (Overflow) is raised." With no Variant operand, and for any other effective type, the overflow is error 6.
/// </para>
/// <para>
/// The specification lists no rule for a Byte effective type, and MS-VBA widens it as it does an Integer: a Variant Byte result that overflows is an Integer
/// (<c>200 + 100</c> = Integer 300), or wider (recorded in Excel). An integral effective type overflows by raising, which is what <see cref="Widens"/> is
/// for; a Single operation that leaves its range yields an infinity, as IEEE 754 arithmetic does, which <see cref="WidensSingle"/> is for.
/// </para>
/// </remarks>
internal static class VariantArithmeticOverflow
{
    /// <summary>
    /// Whether an operation that overflows its effective type is evaluated again, in a wider one.
    /// </summary>
    /// <param name="frame">The frame of the operation: what it says of its operands as they were given (<see cref="OperatorEvaluationFrame.HasVariantOperand"/>).</param>
    /// <param name="effectiveType">The effective type of the operation.</param>
    public static bool Widens(OperatorEvaluationFrame frame, VBType effectiveType)
        => frame.HasVariantOperand && effectiveType is VBByteType or VBIntegerType or VBLongType;

    /// <summary>
    /// Whether a Single operation whose value is not finite - outside the range of a Single - is evaluated again, in a wider type: with a Variant operand, a
    /// Double (<c>3E+38 * 10</c> = Double 3.00000000549776E+39), with no error even when that is not finite either (an infinity plus one is a Double
    /// infinity; observed in Excel).
    /// </summary>
    /// <param name="frame">The frame of the operation.</param>
    /// <param name="value">The value of the operation, as a Single.</param>
    public static bool WidensSingle(OperatorEvaluationFrame frame, float value)
        => frame.HasVariantOperand && !float.IsFinite(value);

    /// <summary>
    /// Whether a Double operation whose value is not finite is an overflow because an operand is a Variant: MS-VBA's Variant arithmetic raises 6 for any
    /// such Double - of an infinity, of a NaN - where a typed one is IEEE 754's, and assigns nothing (observed in Excel).
    /// </summary>
    /// <param name="frame">The frame of the operation.</param>
    /// <param name="value">The value of the operation.</param>
    public static bool OverflowsDouble(OperatorEvaluationFrame frame, double value)
        => frame.HasVariantOperand && !double.IsFinite(value);

    /// <summary>
    /// The value of an operation that overflowed its effective type, computed as a Double, in the narrowest of Integer, Long and Double that holds it; or
    /// <see langword="null"/> when no Double does, which is error 6.
    /// </summary>
    /// <param name="value">The value of the operation, computed as a Double.</param>
    public static VBTypedValue? Widened(double value) => value switch
    {
        >= short.MinValue and <= short.MaxValue => new VBIntegerValue((short)value),
        >= int.MinValue and <= int.MaxValue => new VBLongValue((int)value),
        _ when double.IsFinite(value) => new VBDoubleValue(value),
        _ => null,
    };
}
