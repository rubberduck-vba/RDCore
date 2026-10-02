using RDCore.Runtime.Semantics.Abstract;
using RDCore.Runtime.Semantics.Conversion;
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
public sealed record class VBNumericLetCoercionTypeRuntimeSemantics(
    IVerboseMessageBuilder FormatterService,
    ILetCoercionRuntimeSemanticsProvider Provider)
    : LetCoercionRuntimeSemantics<VBNumericType>(FormatterService)
{
    public override LetCoercionResult EvaluateLetCoercion(
        ISymbolResolver resolver, ExpressionNode expression,
        LetCoercionStackFrame frame)
    {
        frame = WithoutVariant(frame);
        return frame.DestinationTypeDesc.Target is VBNumericType destination
            ? FromConversion(ValueConversions.ToNumeric(frame.SourceValue, destination), expression, frame)
            : LetCoercionResult.NotApplicable(frame);
    }

    protected override ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation(
        ILetCoercionSemanticContextBuilder builder,
        ISymbolResolver resolver,
        ExpressionNode expression,
        LetCoercionStackFrame frame)
    {
        // these describe how THIS operand is coerced (an operation's two operands can widen and narrow differently), so
        // they are reported for the operand rather than for the operation as a whole.
        builder.AddLetCoercionFlags(ConversionSemanticFlags.Numeric
            | ConversionSemanticFlags.CTypeAvailable
            | DateSerialFlagsOf(frame.SourceValue, frame.DestinationTypeDesc.Target)
            | WidthFlagsOf(frame.SourceValue.TypeInfo, frame.DestinationTypeDesc.Target), frame.OperandIndex);
        return builder;
    }

    // MS-VBAL 5.5.1.2.1: a Date source coerced to a numeric type is its own DateSerial conversion, not
    // an ordinary numeric one - the provider dispatches by DESTINATION type, so this strategy (keyed on
    // VBNumericType) is the one that actually runs for Date -> Long/Double/etc, never
    // VBDateLetCoercionRuntimeSemantics's own Date-source branches (keyed on VBDateType, so only ever
    // reachable for the reverse direction).
    internal static ConversionSemanticFlags DateSerialFlagsOf(VBTypedValue source, VBType destination)
        => source is VBDateValue && destination.DefaultValue.Size < VBDoubleType.TypeInfo.DefaultValue.Size
            ? ConversionSemanticFlags.DateSerial | ConversionSemanticFlags.Narrowing
            : source is VBDateValue
                ? ConversionSemanticFlags.DateSerial
                : 0;

    // MS-VBAL 5.5.1.2.1: a conversion is wider when the destination type can hold every value of the source type, and
    // narrower when it cannot (the other way around, the source type holds values the destination type cannot).
    private static ConversionSemanticFlags WidthFlagsOf(VBType source, VBType destination)
    {
        // the fraction of a non-integral value is dropped by rounding to the nearest integer (banker's rounding):
        if (source is IFloatingPointNumericType or IFixedPointNumericType && destination is IIntegralNumericType)
        {
            return ConversionSemanticFlags.Narrowing | ConversionSemanticFlags.Lossy | ConversionSemanticFlags.BankersRounding;
        }

        if (source is not VBNumericType from || destination is not VBNumericType to || from.Equals(to))
        {
            return 0;
        }

        if (to.ManagedMinValue <= from.ManagedMinValue && to.ManagedMaxValue >= from.ManagedMaxValue)
        {
            return ConversionSemanticFlags.Widening;
        }

        if (from.ManagedMinValue <= to.ManagedMinValue && from.ManagedMaxValue >= to.ManagedMaxValue)
        {
            // a Double that is put in a Single loses digits, too; an integer put in a smaller integer type only loses its range.
            return source is IFloatingPointNumericType && destination is IFloatingPointNumericType
                ? ConversionSemanticFlags.Narrowing | ConversionSemanticFlags.Lossy
                : ConversionSemanticFlags.Narrowing;
        }

        return 0;
    }

}
