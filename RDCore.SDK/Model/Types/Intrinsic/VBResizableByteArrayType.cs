#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace RDCore.SDK.Model.Types;

/// <summary>
/// Represents a resizable array containing <c>Byte</c> elements.
/// </summary>
public sealed record class VBResizableByteArrayType() : VBResizableArrayType(VBByteType.TypeInfo) 
{
    private static readonly Lazy<VBResizableByteArrayType> _instance = new(() => new VBResizableByteArrayType(), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// The <c>Byte()</c> resizable array data type.
    /// </summary>
    public static new VBArrayType TypeInfo => _instance.Value;
}