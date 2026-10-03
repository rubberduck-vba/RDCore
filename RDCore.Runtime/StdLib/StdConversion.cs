using RDCore.Runtime.Semantics.Conversion;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.Shared;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RDCore.Runtime.StdLib;

/// <inheritdoc cref="IStdConversionModule"/>
/// <remarks>
/// Every <c>CXxx</c> function is defined by the specification as its argument being Let-coerced to
/// <c>Xxx</c> (<strong>MS-VBAL §5.5.1.2</strong>). That is the <em>value</em> the function returns, not the
/// machinery of an assignment, so they get it from <see cref="ValueConversions"/> — the conversions Let-coercion
/// itself is built on.
/// <para>
/// 🚧 <see cref="CLngPtr"/> is not implemented, because nothing in the runtime models a <c>LongPtr</c> yet, and an
/// object argument raises Type mismatch rather than being read through its default member
/// (<strong>MS-VBAL §5.5.1.2.13</strong>): that is an invocation, which a conversion is not. TODO both.
/// </para>
/// <para>
/// 👉 The specification's text for <see cref="Fix"/> says the result is "the smallest integer greater than or
/// equal to" the argument, which is what <see cref="Int"/>'s mirror image would be for a negative number and is
/// wrong for a positive one; the function's own description is "the integer portion of a number", and that is what
/// is implemented. Likewise <see cref="Oct"/> describes its positive result as "hexadecimal", which is a
/// transcription of <see cref="Hex"/>'s text. <see cref="Val"/> reads <c>&amp;H</c> and <c>&amp;O</c> digits as
/// the number they spell; MS-VBA's wrap of a short literal into a negative <c>Integer</c> is not specified.
/// </para>
/// </remarks>
/// <param name="session">The session whose most recently raised error <see cref="Error"/> reports.</param>
public sealed partial class StdConversion(IRuntimeSession session) : IStdConversionModule
{
    // MS-VBAL 6.1.2.3.1.14: "If the resulting data value is not in the inclusive range 0 to 65535, Error 5 is raised."
    private const int LargestErrorCode = 65535;

    // MS-VBAL 6.1.2.3.1.15: "Otherwise, the data value is "Application-defined or object-defined error.""
    private const string UnknownErrorText = "Application-defined or object-defined error.";

    private static RuntimeSemanticsEvaluationResult<TValue> Fail<TValue>(VBRuntimeErrorId id, string verbose)
        where TValue : VBTypedValue
        => RuntimeSemanticsEvaluationResult<TValue>.Error(VBRuntimeErrorInfo.For(id, default, verbose));

    private static RuntimeSemanticsEvaluationResult<TValue> NotImplemented<TValue>(string member)
        where TValue : VBTypedValue
        => Fail<TValue>(VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError, $"'{member}' is declared but not implemented yet.");

    private static RuntimeSemanticsEvaluationResult<TValue> Succeed<TValue>(TValue value)
        where TValue : VBTypedValue
        => RuntimeSemanticsEvaluationResult<TValue>.Success(value);

    private static RuntimeSemanticsEvaluationResult<VBVariantValue> InVariant<TValue>(RuntimeSemanticsEvaluationResult<TValue> result)
        where TValue : VBTypedValue
        => result.IsSuccess
            ? Succeed(new VBVariantValue(result.Result!))
            : RuntimeSemanticsEvaluationResult<VBVariantValue>.Error(result.ErrorInfo!);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBBooleanValue> CBool(VBVariantValue expression)
        => Convert<VBBooleanValue>(expression, ValueConversions.ToBoolean, nameof(Boolean), errorAsCode: true);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBByteValue> CByte(VBVariantValue expression)
        => ToNumeric<VBByteValue>(expression, VBByteType.TypeInfo, errorAsCode: true);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBCurrencyValue> CCur(VBVariantValue expression)
        => ToNumeric<VBCurrencyValue>(expression, VBCurrencyType.TypeInfo, errorAsCode: true);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDateValue> CDate(VBVariantValue expression)
        => Unwrapped(expression) is VBErrorValue
            // MS-VBAL 6.1.2.3.1.4: "If the value of Expression is an Error data value then raise error 13".
            ? Fail<VBDateValue>(VBRuntimeErrorId.TypeMismatch, "An error value cannot be converted to a Date.")
            : Convert<VBDateValue>(expression, ValueConversions.ToDate, "Date", errorAsCode: false);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> CVDate(VBVariantValue expression)
        => InVariant(CDate(expression));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> CDbl(VBVariantValue expression)
        => ToNumeric<VBDoubleValue>(expression, VBDoubleType.TypeInfo, errorAsCode: true);

