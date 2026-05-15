using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Values;

internal record class VBMissingValue(Symbol? Symbol = null) : VBTypedValue(VBMissingType.TypeInfo, Symbol)
{
    public static VBMissingValue Missing { get; } = new();

    public override int Size => sizeof(int);
}