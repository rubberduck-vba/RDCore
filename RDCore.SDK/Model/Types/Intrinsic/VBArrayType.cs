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

    private static readonly Lazy<VBArrayValue> _defaultValue = new(() => VBResizableArrayValue.Empty, LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets an empty (uninitialized) <c>VBResizableArrayValue</c>.
    /// </summary>
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    /// <summary>
    /// Recovers the <see cref="VBArrayValue"/> boxed into <paramref name="handle"/> as a
    /// <see cref="VBRuntimeArrayValue"/> — an array's real storage (its element cells) lives on the
    /// array object itself, not in anything a handle could reconstruct piecemeal, so this unboxes the
    /// original instance rather than building a new one.
    /// </summary>
    public override VBTypedValue CreateValue(IBindingHandle handle)
        => ((VBRuntimeValue<VBRuntimeArrayValue>)handle.Value).StoredValue.Array;
}
