using RDCore.Runtime.Semantics.Abstract;
using RDCore.Runtime.Semantics.Conversion;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.LetCoercion;

/// <summary>
/// MS-VBAL 5.5.1.2.6 Let-coercion to and from <c>VBResizableByteArray</c>
/// </summary>
public record class VBResizableByteArrayLetCoercionRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider Provider,
    IVerboseMessageBuilder FormatterService)
    : LetCoercionRuntimeSemantics<VBResizableByteArrayType>(FormatterService)
{
    public override LetCoercionResult EvaluateLetCoercion(
        ISymbolResolver resolver,
        ExpressionNode expression,
        LetCoercionStackFrame frame)
        => frame.SourceValue switch
        {
            VBArrayValue byteArraySource when byteArraySource.ItemType is VBByteType
                && frame.DestinationTypeDesc.Target is VBResizableByteArrayType
                    => LetCoercionResult.Success(CopyOf(byteArraySource)),

            // MS-VBAL 5.5.1.2.6: "a copy of the implementation-defined binary data used to store the
            // String value, excluding any prefixed length and/or end marker" - the string's own data, which
            // has no prefix or end marker to exclude: every code unit, a lone surrogate's included, and a
            // trailing byte when there is one, so an odd string gives an odd number of elements.
            VBStringValue stringSourceValue when frame.DestinationTypeDesc.Target is VBResizableByteArrayType
                => LetCoercionResult.Success(FromBytes(stringSourceValue.GetBytes())),

            // MS-VBAL 5.5.1.2.6: "Runtime error 13 (Type mismatch) is raised."
            VBNumericTypedValue or VBBooleanValue or VBDateValue when frame.DestinationTypeDesc.Target is VBResizableByteArrayType
                => LetCoercionResult.Error(OnLetCoercionTypeMismatch(expression, frame), frame),

            _ => LetCoercionResult.NotApplicable(frame)
        };

    protected override ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation(
        ILetCoercionSemanticContextBuilder builder,
        ISymbolResolver resolver,
        ExpressionNode expression,
        LetCoercionStackFrame frame) => builder.AddFlags(ConversionSemanticFlags.ArrayTarget | ConversionSemanticFlags.ByteArrayTarget);

    // MS-VBAL 5.5.1.2.6: "The result is a copy of the source Byte array" - bounds and rank are
    // preserved exactly (a whole-array Let-assignment implicitly ReDims the destination to the
    // source's own bounds, it does not renumber to 0-based).
    private static VBResizableByteArrayValue CopyOf(VBArrayValue source)
    {
        var copy = new VBResizableByteArrayValue([.. source.Dimensions.Select(d => (d.LowerBound, d.UpperBound))]);
        foreach (var subscripts in VBByteArrayCoercionHelpers.EnumerateSubscripts(source.Dimensions))
        {
            var value = ((VBByteValue)source[subscripts]!).Value;
            copy.TrySetElement(new ValueBindingHandle(new VBRuntimeValue<byte>(value)), subscripts);
        }
        return copy;
    }

    private static VBResizableByteArrayValue FromBytes(byte[] bytes)
    {
        var result = new VBResizableByteArrayValue([(0, bytes.Length - 1)]);
        for (var i = 0; i < bytes.Length; i++)
        {
            result.TrySetElement(new ValueBindingHandle(new VBRuntimeValue<byte>(bytes[i])), i);
        }
        return result;
    }
}
