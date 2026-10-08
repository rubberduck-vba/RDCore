using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Runtime.Semantics.Conversion;

public static partial class ValueConversions
{
    /// <summary>
    /// Converts <paramref name="source"/> to a <c>Boolean</c> (<strong>MS-VBAL §5.5.1.2.2</strong>), from a
    /// <c>Boolean</c>, any numeric type, <c>Date</c>, <c>String</c> (<strong>§5.5.1.2.4</strong>) or
    /// <c>Empty</c> (<strong>§5.5.1.2.11</strong>).
    /// </summary>
    /// <param name="source">The value, unwrapped from any <c>Variant</c> that held it.</param>
    public static ValueConversionResult ToBoolean(VBTypedValue source) => source switch
    {
        VBBooleanValue boolean => ValueConversionResult.Success(new VBBooleanValue((bool)boolean.Value)),

        // MS-VBAL 5.5.1.2.2: "If the source value is 0, the result is False. Otherwise, the result is True."
        VBNumericTypedValue number => ValueConversionResult.Success(new VBBooleanValue(number.AsDouble != 0)),

        // MS-VBAL 5.5.1.2.3: a Date source converts via its standard Double (serial value) representation.
        VBDateValue date => ValueConversionResult.Success(new VBBooleanValue(date.SerialValue != 0)),

        VBStringValue text => StringToBoolean(text.Value),

        // MS-VBAL 5.5.1.2.11: "The result is False."
        VBEmptyValue => ValueConversionResult.Success(VBBooleanValue.False),

        VBNullValue => ValueConversionResult.Failure(VBRuntimeErrorId.InvalidUseOfNull),

        // MS-VBAL 5.5.1.2.9: an Error converts to nothing but a Variant or an Error.
        VBErrorValue => ValueConversionResult.Failure(VBRuntimeErrorId.TypeMismatch),

        _ => ValueConversionResult.NotApplicable,
    };

    // MS-VBAL 5.5.1.2.4: "True"/"False" are matched case-insensitive; "#TRUE#"/"#FALSE#" case-sensitive.
    // Otherwise, the result is the source string converted to Double, then converted to Boolean.
    private static ValueConversionResult StringToBoolean(string? text)
    {
        if (string.Equals(text, Tokens.True, StringComparison.InvariantCultureIgnoreCase)
            || string.Equals(text, "#TRUE#", StringComparison.InvariantCulture))
        {
            return ValueConversionResult.Success(VBBooleanValue.True);
        }

        if (string.Equals(text, Tokens.False, StringComparison.InvariantCultureIgnoreCase)
            || string.Equals(text, "#FALSE#", StringComparison.InvariantCulture))
        {
            return ValueConversionResult.Success(VBBooleanValue.False);
        }

        var number = StringToNumeric(text, VBDoubleType.TypeInfo);
        return number.Value is VBDoubleValue converted
            ? ValueConversionResult.Success(new VBBooleanValue(converted.Value != 0))
            : number;
    }
}
