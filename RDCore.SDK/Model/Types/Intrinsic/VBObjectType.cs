using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace RDCore.SDK.Model.Types;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Represents the <c>Object</c> data type.
/// </summary>
public record class VBObjectType() : VBIntrinsicType<int>(VBTypeNames.VBObject)
{
    private static readonly Lazy<VBObjectType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// The <c>Object</c> data type.
    /// </summary>
    public static VBObjectType TypeInfo => _instance.Value;

    private static readonly Lazy<VBObjectValue> _defaultValue = new(() => VBObjectValue.Nothing, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;
    
    public override int Size => sizeof(int);
}
