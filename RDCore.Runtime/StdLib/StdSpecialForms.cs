using RDCore.Runtime.Semantics.Conversion;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.StdLib;

/// <inheritdoc cref="IStdSpecialFormsModule"/>
/// <remarks>
/// Every member is a function of its arguments alone, so this takes no session.
/// </remarks>
public sealed class StdSpecialForms : IStdSpecialFormsModule
{
    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBLongValue> LBound(VBVariantValue arrayName, VBVariantValue? dimension = default)
        => Bound(arrayName, dimension, nameof(LBound), bounds => bounds.LowerBound);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBLongValue> UBound(VBVariantValue arrayName, VBVariantValue? dimension = default)
        => Bound(arrayName, dimension, nameof(UBound), bounds => bounds.UpperBound);

    // one lookup with one dial: the two functions differ in which end of a dimension they read.
    private static RuntimeSemanticsEvaluationResult<VBLongValue> Bound(
        VBVariantValue arrayName, VBVariantValue? dimension, string function, Func<VBArrayValue.VBArrayDimension, int> end)
    {
        if (Unwrapped(arrayName) is not VBArrayValue array)
        {
            return Fail(VBRuntimeErrorId.TypeMismatch, $"{function} takes an array.");
        }

        if (!TryReadDimension(dimension, out var requested, out var error))
        {
            return error;
        }

        // a dynamic array that has not been sized has no dimensions to ask about, and neither does one the
        // subscript is not a dimension of.
        if (!array.IsInitialized || requested < 1 || requested > array.Rank
            || !array.Dimensions[requested - 1].IsInitialized)
        {
            return Fail(VBRuntimeErrorId.SubscriptOutOfRange,
                array.IsInitialized
                    ? $"{function}: the array has {array.Rank} dimension(s), not {requested}."
                    : $"{function}: the array has no dimensions yet.");
        }

        return RuntimeSemanticsEvaluationResult<VBLongValue>.Success(new VBLongValue(end(array.Dimensions[requested - 1])));
    }

    // "1 when unspecified" - and an argument the caller left out arrives as null, or as the Empty an omitted
    // Optional Variant is filled in with, which is the same thing.
    private static bool TryReadDimension(
        VBVariantValue? dimension, out int requested, out RuntimeSemanticsEvaluationResult<VBLongValue> error)
    {
        requested = 1;
        error = default;
        if (dimension is null || Unwrapped(dimension) is VBEmptyValue)
        {
            return true;
        }

        var converted = ValueConversions.ToNumeric(Unwrapped(dimension), VBLongType.TypeInfo);
        if (converted.Value is VBLongValue number)
        {
            requested = number.Value;
            return true;
        }

        error = Fail(converted.Error ?? VBRuntimeErrorId.TypeMismatch, "The dimension is not a number.");
        return false;
    }

    // a Variant holds whatever it was given, another Variant included, so this goes all the way down.
    private static VBTypedValue Unwrapped(VBVariantValue value)
    {
        VBTypedValue unwrapped = value;
        while (unwrapped is VBVariantValue { TypedValue: var wrapped })
        {
            unwrapped = wrapped;
        }

        return unwrapped;
    }

    private static RuntimeSemanticsEvaluationResult<VBLongValue> Fail(VBRuntimeErrorId id, string verbose)
        => RuntimeSemanticsEvaluationResult<VBLongValue>.Error(VBRuntimeErrorInfo.For(id, default, verbose));
}
