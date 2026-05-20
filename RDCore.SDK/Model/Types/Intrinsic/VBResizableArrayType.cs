#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace RDCore.SDK.Model.Types;

/// <summary>
/// Represents any resizable array.
/// </summary>
public record class VBResizableArrayType() : VBArrayType()
{
    private static readonly Lazy<VBResizableArrayType> _instance = new(() => new VBResizableArrayType(), LazyThreadSafetyMode.PublicationOnly);
    public static new VBArrayType TypeInfo => _instance.Value;
}
