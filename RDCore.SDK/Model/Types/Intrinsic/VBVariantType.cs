using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace RDCore.SDK.Model.Types;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Represents the <c>Variant</c> intrinsic data type.
/// </summary>
/// <param name="SubType">
/// The <c>VBVariant</c> subtype. Could be any <c>VBType</c>, including <c>VBVariant</c>, or otherwise semantically invalid data types.
/// </param>
public sealed record class VBVariantType(VBType SubType) : VBIntrinsicType<object?>(VBTypeNames.VBVariant)
{
    private static readonly Lazy<VBVariantType> _instance = new(() => new(VBEmptyType.TypeInfo), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// The <c>Variant</c> data type.
    /// </summary>
    public static VBVariantType TypeInfo => _instance.Value;

    private static readonly Lazy<VBVariantValue> _defaultValue = new(() => new(VBEmptyValue.Empty, GlobalSymbols.StaticSymbols.Empty), LazyThreadSafetyMode.PublicationOnly);
    public override VBVariantValue DefaultValue => _defaultValue.Value;

    public override int Size => sizeof(int); // FIXME this is a lie
}
