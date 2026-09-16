using RDCore.Runtime.Semantics.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Services.VerboseMessages;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RDCore.Runtime.Semantics.LetCoercion;

/// <summary>
/// MS-VBAL 5.5.1.2.1 Let-coercion between numeric types
/// </summary>
/// <remarks>
/// The coercion provider dispatches by <em>destination</em> type, so this class — registered for
/// <see cref="VBNumericType"/> — also owns the "String -&gt; any numeric type" rule of MS-VBAL
/// 5.5.1.2.4 (Let-coercion to and from String), even though that rule is documented in the String
/// section of the specification.
/// </remarks>
public sealed partial record class VBNumericLetCoercionTypeRuntimeSemantics(
    IVerboseMessageBuilder FormatterService,
    ILetCoercionRuntimeSemanticsProvider Provider)
    : LetCoercionRuntimeSemantics<VBNumericType>(FormatterService)
{
    // MS-VBAL 5.5.1.2.4: numeric-coercion-string = [WS] [sign [WS]] regional-number-string
    // [exponent-clause] [WS]; exponent-clause = ("e" / "d") [sign] integer-literal. Whitespace is
    // also tolerated immediately around the sign and the exponent letter.
    [GeneratedRegex(@"^\s*(?<mantissa>[+-]?\s*(?:[0-9]+\.?[0-9]*|\.[0-9]+))\s*(?:[eEdD]\s*(?<exponent>[+-]?\s*[0-9]+))?\s*$")]
    private static partial Regex NumericCoercionStringPattern();

    public override LetCoercionResult EvaluateLetCoercion(
        ISymbolResolver resolver, VBOperatorExpression expression,
        LetCoercionStackFrame frame) => frame.SourceValue.TypeInfo switch
        {
            VBStringType when frame.DestinationTypeDesc.Target is INumericType
                => CoerceStringToNumeric(expression, frame, (VBStringValue)frame.SourceValue),

            IIntegralNumericType when frame.DestinationTypeDesc.Target is INumericType
                // if the source value is within the range of the destination type, the result is a copy of the value.
                => ValidateDestinationTypeRange(expression, frame, out var numericCoercionError)
                    ? LetCoercionResult.Success(
                        ((VBNumericType)frame.DestinationTypeDesc.Target).CreateValue(((VBNumericTypedValue)frame.SourceValue).AsDouble))
                    : LetCoercionResult.Error(numericCoercionError),

            IFloatingPointNumericType or IFixedPointNumericType when frame.DestinationTypeDesc.Target is IIntegralNumericType
                // if the source value is finite (not NaN or +/- infinity) and within the range of the destination type,
                // the result is the value converted to an integer using Banker's Rounding.
                => ValidateDestinationTypeRange(expression, frame, out var integralCoercionError)
                    ? LetCoercionResult.Success(
                        // NOTE semantic flags should note a lossy conversion here;
                        // if the source value is small enough, it can convert to zero.
                        // IMPLEMENTATION NOTE: MS-VBAL actually makes the above remark about lossy conversion in the next block.
                        ((VBNumericType)frame.DestinationTypeDesc.Target).CreateValue(VBNumericType.BankersRounding((VBNumericTypedValue)frame.SourceValue)))
                    : LetCoercionResult.Error(integralCoercionError),

            // Float/Fixed -> Float/Fixed (e.g. Double -> Single, Currency -> Decimal) was missing
            // entirely: none of the surrounding branches' patterns matched this combination, so it
            // fell through to NotApplicable — surfaced by the Numeric -> Date round-trip step
            // (VBDateLetCoercionRuntimeSemantics) coercing an already-Double/Single/Currency/Decimal
            // source to VBDoubleType.
            IFloatingPointNumericType or IFixedPointNumericType when frame.DestinationTypeDesc.Target is IFloatingPointNumericType or IFixedPointNumericType
                => ValidateDestinationTypeRange(expression, frame, out var floatToFloatError)
                    ? LetCoercionResult.Success(
                        ((VBNumericType)frame.DestinationTypeDesc.Target).CreateValue(((VBNumericTypedValue)frame.SourceValue).AsDouble))
                    : LetCoercionResult.Error(floatToFloatError),

            IIntegralNumericType when frame.DestinationTypeDesc.Target is IFloatingPointNumericType or IFixedPointNumericType
                // IMPLEMENTATION NOTE: MS-VBAL defines this block using a copy of the previous block *and* notes a lossy conversion:
                // > if the source value is finite (not NaN or +/- infinity) and within the range of the destination type,
                // > the result is the value converted to an integer using Banker's Rounding.
                // This is clearly an error in the MS-VBAL document, because no integer value will ever meet these conditions,
                // and the conversion is clearly a widening one in this case.
                // This implementation skips this technically specified check, because including it would be mathematically wrong,
                // and would also needlessly complicate the null-handling of floatCoercionError.
                //      && !double.IsNaN(sourceValue.ManagedValue) && !double.IsInfinity(sourceValue.ManagedValue)
                => ValidateDestinationTypeRange(expression, frame, out var floatCoercionError)
                    ? LetCoercionResult.Success(
                        ((VBNumericType)frame.DestinationTypeDesc.Target).CreateValue(((VBNumericTypedValue)frame.SourceValue).AsDouble))
                    : LetCoercionResult.Error(floatCoercionError),

            // MS-VBAL 5.5.1.2.2: coercing a Boolean source is dispatched by a numeric *destination*, so it
            // lands here rather than in VBBooleanLetCoercionRuntimeSemantics (which only ever runs for a
            // Boolean destination). Byte is the one destination-type exception: True -> 255, not -1.
            VBBooleanType when frame.DestinationTypeDesc.Target is VBByteType
                => LetCoercionResult.Success(new VBByteValue((byte)((bool)((VBBooleanValue)frame.SourceValue).Value ? 255 : 0))),

            VBBooleanType when frame.DestinationTypeDesc.Target is INumericType
                => LetCoercionResult.Success(
                    ((VBNumericType)frame.DestinationTypeDesc.Target).CreateValue((bool)((VBBooleanValue)frame.SourceValue).Value ? -1d : 0d)),

            // MS-VBAL 5.5.1.2.3: a Date source coerces via its standard Double (serial value) representation.
            // Can't reuse ValidateDestinationTypeRange here — it assumes an already-numeric SourceValue.
            VBDateType when frame.DestinationTypeDesc.Target is INumericType
                => VBNumericType.IsWithinRange(((VBDateValue)frame.SourceValue).SerialValue, (VBNumericType)frame.DestinationTypeDesc.Target)
                    ? LetCoercionResult.Success(
                        ((VBNumericType)frame.DestinationTypeDesc.Target).CreateValue(((VBDateValue)frame.SourceValue).SerialValue))
                    : LetCoercionResult.Error(OnLetCoercionOverflow(expression, frame)),

            _ => LetCoercionResult.NotApplicable(frame)
        };

    protected override ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation(
        ILetCoercionSemanticContextBuilder builder,
        ISymbolResolver resolver,
        VBOperatorExpression expression,
        LetCoercionStackFrame frame) => 
        builder.AddFlags(ConversionSemanticFlags.LetCoerced
            // IMPLEMENTATION NOTE: LetCoercionRuntimeSemantics does not know about its own context.
            // Following MS-VBAL we should be adding an 'Implicit' flag here, but the introdction of an
            // explicit [__c()_op] coercion operator changes this: statement-level analysis must set the Implicit|Explicit coercion flags.
            // | ConversionSemanticFlags.Implicit
            | ConversionSemanticFlags.Numeric 
            | ConversionSemanticFlags.CTypeAvailable 
            | frame.SourceValue.TypeInfo switch
            {
                IFloatingPointNumericType or IFixedPointNumericType when frame.DestinationTypeDesc.Target is IIntegralNumericType
                    => ConversionSemanticFlags.Narrowing 
                     | ConversionSemanticFlags.Lossy | ConversionSemanticFlags.BankersRounding,

                IIntegralNumericType when frame.DestinationTypeDesc.Target is IFloatingPointNumericType or IFixedPointNumericType
                    => ConversionSemanticFlags.Widening,

                _ => 0
            });

    private LetCoercionResult CoerceStringToNumeric(ExpressionNode expression, LetCoercionStackFrame frame, VBStringValue source)
    {
        var match = NumericCoercionStringPattern().Match(source.Value ?? string.Empty);
        if (!match.Success)
        {
            return LetCoercionResult.Error(OnLetCoercionTypeMismatch(expression, frame));
        }

        if (!double.TryParse(
            match.Groups["mantissa"].Value.Replace(" ", string.Empty),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var interpretedValue))
        {
            return LetCoercionResult.Error(OnLetCoercionTypeMismatch(expression, frame));
        }

        var scaledValue = interpretedValue;
        if (match.Groups["exponent"].Success)
        {
            if (!int.TryParse(
                match.Groups["exponent"].Value.Replace(" ", string.Empty),
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var exponent))
            {
                return LetCoercionResult.Error(OnLetCoercionTypeMismatch(expression, frame));
            }
            scaledValue *= Math.Pow(10, exponent);
        }

        var destinationType = (VBNumericType)frame.DestinationTypeDesc.Target;
        return !double.IsNaN(scaledValue) && VBNumericType.IsWithinRange(scaledValue, destinationType)
            ? LetCoercionResult.Success(destinationType.CreateValue(scaledValue))
            : LetCoercionResult.Error(OnLetCoercionOverflow(expression, frame));
    }
}
