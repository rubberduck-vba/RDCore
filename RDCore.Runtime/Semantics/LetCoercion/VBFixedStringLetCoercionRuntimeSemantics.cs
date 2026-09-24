using RDCore.Runtime.Semantics.Abstract;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.LetCoercion;

/// <summary>
/// MS-VBAL 5.5.1.2.5 Let-coercion to <c>VBFixedStringType</c> (<c>String * length</c>)
/// </summary>
/// <remarks>
/// The coercion provider dispatches by <em>destination</em> type only, so this class only ever runs when the
/// destination is a fixed-length string; the result is always exactly <c>length</c> characters. A String source is
/// truncated to its first <c>length</c> characters, or padded on the right with spaces up to <c>length</c>; a numeric,
/// Boolean or Date source is first let-coerced to a String (<see cref="VBStringLetCoercionRuntimeSemantics"/>, through
/// the provider), and then to the fixed-length string. The same goes for a Byte() source (MS-VBAL 5.5.1.2.6, which
/// lists <c>String * length</c> alongside String). An Empty source is a string of <c>length</c> spaces
/// (MS-VBAL 5.5.1.2.11).
/// </remarks>
public record class VBFixedStringLetCoercionRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider,
    IVerboseMessageBuilder FormatterService)
    : LetCoercionRuntimeSemantics<VBFixedStringType>(FormatterService)
{
    public override LetCoercionResult EvaluateLetCoercion(
        ISymbolResolver resolver,
        ExpressionNode expression,
        LetCoercionStackFrame frame)
    {
        if (frame.DestinationTypeDesc.Target is not VBFixedStringType destination)
        {
            return LetCoercionResult.NotApplicable(frame);
        }

        var text = ToText(resolver, expression, frame);
        return text.Result is VBStringValue coerced
            ? LetCoercionResult.Success(new VBStringValue(Fit(coerced.Value, destination.Length)))
            : text;
    }

    protected override ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation(
        ILetCoercionSemanticContextBuilder builder,
        ISymbolResolver resolver,
        ExpressionNode expression,
        LetCoercionStackFrame frame)
    {
        ConversionSemanticFlags flags = frame.SourceValue switch
        {
            VBEmptyValue => ConversionSemanticFlags.EmptyOperand,
            VBArrayValue { ItemType: VBByteType } => ConversionSemanticFlags.ByteArrayOperand,
            _ => 0
        };

        // a string longer than the destination loses its tail.
        if (frame.DestinationTypeDesc.Target is VBFixedStringType destination
            && ToText(resolver, expression, frame).Result is VBStringValue text
            && text.Value.Length > destination.Length)
        {
            flags |= ConversionSemanticFlags.Narrowing | ConversionSemanticFlags.Lossy;
        }

        return builder.AddFlags(flags);
    }

    // the source as a String value, before it is fitted to the destination's length: MS-VBAL 5.5.1.2.5 defines every
    // other source as "Let-coerced to a String value and then Let-coerced to a String * length value".
    private LetCoercionResult ToText(ISymbolResolver resolver, ExpressionNode expression, LetCoercionStackFrame frame)
        => frame.SourceValue switch
        {
            VBStringValue source => LetCoercionResult.Success(source),

            // MS-VBAL 5.5.1.2.11 - "The result is a string containing length spaces": Empty is the empty string here,
            // and fitting it to the length pads it with exactly that.
            VBEmptyValue => LetCoercionResult.Success(VBStringValue.ZeroLengthString),

            VBNumericTypedValue or VBBooleanValue or VBDateValue or VBArrayValue { ItemType: VBByteType }
                => LetCoercionProvider.EvaluateLetCoercionSemantics(resolver, expression,
                    frame with { DestinationTypeDesc = new VBTypeDescValue(VBStringType.TypeInfo) }),

            _ => LetCoercionResult.NotApplicable(frame)
        };

    // MS-VBAL 5.5.1.2.5: "If the source string has more than length characters, the result is a copy of the source
    // string truncated to the first length characters. Otherwise, the result is a copy of the source string padded on
    // the right with space characters to reach a total of length characters."
    private static string Fit(string text, int length)
        => text.Length > length ? text[..length] : text.PadRight(length);
}
