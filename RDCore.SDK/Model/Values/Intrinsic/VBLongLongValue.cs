using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Intrinsic;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

public sealed record class VBLongLongValue(Symbol? Symbol = null) : VBNumericTypedValue(VBLongLongType.TypeInfo, Symbol),
    IVBTypedValue<VBLongLongValue, long>,
    INumericValue<VBLongLongValue>
{
    public static VBLongLongValue MinValue { get; } = new VBLongLongValue { ManagedValue = long.MinValue };
    public static VBLongLongValue MaxValue { get; } = new VBLongLongValue { ManagedValue = long.MaxValue };
    public static VBLongLongValue Zero { get; } = new VBLongLongValue { ManagedValue = 0 };

    VBLongLongValue INumericValue<VBLongLongValue>.MinValue => MinValue;
    VBLongLongValue INumericValue<VBLongLongValue>.Zero => Zero;
    VBLongLongValue INumericValue<VBLongLongValue>.MaxValue => MaxValue;

    public long Value => (long)ManagedValue;
    public override int Size => sizeof(long);
    public override double ManagedValue { get; init; }

    public new VBLongLongValue WithValue(double value) => this with { ManagedValue = (long)value };
    public VBLongLongValue WithValue(long value) => this with { ManagedValue = value };

    public bool Equals(IVBTypedValue<VBLongLongValue, long>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
