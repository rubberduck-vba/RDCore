#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

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
    /// The size of an array pointer.
    /// </summary>
    /// <remarks>
    /// You may be looking for <c>VBArrayValue.Size</c>.
    /// </remarks>
    public override int Size => sizeof(int);
}
