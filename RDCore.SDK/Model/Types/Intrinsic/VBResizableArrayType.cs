#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.SDK.Model.Types;

/// <summary>
/// A <see cref="VBArrayType"/> representing the data type of any <em>resizable array</em>.
/// </summary>
/// <param name="ItemType">The <em>declared type</em> of the array elements.</param>
public record class VBResizableArrayType(VBType ItemType) : VBArrayType(ItemType)
{
    private static readonly Lazy<VBResizableArrayType> _instance = new(() => new VBResizableArrayType(VBVariantType.TypeInfo), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// The <c>Array()</c> resizable array data type.
    /// </summary>
    public static new VBArrayType TypeInfo => _instance.Value;
}
