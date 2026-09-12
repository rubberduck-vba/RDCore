using RDCore.Runtime.Semantics.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.LetCoercion;

/// <summary>
/// MS-VBAL 5.5.1.2.2 Let-coercion to and from <c>VBBooleanType</c>
/// </summary>
/// <remarks>
/// The coercion provider dispatches by <em>destination</em> type only, so this class — registered for
/// <see cref="VBBooleanType"/> — only ever runs when the destination actually is Boolean; it therefore
/// implements the "-&gt; Boolean" half of MS-VBAL 5.5.1.2.2's table (any numeric type, Date or Boolean
/// source). The "Boolean -&gt;" half (Boolean as source, including Boolean -&gt; Byte's spec-mandated
/// 255 special case) is dispatched by a <em>numeric</em> destination instead, and lives in
/// <see cref="VBNumericLetCoercionTypeRuntimeSemantics"/> for that same reason.
/// </remarks>
public sealed record class VBBooleanLetCoercionRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider Provider,
    IVerboseMessageBuilder FormatterService)
    : LetCoercionRuntimeSemantics<VBBooleanType>(FormatterService)
{
    public sealed override LetCoercionResult EvaluateLetCoercion(ISymbolResolver resolver, VBOperatorExpression expression, LetCoercionStackFrame frame) =>
        frame.SourceValue switch
        {
            VBBooleanValue booleanSourceValue when frame.DestinationTypeDesc.Target is VBBooleanType
                => LetCoercionResult.Success(new VBBooleanValue((bool)booleanSourceValue.Value)),

            // MS-VBAL 5.5.1.2.2: "If the source value is 0, the result is False. Otherwise, the result is True."
            VBNumericTypedValue numericSourceValue when frame.DestinationTypeDesc.Target is VBBooleanType
                => LetCoercionResult.Success(new VBBooleanValue(numericSourceValue.AsDouble != 0)),

            // MS-VBAL 5.5.1.2.3: a Date source coerces via its standard Double (serial value) representation.
            VBDateValue dateSourceValue when frame.DestinationTypeDesc.Target is VBBooleanType
                => LetCoercionResult.Success(new VBBooleanValue(dateSourceValue.SerialValue != 0)),

            _ => LetCoercionResult.NotApplicable(frame)
        };

    protected override ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation(ILetCoercionSemanticContextBuilder builder, ISymbolResolver resolver, VBOperatorExpression expression, LetCoercionStackFrame frame)
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

            _ => 0 // nop
        }, frame.OperandIndex);
        return builder;
    }
}
