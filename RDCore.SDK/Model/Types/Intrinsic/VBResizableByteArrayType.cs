#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace RDCore.SDK.Model.Types;

/// <summary>
/// A <see cref="VBArrayType"/> representing the data type of a <em>resizable array</em> containing elements of type <see cref="VBByteType"/>.
/// </summary>
/// <remarks>
/// 👉 Values of this specific type of array have <em>let-coercion semantics</em> to and from <see cref="VBStringType"/> values.
/// </remarks>
public sealed record class VBResizableByteArrayType() : VBResizableArrayType(VBByteType.TypeInfo) 
{
    private static readonly Lazy<VBResizableByteArrayType> _instance = new(() => new VBResizableByteArrayType(), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// The <c>Byte()</c> resizable array data type.
    /// </summary>
    public static new VBArrayType TypeInfo => _instance.Value;
}