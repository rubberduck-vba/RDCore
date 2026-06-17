using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// A <see cref="VBNumericTypedValue"/> representing a runtime value of the <see cref="VBCurrencyType"/> data type.
/// </summary>
/// <param name="Symbol">The <see cref="Symbol"/> associated with this value.</param>
public sealed record class VBCurrencyValue(Symbol Symbol) 
    : VBNumericTypedValue(VBCurrencyType.TypeInfo, Symbol), IVBTypedValue<VBCurrencyValue, decimal>, INumericValue<VBCurrencyValue>
{
    public decimal Value => (decimal)ManagedValue;
    public override int Size { get; } = sizeof(long);
    public override double ManagedValue { get; init; }
    public override object BoxedValue => ManagedValue;

    public VBCurrencyValue WithValue(decimal value) => this with { ManagedValue = (double)value };

    public bool Equals(IVBTypedValue<VBCurrencyValue, decimal>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
