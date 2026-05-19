using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Intrinsic;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

public record class VBIntegerValue(Symbol Symbol) : VBNumericTypedValue(VBIntegerType.TypeInfo, Symbol),
    IVBTypedValue<VBIntegerValue, short>,
    INumericValue<VBIntegerValue>
{
    public static VBIntegerValue MinValue { get; } = new VBIntegerValue(GlobalSymbols.VBIntegerMinValue) { ManagedValue = short.MinValue };
    public static VBIntegerValue MaxValue { get; } = new VBIntegerValue(GlobalSymbols.VBIntegerMaxValue) { ManagedValue = short.MaxValue };
    public static VBIntegerValue Zero { get; } = new VBIntegerValue(GlobalSymbols.VBIntegerZeroValue) { ManagedValue = 0 };

    VBIntegerValue INumericValue<VBIntegerValue>.MinValue => MinValue;
    VBIntegerValue INumericValue<VBIntegerValue>.Zero => Zero;
    VBIntegerValue INumericValue<VBIntegerValue>.MaxValue => MaxValue;

    public short Value => (short)ManagedValue;
    public override int Size { get; } = sizeof(short);
    public override double ManagedValue { get; init; }


    public new VBIntegerValue WithValue(double value)
    {
        if (value > MaxValue.Value || value < MinValue.Value)
        {
            ThrowWithSymbol(symbol => VBRuntimeErrorException.Overflow(Symbol?.Range!, $"`{TypeInfo.Name}` values must be between **{MinValue.Value:N}** and **{MaxValue.Value:N}**."));
        }
        return this with { ManagedValue = (short)value };
    }

    public VBIntegerValue WithValue(short value) => WithValue((double)value);

    public bool Equals(IVBTypedValue<VBIntegerValue, short>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
