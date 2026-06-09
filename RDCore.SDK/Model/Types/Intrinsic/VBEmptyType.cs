#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types;

/// <summary>
/// A <see cref="VBIntrinsicType{int}"/> representing the <c>Empty</c> data type.
/// </summary>
/// <remarks>
/// The <em>managed type</em> of a value of this data type is <c>int</c>.<br/>
/// 👉 This data type has no declaration semantics and is only indirectly usable as a <c>Variant</c> subtype.
/// </remarks>
public sealed record class VBEmptyType() : VBIntrinsicType<int>(VBTypeNames.VBEmpty)
{
    private static readonly Lazy<VBEmptyType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// The <c>Empty</c> data type.
    /// </summary>
    public static VBEmptyType TypeInfo => _instance.Value;

    private static readonly Lazy<VBEmptyValue> _defaultValue = new(() => VBEmptyValue.Empty, LazyThreadSafetyMode.PublicationOnly);
    public override VBEmptyValue DefaultValue => _defaultValue.Value;

    public override int Size => sizeof(int);
}
