using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// A <see cref="VBNumericTypedValue"/> representing a runtime value of the <see cref="VBByteType"/> data type.
/// </summary>
/// <param name="Symbol">The <see cref="Symbol"/> associated with this value.</param>
public sealed record class VBByteValue(Symbol Symbol) 
    : VBNumericTypedValue(VBByteType.TypeInfo, Symbol), IVBTypedValue<VBByteValue, byte>, INumericValue<VBByteValue>
{
    public byte Value => ManagedValue.InteropValue!.Value.Byte;
    public override int Size { get; } = sizeof(byte);

    public bool Equals(IVBTypedValue<VBByteValue, byte>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
