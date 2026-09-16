using RDCore.Runtime.Semantics.Abstract;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Services.VerboseMessages;
using System.Globalization;

namespace RDCore.Runtime.Semantics.LetCoercion;

/// <summary>
/// MS-VBAL 5.5.1.2.4 Let-coercion to and from <c>VBStringType</c>
/// </summary>
/// <remarks>
/// The coercion provider dispatches by <em>destination</em> type only, so this class — registered for
/// <see cref="VBStringType"/> — only ever runs when the destination actually is String; it therefore
/// implements the "-&gt; String" half of MS-VBAL 5.5.1.2.4's table (String, any numeric type, Boolean
/// or Date source). The "String -&gt;" half (String as source, coercing to a numeric, Boolean or Date
/// destination) is dispatched by those destination types instead, and lives in
/// <see cref="VBNumericLetCoercionTypeRuntimeSemantics"/>, <see cref="VBBooleanLetCoercionRuntimeSemantics"/>
/// and <see cref="VBDateLetCoercionRuntimeSemantics"/> respectively, for that same reason.
/// </remarks>
public record class VBStringLetCoercionRuntimeSemantics(
    IVerboseMessageBuilder FormatterService)
    : LetCoercionRuntimeSemantics<VBStringType>(FormatterService)
{
    public override LetCoercionResult EvaluateLetCoercion(
        ISymbolResolver resolver,
        VBOperatorExpression expression,
        LetCoercionStackFrame frame)
    {
        var cultureInfo = CultureInfo.InvariantCulture;
        return frame.SourceValue switch
        {
            VBStringValue stringSourceValue when frame.DestinationTypeDesc.Target is VBStringType
                // result is a copy of the source string.
                => LetCoercionResult.Success(new VBStringValue(stringSourceValue.Value)),

            VBNumericTypedValue numericSourceValue when frame.DestinationTypeDesc.Target is VBStringType
                => CoerceToVBString(numericSourceValue, cultureInfo),

            VBBooleanValue booleanSourceValue when frame.DestinationTypeDesc.Target is VBStringType
                => LetCoercionResult.Success(
                    new VBStringValue((bool)booleanSourceValue.Value
                        ? Tokens.True
                        : Tokens.False)),

            // MS-VBAL 5.5.1.2.4: "If the day value of the source date is 12/30/1899" — the date
            // component only, regardless of the time-of-day fraction, hence comparing .Date rather
            // than requiring the exact zero serial value.
            VBDateValue dateSourceValue when frame.DestinationTypeDesc.Target is VBStringType
                => LetCoercionResult.Success(
                    new VBStringValue(dateSourceValue.Value.Date == VBDateType.Zero.Value.Date
                        ? dateSourceValue.Value.ToLongTimeString()
                        : dateSourceValue.Value.ToShortDateString())),

            _ => LetCoercionResult.NotApplicable(frame)
        };
    }

    protected override ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation(
        ILetCoercionSemanticContextBuilder builder,
        ISymbolResolver resolver,
        VBOperatorExpression expression,
        LetCoercionStackFrame frame)
    {
        throw new NotImplementedException();
    }

    private static LetCoercionResult CoerceToVBString(VBNumericTypedValue value, CultureInfo cultureInfo)
    {
        // BoxedValue's CLR type tracks the source's own numeric type (int for Long, short for
        // Integer, ...), not always double — a direct (double) unboxing cast throws for anything
        // that isn't already a boxed double. AsDouble widens through Convert.ToDouble instead.
        var numericValue = value.AsDouble;
        if (numericValue == 0)
        {
            return LetCoercionResult.Success(new VBStringValue(VBStringValue.Zero));
        }
        if (double.IsPositiveInfinity(numericValue))
        {
            return LetCoercionResult.Success(new VBStringValue(VBStringValue.PositiveInfinity));
        }
        if (double.IsNegativeInfinity(numericValue))
        {
            return LetCoercionResult.Success(new VBStringValue(VBStringValue.NegativeInfinity));
        }
        if (double.IsNaN(numericValue))
        {
            return LetCoercionResult.Success(new VBStringValue(VBStringValue.NaN));
        }

        var sign = numericValue < 0 ? "-" : string.Empty;
        var absoluteValue = Math.Abs(numericValue);
        var dot = cultureInfo.NumberFormat.NumberDecimalSeparator;

        // MS-VBAL 5.5.1.2.4: scientific notation is used whenever the integer part has more than the
        // source type's maximum significant integral digits, regardless of whether the value also has
        // a fractional part. "F0" (fixed-point, no fractional digits) reliably yields just the integer
        // part without .NET's default ToString() collapsing a very large/small magnitude into its own
        // scientific notation first (which would otherwise hide the decimal separator this method used
        // to gate on, undercounting whole-number values entirely).
        var integerPartDigitCount = absoluteValue < 1 ? 1 : absoluteValue.ToString("F0", cultureInfo).Length;
        var significantIntegerDigits = value is VBSingleValue
            ? VBSingleType.SignificantIntegerDigits
            : VBDoubleType.SignificantIntegerDigits;

        var stringValue = integerPartDigitCount > significantIntegerDigits
            ? ToVBScientificNotation(numericValue, significantIntegerDigits, dot, cultureInfo)
            : $"{sign}{absoluteValue.ToString(cultureInfo)}";

        return LetCoercionResult.Success(new VBStringValue(stringValue));
    }

    private static string ToVBScientificNotation(double value, int significantIntegerDigits, string decimalSeparator, CultureInfo cultureInfo)
    {
        var sign = value < 0 ? "-" : string.Empty;
        var absoluteValue = Math.Abs(value);

        // .NET's own "E" format always yields exactly one digit before the decimal point (s * 10^e),
        // regardless of the source's magnitude or whether it has a fractional part — reliably giving
        // the significand and exponent without needing to first locate a decimal separator that may
        // not even be present (a whole-number source) or that .NET's default ToString() may already
        // have collapsed into its own scientific notation. 16 fractional digits comfortably covers a
        // double's ~15-17 significant decimal digits of precision.
        var scientific = absoluteValue.ToString("E16", CultureInfo.InvariantCulture);
        var eIndex = scientific.IndexOf('E');
        var exponent = int.Parse(scientific[(eIndex + 1)..], CultureInfo.InvariantCulture);
        var s = scientific[0];

        // MS-VBAL 5.5.1.2.4: "a maximum of 15 [7 for Single] integer and significand digits are
        // printed total with trailing zeros removed" — the leading digit `s` counts as one of them.
        var fractionalDigits = scientific[2..eIndex].TrimEnd('0');
        var maxFractionalDigits = Math.Max(0, significantIntegerDigits - 1);
        if (fractionalDigits.Length > maxFractionalDigits)
        {
            fractionalDigits = fractionalDigits[..maxFractionalDigits];
        }

        // MS-VBAL 5.5.1.2.4: the exponent is always signed ("+" or "-"), unlike a bare C# int.ToString().
        var exponentSign = exponent < 0 ? "-" : "+";
        return fractionalDigits.Length > 0
            ? $"{sign}{s}{decimalSeparator}{fractionalDigits}E{exponentSign}{Math.Abs(exponent)}"
            : $"{sign}{s}E{exponentSign}{Math.Abs(exponent)}";
    }
}
