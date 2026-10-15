#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.SDK.Model.Types;

/// <summary>
/// A <see cref="VBIntrinsicType{Object[]}"/> representing any type of <em>array</em> data type.
/// </summary>
/// <remarks>
/// The <em>managed type</em> of a value of this data type is <c>object[]</c>.
/// </remarks>
public abstract record class VBArrayType(VBType ItemType) :
    VBIntrinsicType<object[]>(VBTypeNames.VBArray), IEnumerableType
{
    private static readonly Lazy<VBArrayType> _instance = new(() => new VBResizableArrayType(VBVariantType.TypeInfo), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets an instance of the <c>VBResizableArrayType</c>.
    /// </summary>
    public static VBArrayType TypeInfo => _instance.Value;

    /// <summary>
    /// Gets an empty (uninitialized) <c>VBResizableArrayValue</c> of this type's <see cref="ItemType"/>.
    /// </summary>
    /// <remarks>
    /// An array that has no dimensions yet is still an array of what it was declared as: <c>TypeName</c> of a <c>Dim a() As Long</c>
    /// that nothing has <c>ReDim</c>'d is <c>"Long()"</c>. A fresh one each time, an array's cells being mutable storage that no two
    /// variables may share.
    /// </remarks>
    public override VBTypedValue DefaultValue => new VBResizableArrayValue([], ItemType);

    /// <summary>
    /// Recovers the <see cref="VBArrayValue"/> boxed into <paramref name="handle"/> as a
    /// <see cref="VBRuntimeArrayValue"/> — an array's real storage (its element cells) lives on the
    /// array object itself, not in anything a handle could reconstruct piecemeal, so this unboxes the
    /// original instance rather than building a new one.
    /// </summary>
    /// <remarks>
    /// An array read from a binding that is not known (<see cref="BindingKnowledge.ForReading"/>) is not known either, and neither is anything read
    /// from it: the element cells are the stored array's own, shared, but what an element of it is can no longer be relied on.
    /// </remarks>
    public override VBTypedValue CreateValue(IBindingHandle handle)
    {
        var array = ((VBRuntimeValue<VBRuntimeArrayValue>)handle.Value).StoredValue.Array;
        return handle is IndeterminateBindingHandle ? array.AsIndeterminate() : array;
    }
}
