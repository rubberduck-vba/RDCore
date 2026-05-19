using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Intrinsic;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

public sealed record class VBSingleValue : VBNumericTypedValue,
    IVBTypedValue<VBSingleValue, float>,
    INumericValue<VBSingleValue>
{
    public VBSingleValue(Symbol? declarationSymbol = null)
        : base(VBSingleType.TypeInfo, declarationSymbol) { }

    public static VBSingleValue MinValue { get; } = new VBSingleValue { ManagedValue = float.MinValue };
    public static VBSingleValue MaxValue { get; } = new VBSingleValue { ManagedValue = float.MaxValue };
    public static VBSingleValue Zero { get; } = new VBSingleValue { ManagedValue = 0 };

    VBSingleValue INumericValue<VBSingleValue>.MinValue => MinValue;
    VBSingleValue INumericValue<VBSingleValue>.Zero => Zero;
    VBSingleValue INumericValue<VBSingleValue>.MaxValue => MaxValue;

    public float Value => (float)ManagedValue;
    public override int Size => sizeof(float);
    public override double ManagedValue { get; init; }

    public new VBSingleValue WithValue(double value) => this with { ManagedValue = (float)value };

    public bool Equals(IVBTypedValue<VBSingleValue, float>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
