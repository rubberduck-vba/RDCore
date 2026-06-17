using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Meta;

namespace RDCore.SDK.Model.Types.Meta;

/// <summary>
/// A meta-type representing a <c>VBType</c> within the type system.
/// </summary>
public sealed record class VBTypeDesc(string Name) : VBType(typeof(Type), Name, isHidden: true)
{
    private static readonly Lazy<VBTypeDesc> _instance = new(() => new(nameof(VBType)), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// The <c>VBTypeDesc</c> metadata type.
    /// </summary>
    public static VBTypeDesc TypeInfo => _instance.Value;

    private static readonly Lazy<VBTypeDescValue> _defaultValue = new(() => new VBTypeDescValue(VBVariantType.TypeInfo, GlobalSymbols.StaticSymbols.TypeDesc), LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    public override int Size => sizeof(int);
}
