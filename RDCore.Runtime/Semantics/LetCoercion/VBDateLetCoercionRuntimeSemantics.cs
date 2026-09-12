using RDCore.Runtime.Semantics.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
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

namespace RDCore.Runtime.Semantics.LetCoercion;

/// <summary>
/// MS-VBAL 5.5.1.2.3 Let-coercion to and from <c>VBDateType</c>
/// </summary>
/// <remarks>
/// The coercion provider dispatches by <em>destination</em> type, so this class — registered for
/// <see cref="VBDateType"/> — also owns the "String -&gt; Date" rule of MS-VBAL 5.5.1.2.4
/// (Let-coercion to and from String), even though that rule is documented in the String section of
/// the specification.
/// </remarks>
public record class VBDateLetCoercionRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider Provider,
    IVerboseMessageBuilder FormatterService)
    : LetCoercionRuntimeSemantics<VBDateType>(FormatterService)
{
    public override LetCoercionResult EvaluateLetCoercion(
        ISymbolResolver resolver,
        VBOperatorExpression expression,
        LetCoercionStackFrame frame) =>
        frame.SourceValue switch
        {
            VBDateValue sourceDateValue when frame.DestinationTypeDesc.Target is VBDateType
                // result is a copy of the source date (no implicit DateSerial semantic flag should be issued here)
                => LetCoercionResult.Success(
                    frame.DestinationTypeDesc.Target.CreateValue(new ValueBindingHandle(sourceDateValue.RuntimeValue))),

            // NOTE: "Date -> Numeric/Boolean" is NOT handled here — the provider dispatches by
            // destination type, so that direction is unreachable from this VBDateType-registered
            // class. It's implemented in VBNumericLetCoercionTypeRuntimeSemantics (VBDateType source
            // case) and VBBooleanLetCoercionRuntimeSemantics (VBDateValue source case) instead.

            VBNumericTypedValue or VBBooleanValue when frame.DestinationTypeDesc.Target is VBDateType
                // result is the source value let-coerced to Double, then the Double is interpreted as a standard SerialValue.
                => LetCoercionResult.Success(frame.DestinationTypeDesc.Target.CreateValue(new ValueBindingHandle(
                    ((VBDoubleValue)Provider.EvaluateLetCoercionSemantics(resolver, expression,
                        // we must first create the VBDoubleValue for the managed SerialValue:
                        frame with {
                            DestinationTypeDesc = new VBTypeDescValue(VBDoubleType.TypeInfo)
                        }).Result!).RuntimeValue))),

            VBStringValue stringSourceValue when frame.DestinationTypeDesc.Target is VBDateType
                => CoerceStringToDate(resolver, expression, frame, stringSourceValue),

            _ => LetCoercionResult.NotApplicable(frame)
        };

    // MS-VBAL 5.5.1.2.4: try date/time/time/date interpretation first; otherwise, if the string can be
    // interpreted as a number or currency value within Double's magnitude range, let-coerce that Double
    // to Date. A Double-conversion overflow is reported as Type mismatch (13), not Overflow (6).
    private LetCoercionResult CoerceStringToDate(ISymbolResolver resolver, VBOperatorExpression expression, LetCoercionStackFrame frame, VBStringValue source)
    {
        if (DateTime.TryParse(source.Value, CultureInfo.InvariantCulture, out var dateValue))
        {
            return LetCoercionResult.Success(new VBDateValue(dateValue.ToOADate()));
        }

        var doubleCoercion = Provider.EvaluateLetCoercionSemantics(resolver, expression,
            frame with { DestinationTypeDesc = new VBTypeDescValue(VBDoubleType.TypeInfo) });

        if (doubleCoercion.Result is not VBDoubleValue coerced)
        {
            // unparseable, or the string-to-Double step overflowed: MS-VBAL 5.5.1.2.4 reports both as
            // Type mismatch (13) here, not the Overflow (6) that the Double coercion itself would raise.
            return LetCoercionResult.Error(OnLetCoercionTypeMismatch(expression, frame));
        }

        var serialValue = (double)coerced.RuntimeValue.BoxedValue;
        return serialValue >= VBDateType.MinSerial && serialValue <= VBDateType.MaxSerial
            ? LetCoercionResult.Success(new VBDateValue(serialValue))
            : LetCoercionResult.Error(OnLetCoercionTypeMismatch(expression, frame));
    }

    protected override ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation(
        ILetCoercionSemanticContextBuilder builder, 
        ISymbolResolver resolver, 
        VBOperatorExpression expression, 
        LetCoercionStackFrame frame)
    {
        var destinationType = frame.DestinationTypeDesc.Target;
        builder.AddLetCoercionFlags(ConversionSemanticFlags.CTypeAvailable | frame.SourceValue switch
        {
            VBDateValue when destinationType is VBNumericType or VBBooleanType && destinationType.DefaultValue.Size < VBDoubleType.TypeInfo.DefaultValue.Size
                => ConversionSemanticFlags.DateSerial | ConversionSemanticFlags.Narrowing,
            VBDateValue when destinationType is VBNumericType or VBBooleanType
                => ConversionSemanticFlags.DateSerial,

            VBNumericTypedValue or VBBooleanValue when destinationType is VBDateType && frame.SourceValue.Size < VBDoubleType.TypeInfo.DefaultValue.Size
                => ConversionSemanticFlags.Numeric | ConversionSemanticFlags.Widening,
            VBNumericTypedValue or VBBooleanValue when destinationType is VBDateType
                => ConversionSemanticFlags.Numeric,

            _ => 0
        }, frame.OperandIndex);

        return builder;
    }
}
