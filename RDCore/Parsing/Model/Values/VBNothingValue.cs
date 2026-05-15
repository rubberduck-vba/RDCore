using RDCore.Parsing.Model.Symbols;
using RDCore.Runtime;

namespace RDCore.Parsing.Model.Values;

internal record class VBNothingValue(Symbol? Symbol = null) : VBObjectValue(Symbol, VBLongPtrValue.Zero),
    IVBTypedValue<VBObjectValue, VBLongPtrValue>
{

}