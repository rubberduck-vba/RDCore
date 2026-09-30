using RDCore.Runtime.Semantics.Conversion;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.StdLib;

/// <inheritdoc cref="IStdConversionModule"/>
/// <remarks>
/// Every <c>CXxx</c> function is defined by the specification as its argument being Let-coerced to
/// <c>Xxx</c> (<strong>MS-VBAL §5.5.1.2</strong>). That is the <em>value</em> the function returns, not the
/// machinery of an assignment, so they get it from <see cref="ValueConversions"/> — the conversions Let-coercion
/// itself is built on — and are stateless.
/// <para>
/// 🚧 Only the functions that convert to a numeric type are implemented. Every other member returns the error
/// a member nothing implements returns. TODO <c>CBool</c>, <c>CDate</c>, <c>CStr</c> and the rest of the module
/// as their conversions are extracted. A <c>Null</c> or an object argument has no numeric conversion yet, so it
/// raises Type mismatch here rather than the error 94 or the default-member coercion the specification gives it.
/// </para>
/// </remarks>
public sealed class StdConversion : IStdConversionModule
{
    private static RuntimeSemanticsEvaluationResult<TValue> NotImplemented<TValue>(string member)
        where TValue : VBTypedValue
        => RuntimeSemanticsEvaluationResult<TValue>.Error(VBRuntimeErrorInfo.For(
            VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError, default,
            $"'{member}' is declared but not implemented yet."));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBBooleanValue> CBool(VBVariantValue expression)
        => NotImplemented<VBBooleanValue>(nameof(CBool));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBByteValue> CByte(VBVariantValue expression)
        => ToNumeric<VBByteValue>(expression, VBByteType.TypeInfo, errorAsCode: true);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBCurrencyValue> CCur(VBVariantValue expression)
        => ToNumeric<VBCurrencyValue>(expression, VBCurrencyType.TypeInfo, errorAsCode: true);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDateValue> CDate(VBVariantValue expression)
        => NotImplemented<VBDateValue>(nameof(CDate));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> CVDate(VBVariantValue expression)
        => NotImplemented<VBVariantValue>(nameof(CVDate));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> CDbl(VBVariantValue expression)
        => ToNumeric<VBDoubleValue>(expression, VBDoubleType.TypeInfo, errorAsCode: true);

    /// <inheritdoc/>
    /// <remarks>
    /// The one numeric conversion the specification gives no Error case. The declared return type is
    /// <c>Variant</c> because a <c>Decimal</c> can only be held in one.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> CDec(VBVariantValue expression)
    {
        var result = ToNumeric<VBDecimalValue>(expression, VBDecimalType.TypeInfo, errorAsCode: false);
        return result.IsSuccess
            ? RuntimeSemanticsEvaluationResult<VBVariantValue>.Success(new VBVariantValue(result.Result!))
            : RuntimeSemanticsEvaluationResult<VBVariantValue>.Error(result.ErrorInfo!);
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBIntegerValue> CInt(VBVariantValue expression)
        => ToNumeric<VBIntegerValue>(expression, VBIntegerType.TypeInfo, errorAsCode: true);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBLongValue> CLng(VBVariantValue expression)
        => ToNumeric<VBLongValue>(expression, VBLongType.TypeInfo, errorAsCode: true);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBLongLongValue> CLngLng(VBVariantValue expression)
        => ToNumeric<VBLongLongValue>(expression, VBLongLongType.TypeInfo, errorAsCode: true);

    /// <inheritdoc/>
    /// <remarks>TODO nothing in the runtime models a <c>LongPtr</c> yet.</remarks>
    public RuntimeSemanticsEvaluationResult<VBLongPtrValue> CLngPtr(VBVariantValue expression)
        => NotImplemented<VBLongPtrValue>(nameof(CLngPtr));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBSingleValue> CSng(VBVariantValue expression)
        => ToNumeric<VBSingleValue>(expression, VBSingleType.TypeInfo, errorAsCode: true);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> CStr(VBVariantValue expression)
        => NotImplemented<VBStringValue>(nameof(CStr));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> CVar(VBVariantValue expression)
        => NotImplemented<VBVariantValue>(nameof(CVar));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> CVErr(VBVariantValue expression)
        => NotImplemented<VBVariantValue>(nameof(CVErr));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Error(VBVariantValue? errorNumber = default)
        => NotImplemented<VBVariantValue>(nameof(Error));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> ErrorStr(VBVariantValue? errorNumber = default)
        => NotImplemented<VBStringValue>("Error$");

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Fix(VBVariantValue number)
        => NotImplemented<VBVariantValue>(nameof(Fix));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Hex(VBVariantValue number)
        => NotImplemented<VBVariantValue>(nameof(Hex));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> HexStr(VBVariantValue number)
        => NotImplemented<VBStringValue>("Hex$");

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Int(VBVariantValue number)
        => NotImplemented<VBVariantValue>(nameof(Int));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Oct(VBVariantValue number)
        => NotImplemented<VBVariantValue>(nameof(Oct));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> OctStr(VBVariantValue number)
        => NotImplemented<VBStringValue>("Oct$");

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Str(VBVariantValue number)
        => NotImplemented<VBVariantValue>(nameof(Str));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> StrStr(VBVariantValue number)
        => NotImplemented<VBStringValue>("Str$");

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> Val(VBStringValue value)
        => NotImplemented<VBDoubleValue>(nameof(Val));

    // a Variant holds whatever it was given, another Variant included, so this goes all the way down.
    private static VBTypedValue Unwrapped(VBVariantValue expression)
    {
        VBTypedValue value = expression;
        while (value is VBVariantValue { TypedValue: var wrapped })
        {
            value = wrapped;
        }

        return value;
    }

    // "If the value of Expression is an Error data value then return the [type] data value that is the result of
    // the Long error code of the Error data value being Let-coerced to [type]" - the error code stands in for the
    // Error, so `CInt(CVErr(13))` is 13, not a type mismatch.
    private static RuntimeSemanticsEvaluationResult<TValue> ToNumeric<TValue>(VBVariantValue expression, VBNumericType destination, bool errorAsCode)
        where TValue : VBTypedValue
    {
        var source = Unwrapped(expression);
        if (errorAsCode && source is VBErrorValue error)
        {
            source = new VBLongValue(error.Value);
        }

        var conversion = ValueConversions.ToNumeric(source, destination);
        if (conversion.IsSuccess && conversion.Value is TValue converted)
        {
            return RuntimeSemanticsEvaluationResult<TValue>.Success(converted);
        }

        // a pair of types with no conversion between them is what a Let-coercion reports as a type mismatch.
        var id = conversion.Error ?? VBRuntimeErrorId.TypeMismatch;
        return RuntimeSemanticsEvaluationResult<TValue>.Error(VBRuntimeErrorInfo.For(
            id, default, $"A value of type {source.TypeInfo.Name} cannot be converted to {destination.Name}."));
    }
}
