using RDCore.Runtime.Semantics.Abstract;
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
using System.Diagnostics;
using System.Globalization;

namespace RDCore.Runtime.Semantics.LetCoercion;

/// <summary>
/// MS-VBAL 5.5.1.2.4 Let-coercion to and from <c>VBStringType</c>
/// </summary>
public record class VBStringLetCoercionRuntimeSemantics(
    IVerboseMessageBuilder FormatterService, 
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider) 
    : LetCoercionRuntimeSemantics<VBStringType>(FormatterService)
{
    public override LetCoercionResult EvaluateLetCoercion<TContext, TFlags>(
        ISymbolResolver resolver, 
        VBOperatorExpression<TContext, TFlags> expression, 
        LetCoercionStackFrame frame)
    {
        var cultureInfo = VBTypedValue.CultureInfo;
        return frame.SourceValue switch
        {
            VBStringValue stringSourceValue when frame.DestinationTypeDesc.Target is VBStringType
                // result is a copy of the source string.
                => LetCoercionResult.Success(VBTypedValueFactory.CreateStringValue(expression.ResultSymbol, stringSourceValue.Value)),

            VBNumericTypedValue numericSourceValue when frame.DestinationTypeDesc.Target is VBStringType
                => CoerceToVBString(numericSourceValue, expression.ResultSymbol, cultureInfo),

            VBStringValue stringSourceValue when frame.DestinationTypeDesc.Target is VBBooleanType
                => CoerceToVBBoolean(resolver, expression, frame, stringSourceValue),

            VBStringValue stringSourceValue when frame.DestinationTypeDesc.Target is VBDateType
                => CoerceToVBDate(resolver, expression, frame, stringSourceValue, cultureInfo),

            VBBooleanValue booleanSourceValue when frame.DestinationTypeDesc.Target is VBStringType
                => LetCoercionResult.Success(
                    VBTypedValueFactory.CreateStringValue(expression.ResultSymbol, booleanSourceValue.Value ? Tokens.True : Tokens.False)),

            VBDateValue dateSourceValue when frame.DestinationTypeDesc.Target is VBStringType
                => LetCoercionResult.Success(
                    VBTypedValueFactory.CreateStringValue(expression.ResultSymbol, 
                        dateSourceValue == VBDateType.Zero 
                            ? dateSourceValue.Value.ToLongTimeString()
                            : dateSourceValue.Value.ToShortDateString())),

            _ => LetCoercionResult.NotApplicable(frame)
        };
    }

    protected override ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation<TContext, TFlags>(
        ILetCoercionSemanticContextBuilder builder, 
        ISymbolResolver resolver, 
        VBOperatorExpression<TContext, TFlags> expression, 
        LetCoercionStackFrame frame)
    {
        throw new NotImplementedException();
    }

    private LetCoercionResult CoerceToVBDate<TContext, TFlags>(
        ISymbolResolver resolver,
        VBOperatorExpression<TContext, TFlags> expression,
        LetCoercionStackFrame frame,
        VBStringValue stringSourceValue,
        CultureInfo cultureInfo)
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum
    {
        if (DateTime.TryParse(stringSourceValue.Value, cultureInfo, out var dateValue))
        {
            return LetCoercionResult.Success(VBTypedValueFactory.CreateValue(expression.ResultSymbol, dateValue));
        }

        if (Decimal.TryParse(stringSourceValue.Value, cultureInfo, out var decimalValue))
        {
            if (decimalValue >= (decimal)VBDoubleType.MinValue.Value && decimalValue <= (decimal)VBDoubleType.MaxValue.Value)
            {
                var doubleCoercion = LetCoerceDouble(resolver, expression, frame);
                return doubleCoercion.IsSuccess
                    ? CoerceToVBBoolean(((VBDoubleValue)doubleCoercion.Result!), expression.ResultSymbol, frame)
                    : doubleCoercion;
            }
            else
            {
                return LetCoercionResult.Error(OnLetCoercionOverflow(expression, frame));
            }
        }

        return LetCoercionResult.Error(OnLetCoercionTypeMismatch(expression, frame));
    }

    private LetCoercionResult CoerceToVBBoolean<TContext, TFlags>(
        ISymbolResolver resolver, 
        VBOperatorExpression<TContext, TFlags> expression, 
        LetCoercionStackFrame frame, 
        VBStringValue value)
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum
    {
        if (string.Equals(value.Value, Tokens.True, StringComparison.InvariantCultureIgnoreCase)
            || string.Equals(value.Value, $"#TRUE#", StringComparison.InvariantCulture))
        {
            return LetCoercionResult.Success(VBTypedValueFactory.CreateBooleanValue(expression.ResultSymbol, VBBooleanValue.True), [frame]);
        }

        if (string.Equals(value.Value, Tokens.False, StringComparison.InvariantCultureIgnoreCase)
            || string.Equals(value.Value, $"#FALSE#", StringComparison.InvariantCulture))
        {
            return LetCoercionResult.Success(VBTypedValueFactory.CreateBooleanValue(expression.ResultSymbol, VBBooleanValue.False), [frame]);
        }

        // otherwise the result is let-coerced to Double, which is let-coerced to Boolean.
        var doubleCoercion = LetCoerceDouble(resolver, expression, frame);
        return doubleCoercion.IsSuccess
            ? CoerceToVBBoolean(((VBDoubleValue)doubleCoercion.Result!), expression.ResultSymbol, frame)
            : doubleCoercion;
    }


    private LetCoercionResult LetCoerceDouble<TContext, TFlags>(
        ISymbolResolver resolver, 
        VBOperatorExpression<TContext, TFlags> expression, 
        LetCoercionStackFrame currentFrame)
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum
    {
        var letCoercionFrame = currentFrame with
        {
            SourceValue = currentFrame.SourceValue,
            DestinationTypeDesc = VBTypedValueFactory.DescribeType(VBDoubleType.TypeInfo, expression.ResultSymbol)
        };
        return LetCoercionProvider.EvaluateLetCoercionSemantics(resolver, expression, letCoercionFrame);
    }

    private static LetCoercionResult CoerceToVBBoolean(VBNumericTypedValue value, Symbol resultSymbol, LetCoercionStackFrame frame)
        => LetCoercionResult.Success(VBTypedValueFactory.CreateBooleanValue(resultSymbol, value.ManagedValue != 0), [frame]);

    private static LetCoercionResult CoerceToVBString(VBNumericTypedValue value, Symbol resultSymbol, CultureInfo cultureInfo)
    {
        var numericValue = value.ManagedValue;
        if (numericValue == 0)
        {
            return LetCoercionResult.Success(VBTypedValueFactory.CreateStringValue(resultSymbol, VBStringValue.Zero));
        }
        else if (double.IsPositiveInfinity(numericValue))
        {
            return LetCoercionResult.Success(VBTypedValueFactory.CreateStringValue(resultSymbol, VBStringValue.PositiveInfinity));
        }
        else if (double.IsNegativeInfinity(numericValue))
        {
            return LetCoercionResult.Success(VBTypedValueFactory.CreateStringValue(resultSymbol, VBStringValue.NegativeInfinity));
        }
        else if (double.IsNaN(numericValue))
        {
            return LetCoercionResult.Success(VBTypedValueFactory.CreateStringValue(resultSymbol, VBStringValue.NaN));
        }

        var isNegative = numericValue < 0;
        var sign = isNegative ? "-" : string.Empty;

        var absoluteValue = Math.Abs(numericValue);
        var stringValue = absoluteValue.ToString(cultureInfo);

        var dot = cultureInfo.NumberFormat.NumberDecimalSeparator;
        var decimalIndex = stringValue.IndexOf(dot);

        if (decimalIndex >= 0)
        {
            var integerString = stringValue[..(decimalIndex - 1)];
            // MS-VBAL 5.5.1.2.4 Let-coercion to and from String

            // VBSingleValue uses normal notation for values up to 7 integer digits, scientific notation otherwise:
            Debug.Assert(VBDoubleType.SignificantIntegerDigits == VBNumericTypedValue.SignificantIntegerDigits);
            if (value is VBSingleValue && integerString.Length > VBSingleType.SignificantIntegerDigits)
            {
                var significantIntegerDigits = VBSingleType.SignificantIntegerDigits;
                stringValue = ToVBScientificNotation(numericValue, significantIntegerDigits, dot, cultureInfo);

            }
            else if (integerString.Length > VBDoubleType.SignificantIntegerDigits)
            {
                // Double (or any other numeric type for that matter): truncate to 15 significant digits
                var significantIntegerDigits = VBDoubleType.SignificantIntegerDigits;
                stringValue = ToVBScientificNotation(numericValue, significantIntegerDigits, dot, cultureInfo);
            }
        }
        else
        {
            stringValue = $"{sign}{stringValue}";
        }

        return LetCoercionResult.Success(VBTypedValueFactory.CreateStringValue(resultSymbol, stringValue));
    }

    private static string ToVBScientificNotation(double value, int significantIntegerDigits, string decimalSeparator, CultureInfo cultureInfo)
    {
        var absoluteValue = Math.Abs(value);
        var sign = value < 0 ? "-" : string.Empty;

        var stringValue = absoluteValue.ToString(cultureInfo);
        var decimalIndex = stringValue.IndexOf(decimalSeparator);

        var integerString = stringValue[..(decimalIndex - 1)];
        var decimalString = stringValue[(decimalIndex + 1)..];

        // s * 10^e
        char s;
        int e;
        if (absoluteValue >= 1)
        {
            s = integerString[0];
            e = decimalIndex; // magnitude is just where the decimal separator is at (positive)
        }
        else
        {
            // s is the first non-zero digit
            var nzIndex = decimalString.IndexOfAny(['1', '2', '3', '4', '5', '6', '7', '8', '9']);
            s = decimalString[nzIndex];
            e = -(nzIndex + 1); // magnitude is the (negative) number of decimal positions shifted
        }

        // combined integer+decimal parts cannot exceed a length of 15:
        decimalString = $"{integerString[1..]}{decimalString}";
        var fullValue = $"{integerString}{decimalSeparator}{decimalString}";

        var fullValueLength = fullValue.Length;
        decimalString = fullValueLength > significantIntegerDigits
            ? decimalString[..significantIntegerDigits]
            : decimalString;

        return $"{sign}{s}{decimalSeparator}{decimalString}E{e}";
    }
}
