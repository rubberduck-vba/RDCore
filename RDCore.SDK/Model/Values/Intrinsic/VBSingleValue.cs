using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <c>Single</c> numeric value.
/// </summary>
/// <param name="Symbol">The symbol associated with this value.</param>
public sealed record class VBSingleValue(Symbol Symbol) : VBNumericTypedValue(VBSingleType.TypeInfo, Symbol),
    IVBTypedValue<VBSingleValue, float>,
    INumericValue<VBSingleValue>
{
    public float Value => (float)ManagedValue;
    public override int Size => sizeof(float);
    public override double ManagedValue { get; init; }

    public override object BoxedValue => ManagedValue;

    public new VBSingleValue WithValue(double value) => this with { ManagedValue = (float)value };

    public bool Equals(IVBTypedValue<VBSingleValue, float>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
