#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types;

/// <summary>
/// Represents any fixed-size array.
/// </summary>
public record class VBFixedSizeArrayType(VBType ItemType) : VBArrayType(ItemType)
{
    private static readonly Lazy<VBFixedSizeArrayType> _instance = new(() => new VBFixedSizeArrayType(VBVariantType.TypeInfo), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the fixed-size <c>String(n)</c> array type.
    /// </summary>
    public static new VBArrayType TypeInfo => _instance.Value;

    private static readonly Lazy<VBFixedSizeArrayValue> _defaultValue = new(() => new VBFixedSizeArrayValue([], GlobalSymbols.StaticSymbols.EmptyFixedSizeArray), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets an empty (uninitialized) <c>VBFixedSizeArrayType</c>.
    /// </summary>
    public override VBTypedValue DefaultValue => _defaultValue.Value;
}