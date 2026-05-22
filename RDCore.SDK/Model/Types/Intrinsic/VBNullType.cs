using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace RDCore.SDK.Model.Types;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Represents the <c>Null</c> data type.
/// </summary>
public sealed record class VBNullType() : VBIntrinsicType<object?>(VBTypeNames.VBNull)
{
    private static readonly Lazy<VBNullType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// The <c>Null</c> data type.
    /// </summary>
    public static VBNullType TypeInfo => _instance.Value;

    private static readonly Lazy<VBNullValue> _defaultValue = new(() => VBNullValue.Null, LazyThreadSafetyMode.PublicationOnly);
    public override VBNullValue DefaultValue => _defaultValue.Value;
    public override int Size => sizeof(int);
}
