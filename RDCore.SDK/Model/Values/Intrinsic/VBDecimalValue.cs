using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// A <see cref="VBNumericTypedValue"/> representing a runtime value of the <see cref="VBDecimalType"/> data type.
/// </summary>
/// <param name="Symbol">The <see cref="Symbol"/> associated with this value.</param>
public sealed record class VBDecimalValue(Symbol Symbol) 
    : VBNumericTypedValue(VBDecimalType.TypeInfo, Symbol), IVBTypedValue<VBDecimalValue, decimal>, INumericValue<VBDecimalValue>
{
    public decimal Value => (decimal)ManagedValue;
    public override int Size => 14;
    public override double ManagedValue { get; init; }
    public override object BoxedValue => ManagedValue;

    public VBDecimalValue WithValue(decimal value) => this with { ManagedValue = (double)value };

    public bool Equals(IVBTypedValue<VBDecimalValue, decimal>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
