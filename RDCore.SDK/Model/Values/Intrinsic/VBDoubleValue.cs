using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic
{
    /// <summary>
    /// Represents a <c>Double</c> value.
    /// </summary>
    /// <param name="Symbol">The symbol associated with this value.</param>
    public sealed record class VBDoubleValue(Symbol Symbol) : VBNumericTypedValue(VBDoubleType.TypeInfo, Symbol),
        IVBTypedValue<VBDoubleValue, double>, INumericValue<VBDoubleValue>
    {
        public double Value => ManagedValue;
        public override int Size => 8;
        public override double ManagedValue { get; init; }
        public override object BoxedValue => ManagedValue;

        public new VBDoubleValue WithValue(double value) => this with { ManagedValue = value };

        public bool Equals(IVBTypedValue<VBDoubleValue, double>? other) => Value == other?.Value;
        public override int GetHashCode() => Value.GetHashCode();
    }
}
