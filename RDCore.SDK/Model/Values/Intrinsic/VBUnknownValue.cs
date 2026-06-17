using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents the placeholder runtime value of an unresolved symbol.
/// </summary>
/// <param name="Symbol"></param>
public sealed record class VBUnknownValue(Symbol Symbol) : VBTypedValue(VBUnknownType.TypeInfo, Symbol), IVBTypedValue<VBUnknownValue, object>
{
    private static readonly Lazy<VBUnknownValue> _defaultValue = new(() => new(GlobalSymbols.StaticSymbols.VBUnknown), LazyThreadSafetyMode.PublicationOnly);
    public static VBUnknownValue DefaultValue => _defaultValue.Value;

    public override int Size => sizeof(int);
    public object Value => BoxedValue;

    public override object BoxedValue => null!;

    public bool Equals(IVBTypedValue<VBUnknownValue, object>? other) => false;
}
