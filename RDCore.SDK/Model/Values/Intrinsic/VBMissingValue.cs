using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <c>Variant/Missing</c> value.
/// </summary>
/// <param name="Symbol">The <em>optional variant parameter</em> symbol associated with this value.</param>
public sealed record class VBMissingValue(Symbol Symbol) : VBTypedValue(VBMissingType.TypeInfo, Symbol)
{
    private static readonly Lazy<VBMissingValue> _missing = new(() => new(GlobalSymbols.StaticSymbols.MissingValue), LazyThreadSafetyMode.PublicationOnly);
    public static VBMissingValue Missing => _missing.Value;

    public override int Size => sizeof(int);
}