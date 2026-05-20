using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <c>Long</c> value.
/// </summary>
/// <param name="Symbol">The symbol associated with this value.</param>
public sealed record class VBLongValue(Symbol Symbol) : VBNumericTypedValue(VBLongType.TypeInfo, Symbol),
    IVBTypedValue<VBLongValue, int>,
    INumericValue<VBLongValue>
{

    public int Value => (int)ManagedValue;
    public override int Size => sizeof(int);
    public override double ManagedValue { get; init; }

    public new VBLongValue WithValue(double value)
    {
        if (value > MaxValue.Value || value < MinValue.Value)
        {
            throw VBRuntimeErrorException.Overflow(Symbol?.SelectionRange!, $"`{TypeInfo.Name}` values must be between **{MinValue.Value:N}** and **{MaxValue.Value:N}**.");
        }
        return this with { ManagedValue = (int)value };
    }

    public VBLongValue WithValue(int value) => WithValue((double)value);

    public bool Equals(IVBTypedValue<VBLongValue, int>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
