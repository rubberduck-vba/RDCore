using RDCore.Parsing.Model.Symbols.Abstract;
using RDCore.Parsing.Model.Types.Intrinsic;
using RDCore.Parsing.Model.Values.Abstract;

namespace RDCore.Parsing.Model.Values.Intrinsic;

internal record class VBMissingValue(Symbol? Symbol = null) : VBTypedValue(VBMissingType.TypeInfo, Symbol)
{
    public static VBMissingValue Missing { get; } = new();

    public override int Size => sizeof(int);
}