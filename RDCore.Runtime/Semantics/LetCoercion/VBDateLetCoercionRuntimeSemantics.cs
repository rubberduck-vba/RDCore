using RDCore.Runtime.Semantics.Abstract;
using RDCore.Runtime.Semantics.Conversion;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Model.AST.Abstract;
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
        ExpressionNode expression,
        LetCoercionStackFrame frame) =>
        frame.DestinationTypeDesc.Target is VBDateType
            ? FromConversion(ValueConversions.ToDate(frame.SourceValue), expression, frame)
            : LetCoercionResult.NotApplicable(frame);

    protected override ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation(
        ILetCoercionSemanticContextBuilder builder, 
        ISymbolResolver resolver, 
        ExpressionNode expression, 
        LetCoercionStackFrame frame)
    {
        var destinationType = frame.DestinationTypeDesc.Target;
        // a Date SOURCE never reaches this switch: the provider dispatches by destination type, and
        // this strategy is keyed on VBDateType, so destinationType here is always Date - the "Date ->
        // numeric/Boolean" DateSerial flag lives in VBNumericLetCoercionTypeRuntimeSemantics.DateSerialFlagsOf
        // and VBBooleanLetCoercionRuntimeSemantics's own switch instead, the strategies that actually run
        // for those destinations.
        builder.AddLetCoercionFlags(ConversionSemanticFlags.CTypeAvailable | frame.SourceValue switch
        {
            VBNumericTypedValue or VBBooleanValue when destinationType is VBDateType && frame.SourceValue.Size < VBDoubleType.TypeInfo.DefaultValue.Size
                => ConversionSemanticFlags.Numeric | ConversionSemanticFlags.Widening,
            VBNumericTypedValue or VBBooleanValue when destinationType is VBDateType
                => ConversionSemanticFlags.Numeric,

            _ => 0
        }, frame.OperandIndex);

        return builder;
    }
}
