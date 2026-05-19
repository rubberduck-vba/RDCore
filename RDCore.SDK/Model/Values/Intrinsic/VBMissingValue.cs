using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Intrinsic;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

public sealed record class VBMissingValue(Symbol Symbol) : VBTypedValue(VBMissingType.TypeInfo, Symbol)
{
    private static readonly Lazy<VBMissingValue> _missing = new(() => new(GlobalSymbols.MissingValue), LazyThreadSafetyMode.PublicationOnly);
    public static VBMissingValue Missing => _missing.Value;

    public override int Size => sizeof(int);
}