    /// <inheritdoc/>
    /// <remarks>
    /// The one numeric conversion the specification gives no Error case. The declared return type is
    /// <c>Variant</c> because a <c>Decimal</c> can only be held in one.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> CDec(VBVariantValue expression)
        => InVariant(ToNumeric<VBDecimalValue>(expression, VBDecimalType.TypeInfo, errorAsCode: false));

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
    public RuntimeSemanticsEvaluationResult<VBLongPtrValue> CLngPtr(VBVariantValue expression)
        => NotImplemented<VBLongPtrValue>(nameof(CLngPtr));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBSingleValue> CSng(VBVariantValue expression)
        => ToNumeric<VBSingleValue>(expression, VBSingleType.TypeInfo, errorAsCode: true);

    /// <inheritdoc/>
    /// <remarks>
    /// An Error is the one argument that is not simply converted: "the String data value consisting of
    /// <c>Error</c> followed by a single space character followed by the String that is the result of the Long
    /// error code … Let-coerced to String" (<strong>MS-VBAL §6.1.2.3.1.12</strong>).
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBStringValue> CStr(VBVariantValue expression)
        => Unwrapped(expression) is VBErrorValue error
            ? Succeed(new VBStringValue($"Error {ErrorCodeText(error)}"))
            : Convert<VBStringValue>(expression, ValueConversions.ToText, nameof(String), errorAsCode: false);

    /// <inheritdoc/>
    /// <remarks>"The argument data value is returned" (<strong>MS-VBAL §6.1.2.3.1.13</strong>).</remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> CVar(VBVariantValue expression)
        => Succeed(expression);

    /// <inheritdoc/>
    /// <remarks>
    /// An Error argument is returned as it is. Anything else is converted to <c>Long</c> for use as the error
    /// code, which has to be in 0 to 65535 (<strong>MS-VBAL §6.1.2.3.1.14</strong>).
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> CVErr(VBVariantValue expression)
    {
        if (Unwrapped(expression) is VBErrorValue)
        {
            return Succeed(expression);
        }

        var code = ToNumeric<VBLongValue>(expression, VBLongType.TypeInfo, errorAsCode: false);
        if (!code.IsSuccess)
        {
            return RuntimeSemanticsEvaluationResult<VBVariantValue>.Error(code.ErrorInfo!);
        }

        return code.Result!.Value is < 0 or > LargestErrorCode
            ? Fail<VBVariantValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument,
                $"CVErr takes an error code from 0 to {LargestErrorCode}, not {code.Result.Value}.")
            : Succeed(new VBVariantValue(new VBErrorValue(code.Result.Value)));
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Error(VBVariantValue? errorNumber = default)
        => InVariant(ErrorText(errorNumber));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> ErrorStr(VBVariantValue? errorNumber = default)
        => ErrorText(errorNumber);

