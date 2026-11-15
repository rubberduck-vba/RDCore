using RDCore.Runtime.Semantics.Abstract;
using RDCore.Runtime.Semantics.Conversion;
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

namespace RDCore.Runtime.Semantics.LetCoercion;

/// <summary>
/// MS-VBAL 5.5.1.2.7 Let-coercion to and from <c>VBResizableArray</c>
/// </summary>
/// <remarks>
/// The coercion provider dispatches by <em>destination</em> type only, so this class runs when the destination is a
/// resizable array. An array source whose element type is the destination's is copied with its bounds and rank, so a
/// whole-array Let-assignment resizes the destination to the source's bounds. Each element is Let-assigned (MS-VBAL
/// §5.5.1.2.7): a record is copied with its fields, and an array a <c>Variant</c> element holds is copied too. An element
/// that holds an object is Set-assigned, and refers to the same object as the source's element.
/// <para>
/// An array of any other element type, and a numeric, Boolean, Date or String source, is a Type mismatch (runtime error
/// 13), except that an array of objects is copied into one of objects of another class, or of <c>Object</c>, as MS-VBA
/// copies it (RD-VBAL §5.5.1.2.7). The Byte() rows are <see cref="VBResizableByteArrayLetCoercionRuntimeSemantics"/>'s.
/// </para>
/// </remarks>
public record class VBResizableArrayLetCoercionRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider,
    IErrorDetailsRecorder ErrorDetails)
    : LetCoercionRuntimeSemantics<VBResizableArrayType>(ErrorDetails)
{
    public override LetCoercionResult EvaluateLetCoercion(
        IRuntimeSymbolResolver resolver,
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

            // MS-VBA copies an array of objects into one of objects of another type as well, Object or any class, and checks no element's class: a Box()
            // is given a Collection (RD-VBAL §5.5.1.2.7; observed in Excel). An array of Variants is still another type.
            VBArrayValue source when HoldsObjects(source.ItemType) && HoldsObjects(destination.ItemType)
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
        IRuntimeSymbolResolver resolver,
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

    private static bool HoldsObjects(VBType elementType) => elementType is VBObjectType or VBClassType;

    // MS-VBAL 5.5.1.2.7: "Elements with a value type of a class or Nothing are Set-assigned to the destination array
    // element and all other elements are Let-assigned." A Set-assigned element is the same reference in a cell of its
    // own. A Let-assigned one is a copy of the source element's value (DataValues.CopyRuntimeValue): a record is copied
    // with its fields, a nested record's and an array field's included (MS-VBAL 5.5.1.2.8), and so is an array a Variant
    // element holds (MS-VBAL 5.5.1.2.12), so writing one array never writes the other. (A let-coercion has no session, so
    // the reference counts of Set-assigned objects are not adjusted here.) The cells are those a declared array of the
    // element type has (ElementCells), so the copy is read and written as the array it is assigned to would be. An
    // element with no readable binding - a Variant, UDT or Object cell that was never assigned - stays as the copy made
    // it: its type's default value.
    private static VBResizableArrayValue CopyOf(VBArrayValue source, VBType itemType)
    {
        (int, int)[] dimensions = [.. source.Dimensions.Select(d => (d.LowerBound, d.UpperBound))];
        var cells = ElementCells.Of(itemType);
        var copy = itemType is VBByteType
            ? new VBResizableByteArrayValue(dimensions, cells)
            : new VBResizableArrayValue(dimensions, itemType, elementCell: cells);

        foreach (var subscripts in VBByteArrayCoercionHelpers.EnumerateSubscripts(source.Dimensions))
        {
            var element = source.GetElementHandle(subscripts)!;
            if (!element.BindingCapabilities.HasFlag(BindingCapabilities.GetValue))
            {
                if (cells is null)
                {
                    copy.TrySetElement(InvalidBindingHandle.Default, subscripts);
                }

                continue;
            }

            ElementCells.Store(copy, cells, DataValues.CopyRuntimeValue(element.Value), element is not IKnowledgeBinding { IsKnown: false }, subscripts);
        }
        return copy;
    }
}
