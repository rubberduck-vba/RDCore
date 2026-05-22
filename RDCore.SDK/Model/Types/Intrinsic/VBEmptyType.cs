using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace RDCore.SDK.Model.Types;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Represents the <c>Empty</c> data type.
/// </summary>
public sealed record class VBEmptyType() : VBIntrinsicType<int?>(VBTypeNames.VBEmpty)
{
    private static readonly Lazy<VBEmptyType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// The <c>Empty</c> data type.
    /// </summary>
    /// <remarks>
    /// This data type has no declaration semantics and is only indirectly usable as a <c>Variant</c> subtype.
    /// </remarks>
    public static VBEmptyType TypeInfo => _instance.Value;

    private static readonly Lazy<VBEmptyValue> _defaultValue = new(() => VBEmptyValue.Empty, LazyThreadSafetyMode.PublicationOnly);
    public override VBEmptyValue DefaultValue => _defaultValue.Value;

    public override int Size => sizeof(int);
}
