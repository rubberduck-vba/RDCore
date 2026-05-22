#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.SDK.Model.Types;

/// <summary>
/// Represents any resizable array.
/// </summary>
public record class VBResizableArrayType(VBType ItemType) : VBArrayType(ItemType)
{
    private static readonly Lazy<VBResizableArrayType> _instance = new(() => new VBResizableArrayType(VBVariantType.TypeInfo), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// The <c>Array()</c> resizable array data type.
    /// </summary>
    public static new VBArrayType TypeInfo => _instance.Value;
}
