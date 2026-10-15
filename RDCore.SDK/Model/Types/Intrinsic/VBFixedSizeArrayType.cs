#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types;

/// <summary>
/// A <see cref="VBArrayType"/> representing the data type of any <em>fixed-size array</em>.
/// </summary>
/// <param name="ItemType">The <em>declared type</em> of the array elements.</param>
public record class VBFixedSizeArrayType(VBType ItemType) : VBArrayType(ItemType)
{
    private static readonly Lazy<VBFixedSizeArrayType> _instance = new(() => new VBFixedSizeArrayType(VBVariantType.TypeInfo), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the <em>fixed-size array</em> type.
    /// </summary>
    public static new VBArrayType TypeInfo => _instance.Value;

    /// <summary>
    /// Gets an empty (uninitialized) <c>VBFixedSizeArrayValue</c> of this type's <see cref="VBArrayType.ItemType"/>.
    /// </summary>
    /// <remarks>
    /// The type does not know the bounds a declaration gives the array - those are the declaration's, which
    /// <c>IVariableDefaults</c> reads - but it knows what the array is an array of.
    /// </remarks>
    public override VBTypedValue DefaultValue => new VBFixedSizeArrayValue([], ItemType);
}