using RDCore.Runtime.Semantics.Conversion;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.Semantics.Expressions;

/// <summary>
/// <strong>MS-VBAL 3.3.5.2</strong> the <c>LBound</c> and <c>UBound</c> special forms (runtime semantics) — the
/// smallest and the largest available subscript of one dimension of an array.
/// </summary>
/// <remarks>
/// MS-VBAL reserves the names and defines no semantics for them, so this is the behavior Microsoft documents for
/// the MS-VBA keywords: a <c>Long</c>, of the dimension counted from 1 and taken to be 1 when it is omitted; and,
/// where that documentation is silent, the behavior MS-VBA is observed to have — an array with no such dimension is
/// error 9, and a value that is not an array is error 13.
/// <para>
/// 👉 It reads <see cref="VBArrayValue.Dimensions"/> of the array it is given, and nothing else of it: the operand is
/// the array itself, as it is held, not a copy of it, however large — what it costs does not depend on the array's
/// size, and a test holds it to that.
/// </para>
/// </remarks>
public static class ArrayBoundRuntimeSemantics
{
    /// <summary>
    /// Evaluates <paramref name="node"/> given the values of its operands.
    /// </summary>
    /// <param name="node">The expression, which says which bound is read and where an error is reported.</param>
    /// <param name="array">The value of the array operand, as held by whatever it was read from.</param>
    /// <param name="dimension">The value of the dimension operand, or <see langword="null"/> when it was omitted.</param>
    /// <returns>The bound as a <c>Long</c>, or the error the construct raises.</returns>
    public static RuntimeSemanticsEvaluationResult Evaluate(ArrayBoundExpressionNode node, VBTypedValue array, VBTypedValue? dimension)
    {
        var keyword = node.Kind == ArrayBoundKind.Lower ? "LBound" : "UBound";

        if (Unwrapped(array) is not VBArrayValue held)
        {
            return Fail(node, VBRuntimeErrorId.TypeMismatch, $"{keyword} takes an array.");
        }

        var requested = 1;
        if (dimension is not null && Unwrapped(dimension) is not VBEmptyValue)
        {
            var converted = ValueConversions.ToNumeric(Unwrapped(dimension), VBLongType.TypeInfo);
            if (converted.Value is not VBLongValue number)
            {
                return Fail(node, converted.Error ?? VBRuntimeErrorId.TypeMismatch, "The dimension is not a number.");
            }

            requested = number.Value;
        }

        // a dynamic array that has not been sized has no dimensions to ask about, and neither does an array the
        // subscript is not a dimension of.
        if (!held.IsInitialized || requested < 1 || requested > held.Rank || !held.Dimensions[requested - 1].IsInitialized)
        {
            return Fail(node, VBRuntimeErrorId.SubscriptOutOfRange, held.IsInitialized
                ? $"{keyword}: the array has {held.Rank} dimension(s), not {requested}."
                : $"{keyword}: the array has no dimensions yet.");
        }

        var bounds = held.Dimensions[requested - 1];
        return RuntimeSemanticsEvaluationResult.Success(
            new VBLongValue(node.Kind == ArrayBoundKind.Lower ? bounds.LowerBound : bounds.UpperBound));
    }

    // a Variant holds whatever it was given, another Variant included, so this goes all the way down.
    private static VBTypedValue Unwrapped(VBTypedValue value)
    {
        while (value is VBVariantValue { TypedValue: var wrapped })
        {
            value = wrapped;
        }

        return value;
    }

    private static RuntimeSemanticsEvaluationResult Fail(ArrayBoundExpressionNode node, VBRuntimeErrorId id, string verbose)
        => RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(id, node.Location, verbose));
}
