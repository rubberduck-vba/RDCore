using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a runtime value of the <see cref="VBDateType"/> data type.
/// </summary>
/// <param name="Symbol">The <see cref="Symbol"/> associated with this value.</param>
public sealed record class VBDateValue(Symbol Symbol) 
    : VBTypedValue(VBDateType.TypeInfo, Symbol), IVBTypedValue<VBDateValue, DateTime>
{
    /// <summary>
    /// Gets the <c>DateSerial</c> (<c>double</c>) underlying numeric representation of the date value.
    /// </summary>
    /// <remarks>
    /// This representation is natively compatible with how dates are represented in <em>Microsoft Excel</em>.
    /// </remarks>
    public double SerialValue => ManagedValue.InteropValue!.Value.Double;

    public DateTime Value => DateTime.FromOADate(ManagedValue.InteropValue!.Value.Double);
    public override int Size => sizeof(double);

    public bool Equals(IVBTypedValue<VBDateValue, DateTime>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
