using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.SDK.Model.Values.Abstract;

/// <summary>
/// Copies a data value (<strong>MS-VBAL §2.1</strong>) the way an assignment does: deeply, so that the copy shares no storage with the value it
/// was copied from.
/// </summary>
/// <remarks>
/// A Let-coercion's result is "a copy of the source value" (<strong>MS-VBAL §5.5.1.2.12</strong>, and each table of §5.5.1.2). An array and a
/// user-defined type are made of cells (an element's, a field's), and a copy that shared them with its source would be the source by another name:
/// <c>w = v: w(0) = 9</c> would change <c>v(0)</c>, and the object an element held would have one holder where there are two. Every cell of the
/// copy is a new one holding a copy of what the source cell holds, the whole way down - an array a <c>Variant</c> element holds, a user-defined
/// type a field is. What a cell knows of its value is kept.
/// <para>
/// 👉 Not a <c>with</c> expression: a record's <c>with</c> makes a new <em>view</em> of the same storage, which is what binding a value to its
/// variable's address needs (<see cref="VBArrayValue.TryAllocateIn"/>). An object is its reference, and a copy of it is the same object; a
/// number, a <c>String</c> and every other intrinsic value is immutable - but one read from a variable is bound to the variable's cell, and its
/// copy is bound to a cell of its own that holds what the variable held when it was copied.
/// </para>
/// </remarks>
public static class DataValues
{
    /// <summary>
    /// A copy of <paramref name="value"/> that shares no cell with it.
    /// </summary>
    /// <param name="value">The value to copy.</param>
    /// <returns>The copy; <paramref name="value"/> itself when it holds no cell to share.</returns>
    public static VBTypedValue Copy(VBTypedValue value) => value switch
    {
        VBArrayValue array => array.DataCopy(),
        VBUserDefinedTypeValue record => record.DataCopy(),
        VBVariantValue variant => Known(new VBVariantValue(Copy(variant.TypedValue)), !variant.IsIndeterminate),
        _ => Snapshot(value),
    };

    // a number, a String, an object reference: immutable, but a value read from a variable is a view of the variable's cell, and is whatever
    // the variable holds when it is next read - `v = n: n = 2` would make v 2. The copy holds what the cell holds now, in a cell of its own.
    // A value that is not known stays the assumption it is.
    private static VBTypedValue Snapshot(VBTypedValue value)
        => value.Handle is IKnowledgeBinding { IsKnown: var isKnown } cell && cell.BindingCapabilities.HasFlag(BindingCapabilities.GetValue)
            ? value with { Handle = BindingKnowledge.NewCell(cell.Value, isKnown) }
            : value;

    /// <summary>
    /// A new cell that holds a copy of what <paramref name="cell"/> holds, and knows of it what <paramref name="cell"/> does.
    /// </summary>
    /// <param name="cell">An element's cell, or a field's.</param>
    /// <returns>The new cell; <paramref name="cell"/> itself when it holds nothing that can be read, which a write replaces rather than writes to.</returns>
    public static IBindingHandle CopyCell(IBindingHandle cell)
        => cell.BindingCapabilities.HasFlag(BindingCapabilities.GetValue) && cell is IKnowledgeBinding knowledge
            ? BindingKnowledge.NewCell(CopyRuntimeValue(cell.Value), knowledge.IsKnown)
            : cell;

    /// <summary>
    /// A copy of a value as a cell stores it: an array, a user-defined type and a <c>Variant</c> are boxed (<c>SymbolAddressTable.BoxedValue</c>),
    /// and their boxes are copied with what they hold.
    /// </summary>
    /// <param name="value">The stored value.</param>
    public static IRuntimeValue CopyRuntimeValue(IRuntimeValue value) => value switch
    {
        VBRuntimeValue<VBRuntimeArrayValue> { StoredValue: var boxed } => new VBRuntimeValue<VBRuntimeArrayValue>(new VBRuntimeArrayValue(boxed.Array.DataCopy())),
        VBRuntimeValue<VBRuntimeUserDefinedTypeValue> { StoredValue: var boxed }
            => new VBRuntimeValue<VBRuntimeUserDefinedTypeValue>(new VBRuntimeUserDefinedTypeValue(boxed.UserDefinedType.DataCopy())),
        VBRuntimeVariantValue { WrappedValue: var held } variant when Holds(held) => variant with { WrappedValue = Copy(held) },
        _ => value,
    };

    // only an array and a user-defined type are made of cells; whatever else a Variant holds is immutable, or an object's reference.
    private static bool Holds(VBTypedValue value) => value is VBArrayValue or VBUserDefinedTypeValue;

    private static VBTypedValue Known(VBTypedValue value, bool isKnown) => isKnown ? value : value.AsIndeterminate();
}