    // MS-VBAL 6.1.2.3.1.15: the error code is the argument's, Let-coerced to Long, or else "the most recently raised
    // error number (or 0 if no error has been raised)" - which "might not necessarily be the same as the current
    // value of Err.Number", so it is the error that was raised that is read rather than the number source may
    // have assigned. An argument the caller left out arrives as null or as Empty, and is the same thing.
    private RuntimeSemanticsEvaluationResult<VBStringValue> ErrorText(VBVariantValue? errorNumber)
    {
        int code;
        if (errorNumber is null || Unwrapped(errorNumber) is VBEmptyValue)
        {
            code = session.Errors.Current?.ErrorId ?? 0;
        }
        else
        {
            var converted = ToNumeric<VBLongValue>(errorNumber, VBLongType.TypeInfo, errorAsCode: false);
            if (!converted.IsSuccess)
            {
                return RuntimeSemanticsEvaluationResult<VBStringValue>.Error(converted.ErrorInfo!);
            }

            code = converted.Result!.Value;
        }

        // "If the resulting data value is greater than 65,535 then Error 6 is raised. Negative values … are acceptable."
        if (code > LargestErrorCode)
        {
            return Fail<VBStringValue>(VBRuntimeErrorId.Overflow, $"{code} is not an error number.");
        }

        var text = code == 0
            ? string.Empty
            : VBRuntimeErrorInfo.VBRuntimeErrors.TryGetValue((VBRuntimeErrorId)code, out var known) ? known : UnknownErrorText;
        return Succeed(new VBStringValue(text));
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Fix(VBVariantValue number)
        => Integral(number, floor: false);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Int(VBVariantValue number)
        => Integral(number, floor: true);

    // MS-VBAL 6.1.2.3.1.16 and 6.1.2.3.1.18 are one function in two roundings: Fix drops the fraction, Int goes
    // down to the next integer, and they agree for everything but a negative fraction.
    private RuntimeSemanticsEvaluationResult<VBVariantValue> Integral(VBVariantValue number, bool floor)
    {
        var source = Unwrapped(number);
        switch (source)
        {
            case VBNullValue:
                // "If the data value of Number is Null, Null is returned."
                return Succeed(new VBVariantValue(VBNullValue.Null));

            // "If the value type of Number is Integer, Long or LongLong, the data value of Number is returned."
            case VBIntegerValue or VBLongValue or VBLongLongValue:
                return Succeed(new VBVariantValue(source));

            case VBDecimalValue @decimal:
                return Succeed(new VBVariantValue(new VBDecimalValue(Round(@decimal.Value, floor))));

            case VBCurrencyValue currency:
                return Succeed(new VBVariantValue(new VBCurrencyValue(Round(System.Convert.ToDecimal(currency.RuntimeValue.BoxedValue), floor))));

            // "the returned value is a data value whose value type is the same as the value type of Number".
            case VBNumericTypedValue numeric:
                return Succeed(new VBVariantValue(((VBNumericType)numeric.TypeInfo).CreateValue(Round(numeric.AsDouble, floor))));

            // "the returned value is the same as result of evaluating the expression: CDate(Int(CDbl(Number)))".
            case VBDateValue date:
                return Succeed(new VBVariantValue(new VBDateValue(Round(date.SerialValue, floor))));

            // "the returned value is the result of the Int function applied to the result of Let-coercing Number to Double".
            case VBStringValue:
                var text = ToNumeric<VBDoubleValue>(number, VBDoubleType.TypeInfo, errorAsCode: false);
                return text.IsSuccess
                    ? Succeed(new VBVariantValue(new VBDoubleValue(Round(text.Result!.Value, floor))))
                    : RuntimeSemanticsEvaluationResult<VBVariantValue>.Error(text.ErrorInfo!);

            // "Otherwise, the returned value is the result of Number being Let-coerced to Integer."
            default:
                return InVariant(ToNumeric<VBIntegerValue>(number, VBIntegerType.TypeInfo, errorAsCode: false));
        }
    }

    private static double Round(double value, bool floor) => floor ? Math.Floor(value) : Math.Truncate(value);

    private static decimal Round(decimal value, bool floor) => floor ? Math.Floor(value) : Math.Truncate(value);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Hex(VBVariantValue number)
        => Radix(number, 16);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> HexStr(VBVariantValue number)
        => RadixText(number, 16);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Oct(VBVariantValue number)
        => Radix(number, 8);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> OctStr(VBVariantValue number)
        => RadixText(number, 8);

    // "If the data value of the parameter Number is the data value Null the function Hex$ raises error 94,
    // "Invalid use of Null" and the function Hex returns the data value Null."
    private RuntimeSemanticsEvaluationResult<VBVariantValue> Radix(VBVariantValue number, int radix)
        => Unwrapped(number) is VBNullValue
            ? Succeed(new VBVariantValue(VBNullValue.Null))
            : InVariant(RadixText(number, radix));

    private static RuntimeSemanticsEvaluationResult<VBStringValue> RadixText(VBVariantValue number, int radix)
    {
        var source = Unwrapped(number);
        switch (source)
        {
            case VBNullValue:
                return Fail<VBStringValue>(VBRuntimeErrorId.InvalidUseOfNull, "Hex$ and Oct$ take no Null.");

            // "If the data value of the parameter Number is the data value Empty the function returns the String data value "0"."
            case VBEmptyValue:
                return Succeed(new VBStringValue("0"));

            // "If the data value of the parameter Number has the value type LongLong, it is not coerced."
            case VBLongLongValue longLong:
                return Succeed(new VBStringValue(Digits(longLong.Value, radix, wide: true)));
        }

        // "If the data value of the parameter Number is any other value, it is Let-coerced to Long".
        var converted = ToNumeric<VBLongValue>(number, VBLongType.TypeInfo, errorAsCode: false);
        return converted.IsSuccess
            ? Succeed(new VBStringValue(Digits(converted.Result!.Value, radix, wide: false)))
            : RuntimeSemanticsEvaluationResult<VBStringValue>.Error(converted.ErrorInfo!);
    }

    // a positive value is its digits with no leading zeros; a negative one is its two's complement, in the
    // narrowest of the 16, 32 and 64 bit widths the specification gives a range to: -32,767 to -1, then down to
    // -2,147,483,648, then the rest. An argument that was a LongLong is never narrowed below 64 bits.
    private static string Digits(long value, int radix, bool wide) => (value, wide) switch
    {
        ( >= 0, _) => System.Convert.ToString(value, radix).ToUpperInvariant(),
        ( >= -32767, false) => System.Convert.ToString((short)value, radix).ToUpperInvariant(),
        ( >= int.MinValue, false) => System.Convert.ToString((int)value, radix).ToUpperInvariant(),
        _ => System.Convert.ToString(value, radix).ToUpperInvariant(),
    };

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Str(VBVariantValue number)
        => Unwrapped(number) is VBNullValue
            // "If the data value of Number is Null, Null is returned."
            ? Succeed(new VBVariantValue(VBNullValue.Null))
            : InVariant(StrStr(number));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> StrStr(VBVariantValue number)
    {
        var source = Unwrapped(number);
        switch (source)
        {
            case VBNullValue:
                return Fail<VBStringValue>(VBRuntimeErrorId.InvalidUseOfNull, "Str$ takes no Null.");

            case VBErrorValue error:
                return Succeed(new VBStringValue($"Error {ErrorCodeText(error)}"));

            // "If the value type of Number is Date, the returned value is the result of Let-coercing Number to String."
            case VBDateValue:
                return Convert<VBStringValue>(number, ValueConversions.ToText, nameof(String), errorAsCode: false);

            // "If the data value of Number is any numeric value type, let S be the result of Let-coercing Number to
            // String using "." as the decimal separator" - which the conversion does, in the invariant culture.
            case VBNumericTypedValue numeric:
                return Text(numeric, numeric.AsDouble >= 0);
        }

        // "Otherwise, the returned value is the result of the Str function applied to the result of Let-coercing
        // Number to Double."
        var asDouble = ToNumeric<VBDoubleValue>(number, VBDoubleType.TypeInfo, errorAsCode: false);
        return asDouble.IsSuccess
            ? Text(asDouble.Result!, asDouble.Result!.Value >= 0)
            : RuntimeSemanticsEvaluationResult<VBStringValue>.Error(asDouble.ErrorInfo!);
    }

    // "If the data value of Number is positive (or zero) the result is S with a single space character appended as
    // its first character, otherwise the result is S."
    private static RuntimeSemanticsEvaluationResult<VBStringValue> Text(VBTypedValue number, bool nonNegative)
    {
        var text = ValueConversions.ToText(number);
        return text.Value is VBStringValue s
            ? Succeed(new VBStringValue(nonNegative ? " " + s.Value : s.Value))
            : Fail<VBStringValue>(text.Error ?? VBRuntimeErrorId.TypeMismatch, "The number has no text.");
    }

    // MS-VBAL 6.1.2.3.1.12: the String that is the result of the Long error code Let-coerced to String.
    private static string ErrorCodeText(VBErrorValue error)
        => ValueConversions.ToText(new VBLongValue(error.Value)).Value is VBStringValue code ? code.Value : string.Empty;

    [GeneratedRegex(@"^[+-]?(?:[0-9]+\.?[0-9]*|\.[0-9]+)(?:[eEdD][+-]?[0-9]+)?", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingNumber();

    /// <inheritdoc/>
    /// <remarks>
    /// "Returns the numbers contained in a string as a Double. The Val function stops reading the string at the
    /// first character it can't recognize as part of a number … Blanks, tabs, and linefeed characters are
    /// stripped from the argument" (<strong>MS-VBAL §6.1.2.3.1.21</strong>).
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> Val(VBStringValue value)
    {
        var text = new string([.. (value.Value ?? string.Empty).Where(c => c is not (' ' or '\t' or '\n' or '\r'))]);

        // "If Value is the 0 length String data value return the Double data value 0."
        if (text.Length == 0)
        {
            return Succeed(new VBDoubleValue(0));
        }

        // "the function recognizes the radix prefixes &O (for octal) and &H (for hexadecimal)."
        if (text.Length >= 2 && text[0] == '&' && char.ToUpperInvariant(text[1]) is 'H' or 'O')
        {
            return Succeed(new VBDoubleValue(Prefixed(text[2..], char.ToUpperInvariant(text[1]) == 'H' ? 16 : 8)));
        }

        var match = LeadingNumber().Match(text);
        if (!match.Success)
        {
            return Succeed(new VBDoubleValue(0));
        }

        var number = double.Parse(
            match.Value.Replace('d', 'e').Replace('D', 'e'),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture);
        return double.IsInfinity(number)
            ? Fail<VBDoubleValue>(VBRuntimeErrorId.Overflow, $"'{match.Value}' is not a Double.")
            : Succeed(new VBDoubleValue(number));
    }

    private static double Prefixed(string digits, int radix)
    {
        var total = 0d;
        foreach (var digit in digits)
        {
            var place = digit is >= '0' and <= '9' ? digit - '0'
                : char.ToUpperInvariant(digit) is >= 'A' and <= 'F' ? char.ToUpperInvariant(digit) - 'A' + 10
                : radix;
            if (place >= radix)
            {
                break;
            }

            total = total * radix + place;
        }

        return total;
    }

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

    private static RuntimeSemanticsEvaluationResult<TValue> ToNumeric<TValue>(VBVariantValue expression, VBNumericType destination, bool errorAsCode)
        where TValue : VBTypedValue
        => Convert<TValue>(expression, source => ValueConversions.ToNumeric(source, destination), destination.Name, errorAsCode);

    // "If the value of Expression is an Error data value then return the [type] data value that is the result of
    // the Long error code of the Error data value being Let-coerced to [type]" - the error code stands in for the
    // Error, so `CInt(CVErr(13))` is 13, not a type mismatch.
    private static RuntimeSemanticsEvaluationResult<TValue> Convert<TValue>(
        VBVariantValue expression, Func<VBTypedValue, ValueConversionResult> conversion, string destinationName, bool errorAsCode)
        where TValue : VBTypedValue
    {
        var source = Unwrapped(expression);
        if (errorAsCode && source is VBErrorValue error)
        {
            source = new VBLongValue(error.Value);
        }

        var converted = conversion(source);
        if (converted.IsSuccess && converted.Value is TValue value)
        {
            return Succeed(value);
        }

        // a pair of types with no conversion between them is what a Let-coercion reports as a type mismatch.
        return Fail<TValue>(converted.Error ?? VBRuntimeErrorId.TypeMismatch,
            $"A value of type {source.TypeInfo.Name} cannot be converted to {destinationName}.");
    }
}
