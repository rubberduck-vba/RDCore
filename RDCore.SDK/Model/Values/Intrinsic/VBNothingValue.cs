using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic
{
    /// <summary>
    /// Represents the <c>Nothing</c> (<c>VBObjectType</c>) literal value.
    /// </summary>
    /// <param name="Symbol">The symbol associate with this value.</param>
    public sealed record class VBNothingValue(Symbol Symbol) : VBObjectValue(Symbol),
        IVBTypedValue<VBObjectValue, int> { }
}