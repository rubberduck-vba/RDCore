using RDCore.Parsing.Model.Abstract;

namespace RDCore.Parsing.Model.Values;

internal record class VBNothingValue : VBObjectValue,
    IVBTypedValue<VBObjectValue, Guid>
{
    public VBNothingValue(Symbol? symbol = null)
        : base(symbol)
    {
        Value = Guid.Empty;
    }
}