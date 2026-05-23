using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <c>Byte</c> value.
/// </summary>
/// <param name="Symbol"></param>
public sealed record class VBByteValue(Symbol Symbol) : VBNumericTypedValue(VBByteType.TypeInfo, Symbol),
    IVBTypedValue<VBByteValue, byte>, INumericValue<VBByteValue>
{
    public byte Value => (byte)ManagedValue;
    public override int Size { get; } = sizeof(byte);
    public override double ManagedValue { get; init; }

    public new VBByteValue WithValue(double value)
    {
        if (value > VBByteType.MaxValue.ManagedValue || value < VBByteType.MinValue.ManagedValue)
        {
            var location = (Symbol as BoundSymbol)?.SelectionRange;
            throw VBRuntimeErrorException.Overflow(location, $"`{TypeInfo.Name}` values must be between **{VBByteType.MinValue.ManagedValue:N}** and **{VBByteType.MaxValue.ManagedValue:N}**.");
        }
        return this with { ManagedValue = (byte)value };
    }

    public bool Equals(IVBTypedValue<VBByteValue, byte>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
