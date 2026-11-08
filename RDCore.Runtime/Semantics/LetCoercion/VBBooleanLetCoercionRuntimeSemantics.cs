using RDCore.Runtime.Semantics.Abstract;
using RDCore.Runtime.Semantics.Conversion;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Runtime.Semantics.LetCoercion;

/// <summary>
/// MS-VBAL 5.5.1.2.2 Let-coercion to and from <c>VBBooleanType</c>
/// </summary>
/// <remarks>
/// The coercion provider dispatches by <em>destination</em> type only, so this class — registered for
/// <see cref="VBBooleanType"/> — only ever runs when the destination actually is Boolean; it therefore
/// implements the "-&gt; Boolean" half of MS-VBAL 5.5.1.2.2's table (any numeric type, Date, String or
/// Boolean source; the String rule is specified in MS-VBAL 5.5.1.2.4 instead, for the same
/// destination-dispatch reason). The "Boolean -&gt;" half (Boolean as source, including Boolean -&gt;
/// Byte's spec-mandated 255 special case) is dispatched by a <em>numeric</em> destination instead, and
/// lives in <see cref="VBNumericLetCoercionTypeRuntimeSemantics"/> for that same reason.
/// </remarks>
/// <param name="Provider">The coercions this one is one of.</param>
/// <param name="ErrorDetails">Records the details of the errors the coercion raises.</param>
/// <param name="Regional">The regional settings a <c>String</c> is read by as a number (MS-VBAL 5.5.1.2.4); en-US when none are given.</param>
public sealed record class VBBooleanLetCoercionRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider Provider,
    IErrorDetailsRecorder ErrorDetails,
    SDK.Runtime.RegionalSettings? Regional = null)
    : LetCoercionRuntimeSemantics<VBBooleanType>(ErrorDetails)
{
    public sealed override LetCoercionResult EvaluateLetCoercion(ISymbolResolver resolver, ExpressionNode expression, LetCoercionStackFrame frame)
    {
        frame = WithoutVariant(frame);
        return frame.DestinationTypeDesc.Target is VBBooleanType
            ? FromConversion(ValueConversions.ToBoolean(frame.SourceValue, Regional), expression, frame)
            : LetCoercionResult.NotApplicable(frame);
    }

    protected override ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation(ILetCoercionSemanticContextBuilder builder, ISymbolResolver resolver, ExpressionNode expression, LetCoercionStackFrame frame)
    {
        builder.AddLetCoercionFlags(ConversionSemanticFlags.Numeric | ConversionSemanticFlags.CTypeAvailable | frame.SourceValue switch
        {
            VBNumericTypedValue numericSourceValue when frame.DestinationTypeDesc.Target is VBNumericType numericDestinationType
                && numericSourceValue.Size > numericDestinationType.DefaultValue.Size
                && ValidateDestinationTypeRange(expression, frame, out _)
                    => ConversionSemanticFlags.Narrowing,

            VBNumericTypedValue numericSourceValue when frame.DestinationTypeDesc.Target is VBNumericType numericDestinationType
                && numericSourceValue.Size < numericDestinationType.DefaultValue.Size
                && ValidateDestinationTypeRange(expression, frame, out _)
                    => ConversionSemanticFlags.Widening,

            // the provider dispatches by DESTINATION type, so this strategy (keyed on VBBooleanType) is
            // the one that actually runs for Date -> Boolean, never VBDateLetCoercionRuntimeSemantics's
            // own Date-source branches (keyed on VBDateType, only reachable for the reverse direction).
            VBDateValue => VBNumericLetCoercionTypeRuntimeSemantics.DateSerialFlagsOf(frame.SourceValue, frame.DestinationTypeDesc.Target),

            _ => 0 // nop
        }, frame.OperandIndex);
        return builder;
    }
}
