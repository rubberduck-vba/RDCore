using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic
{
    /// <summary>
    /// Represents a <c>LongLong</c> value.
    /// </summary>
    /// <param name="Symbol">The symbol associated with this value.</param>
    public sealed record class VBLongLongValue(Symbol Symbol) : VBNumericTypedValue(VBLongLongType.TypeInfo, Symbol),
        IVBTypedValue<VBLongLongValue, long>, 
        INumericValue<VBLongLongValue>
    {
        public long Value => (long)ManagedValue;
        public override int Size => sizeof(long);
        public override double ManagedValue { get; init; }

        public override object BoxedValue => ManagedValue;

        public new VBLongLongValue WithValue(double value) => this with { ManagedValue = (long)value };
        public VBLongLongValue WithValue(long value) => this with { ManagedValue = value };

        public bool Equals(IVBTypedValue<VBLongLongValue, long>? other) => Value == other?.Value;
        public override int GetHashCode() => Value.GetHashCode();
    }
}
