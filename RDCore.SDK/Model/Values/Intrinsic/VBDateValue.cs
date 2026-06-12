using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic
{
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
        public double SerialValue => Value.ToOADate();
        public override object BoxedValue => SerialValue;

        public DateTime Value { get; init; } = default;
        public override int Size => 8;

        public bool Equals(IVBTypedValue<VBDateValue, DateTime>? other) => Value == other?.Value;
        public override int GetHashCode() => Value.GetHashCode();
    }
}
