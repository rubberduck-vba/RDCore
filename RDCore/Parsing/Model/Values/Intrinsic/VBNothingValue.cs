using RDCore.Parsing.Model.Symbols.Abstract;
using RDCore.Parsing.Model.Values.Abstract;
using RDCore.Runtime;

namespace RDCore.Parsing.Model.Values.Intrinsic;

internal record class VBNothingValue(Symbol? Symbol = null) : VBObjectValue(Symbol, VBLongPtrValue.Zero),
    IVBTypedValue<VBObjectValue, VBLongPtrValue>
{

}