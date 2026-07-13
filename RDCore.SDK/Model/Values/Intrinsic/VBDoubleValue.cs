using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <c>Double</c> value.
/// </summary>
/// <param name="Symbol">The symbol associated with this value.</param>
public sealed record class VBDoubleValue(Symbol Symbol) : VBNumericTypedValue(VBDoubleType.TypeInfo, Symbol),
    IVBTypedValue<VBDoubleValue, double>, INumericValue<VBDoubleValue>
{
    public double Value => ManagedValue.InteropValue!.Value.Double;
    public override int Size => 8;

    public bool Equals(IVBTypedValue<VBDoubleValue, double>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
