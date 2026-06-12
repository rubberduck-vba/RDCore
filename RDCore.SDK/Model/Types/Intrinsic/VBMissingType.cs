#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types;

/// <summary>
/// A <see cref="VBIntrinsicType{int}"/> representing the <c>Variant</c> subtype given to an <strong>optional <c>Variant</c> parameter</strong> that was not supplied.
/// </summary>
/// <remarks>
/// The <em>managed type</em> of a value of this data type is <c>int</c>.<br/>
/// 👉 This data type has no declaration semantics and is only indirectly usable as a <c>Variant</c> subtype.
/// </remarks>
public record class VBMissingType() : VBIntrinsicType<int>(VBTypeNames.VBMissing)
{
    private static readonly Lazy<VBMissingType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// The <c>Missing</c> data type.
    /// </summary>
    /// <remarks>
    /// This data type has no declaration semantics and is only indirectly usable as a <c>Variant</c> subtype.
    /// </remarks>
    public static VBMissingType TypeInfo => _instance.Value;

    private static readonly Lazy<VBMissingValue> _defaultValue = new(() => new(GlobalSymbols.StaticSymbols.MissingValue), LazyThreadSafetyMode.PublicationOnly);
    public override VBMissingValue DefaultValue => _defaultValue.Value;

    public override int Size => sizeof(int);
}