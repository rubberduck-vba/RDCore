using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace RDCore.SDK.Model.Types;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Represents the <c>Boolean</c> data type.
/// </summary>
public sealed record class VBBooleanType() : VBIntrinsicType<bool>(VBTypeNames.VBBoolean)
{
    private static readonly Lazy<VBBooleanType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// The <c>Boolean</c> data type.
    /// </summary>
    public static VBBooleanType TypeInfo => _instance.Value;

    private readonly Lazy<VBTypedValue> _defaultValue = new(() => VBBooleanValue.False, LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the default value <c>VBBooleanValue.False</c>.
    /// </summary>
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    public override int Size => sizeof(bool);
}
