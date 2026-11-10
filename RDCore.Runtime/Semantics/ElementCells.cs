using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.Runtime.Semantics;

/// <summary>
/// The cell an element of an array is stored in, whichever way the array is made: declared, sized by a <c>ReDim</c>, or copied by an assignment.
/// </summary>
/// <remarks>
/// An element of a scalar type is kept in a cell that holds it as a payload, as a scalar variable is: an expression of it is evaluated on the payload,
/// and an assignment to it writes one, with no value made of it either way. An element of a <c>Variant</c> array is kept in a Variant's cell, as a
/// Variant variable is, where compiled code reads and writes the payload it holds. An element of another type is in the cell <see cref="VBArrayValue"/>
/// makes of the type's default value when the array is declared or sized, and in a cell of its own holding the element's value when an assignment
/// copies the array (<see cref="Store"/>).
/// </remarks>
internal static class ElementCells
{
    /// <summary>
    /// Makes the cell an element of <paramref name="itemType"/> is stored in, holding the type's default value.
    /// </summary>
    /// <returns><see langword="null"/> when the element is kept in the cell <see cref="VBArrayValue"/> makes of its default value.</returns>
    public static Func<IBindingHandle>? Of(VBType itemType)
    {
        if (!UnboxedEvaluation.IsEnabled)
        {
            return null;
        }

        // an element of an enumeration is a Long (MS-VBAL §5.2.3.4), held in a cell of the Long type as a variable declared as the enumeration is.
        var stored = VBEnumType.AsDeclaredType(itemType);
        if (ScalarPayload.TryOf(stored, stored.DefaultValue.RuntimeValue, out var initial))
        {
            return () => ScalarCell.Of(initial);
        }

        return itemType is VBVariantType && itemType.DefaultValue.RuntimeValue is VBRuntimeVariantValue empty ? () => VariantCell.Of(empty) : null;
    }

    /// <summary>
    /// The cell of an element of a <c>Variant</c> array made of the values it holds (by <c>Array</c>, or a <c>ParamArray</c> that collects a copy of an
    /// argument): a Variant's cell holding <paramref name="value"/>, as the cells of <see cref="Of"/> are, and a cell of its own when it is not known.
    /// </summary>
    /// <param name="value">The element's value, a Variant's, which the cell holds as it is.</param>
    public static IBindingHandle OfVariant(VBVariantValue value)
        => UnboxedEvaluation.IsEnabled && value is { IsIndeterminate: false, TypedValue: var held }
            && VariantCell.TryCreate(VBVariantType.TypeInfo, new VBRuntimeVariantValue(held.TypeInfo.VarType(), held), out var cell)
                ? cell!
                : BindingKnowledge.NewCell(value);

    /// <summary>
    /// Stores <paramref name="value"/> as the element of <paramref name="array"/> at <paramref name="subscripts"/>: in the element's own cell when the
    /// array was made with the cells of <see cref="Of"/>, so that the copy an assignment makes keeps them, and in a cell of its own otherwise.
    /// </summary>
    /// <param name="array">The array, made with <paramref name="cells"/>.</param>
    /// <param name="cells">What <see cref="Of"/> gave for the array's element type.</param>
    /// <param name="value">The element's value.</param>
    /// <param name="isKnown">Whether the value is known.</param>
    /// <param name="subscripts">The element's subscripts.</param>
    public static void Store(VBArrayValue array, Func<IBindingHandle>? cells, IRuntimeValue value, bool isKnown, params int[] subscripts)
    {
        if (cells is not null && array.GetElementHandle(subscripts) is IKnowledgeBinding cell)
        {
            cell.Store(value, isKnown);
            return;
        }

        array.TrySetElement(BindingKnowledge.NewCell(value, isKnown), subscripts);
    }
}
