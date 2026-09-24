using RDCore.Runtime.Semantics.Abstract;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.LetCoercion;

/// <summary>
/// MS-VBAL 5.5.1.2.7 Let-coercion to and from <c>VBResizableArray</c>
/// </summary>
/// <remarks>
/// The coercion provider dispatches by <em>destination</em> type only, so this class only ever runs when the
/// destination is a resizable array. Any array source whose element type is the destination's is copied — a shallow
/// copy, bounds and rank preserved exactly, so a whole-array Let-assignment implicitly ReDims the destination to the
/// source's own bounds; an array of any other element type, and a numeric, Boolean, Date or String source, is a Type
/// mismatch (runtime error 13). The Byte() rows are <see cref="VBResizableByteArrayLetCoercionRuntimeSemantics"/>'s.
/// </remarks>
public record class VBResizableArrayLetCoercionRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider,
    IVerboseMessageBuilder FormatterService)
    : LetCoercionRuntimeSemantics<VBResizableArrayType>(FormatterService)
{
    public override LetCoercionResult EvaluateLetCoercion(
        ISymbolResolver resolver,
        ExpressionNode expression,
        LetCoercionStackFrame frame)
    {
        if (frame.DestinationTypeDesc.Target is not VBResizableArrayType destination)
        {
            return LetCoercionResult.NotApplicable(frame);
        }

        return frame.SourceValue switch
        {
            // MS-VBAL 5.5.1.2.7: "Any non-Byte array -> Array with same element type as source type: the result is a
            // shallow copy of the array."
            VBArrayValue source when SameElementType(source.ItemType, destination.ItemType)
                => LetCoercionResult.Success(CopyOf(source, destination.ItemType)),

            // "... -> Any other type except Variant: Runtime error 13 (Type mismatch) is raised." - an array of
            // another element type is another type, and so is a scalar:
            // "Any numeric type, Boolean, Date, or String -> Any fixed-size array or non-Byte resizable array."
            VBArrayValue or VBNumericTypedValue or VBBooleanValue or VBDateValue or VBStringValue
                => LetCoercionResult.Error(OnLetCoercionTypeMismatch(expression, frame), frame),

            _ => LetCoercionResult.NotApplicable(frame)
        };
    }

    protected override ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation(
        ILetCoercionSemanticContextBuilder builder,
        ISymbolResolver resolver,
        ExpressionNode expression,
        LetCoercionStackFrame frame) => builder.AddFlags(ConversionSemanticFlags.ArrayTarget);

    // a class, user-defined type or Enum element type is a reference to its declaration, and two of them are the same
    // type when they are the same declaration: record equality would compare the symbols' uris, which ignore the
    // fragment that tells two declarations of one module apart.
    private static bool SameElementType(VBType source, VBType destination) => (source, destination) switch
    {
        (VBClassType s, VBClassType d) => s.Symbol.SemanticId == d.Symbol.SemanticId,
        (VBUserDefinedType s, VBUserDefinedType d) => s.Symbol.SemanticId == d.Symbol.SemanticId,
        (VBEnumType s, VBEnumType d) => s.Symbol.SemanticId == d.Symbol.SemanticId,
        _ => source.Equals(destination),
    };

    private static VBResizableArrayValue CopyOf(VBArrayValue source, VBType itemType)
    {
        (int, int)[] dimensions = [.. source.Dimensions.Select(d => (d.LowerBound, d.UpperBound))];
        var copy = itemType is VBByteType
            ? new VBResizableByteArrayValue(dimensions)
            : new VBResizableArrayValue(dimensions, itemType);

        foreach (var subscripts in VBByteArrayCoercionHelpers.EnumerateSubscripts(source.Dimensions))
        {
            copy.TrySetElement(CopyOfElement(source.GetElementHandle(subscripts)!), subscripts);
        }
        return copy;
    }

    // MS-VBAL 5.5.1.2.7: "Elements with a value type of a class or Nothing are Set-assigned to the destination array
    // element and all other elements are Let-assigned." Either way the destination element is a binding of its own
    // holding the source element's value - a reference, for a class or Nothing - so writing one array never writes the
    // other. (A let-coercion has no session, so the reference counts of Set-assigned objects are not adjusted here.)
    // An element with no readable binding - a Variant, UDT or Object cell that was never assigned - stays inert.
    private static IBindingHandle CopyOfElement(IBindingHandle element)
        => element.BindingCapabilities.HasFlag(BindingCapabilities.GetValue)
            ? new ValueBindingHandle(element.Value)
            : InvalidBindingHandle.Default;
}
