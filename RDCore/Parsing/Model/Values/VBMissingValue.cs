using RDCore.Parsing.Model.Abstract;
using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Values;

internal record class VBMissingValue : VBTypedValue
{
    public VBMissingValue(Symbol? symbol = null)
        : base(VBMissingType.TypeInfo, symbol) { }

    public static VBMissingValue Missing { get; } = new VBMissingValue();

    public override int Size => sizeof(int);
}