using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

public record class VBIntegerValue(Symbol Symbol) : VBNumericTypedValue(VBIntegerType.TypeInfo, Symbol),
    IVBTypedValue<VBIntegerValue, short>,
    INumericValue<VBIntegerValue>
{
    public short Value => (short)ManagedValue;
    public override int Size { get; } = sizeof(short);
    public override double ManagedValue { get; init; }

    public new VBIntegerValue WithValue(double value)
    {
        if (value > VBIntegerType.MaxValue.Value || value < VBIntegerType.MinValue.Value)
        {
            ThrowWithSymbol(symbol => VBRuntimeErrorException.Overflow(Symbol?.Range!, $"`{TypeInfo.Name}` values must be between **{VBIntegerType.MinValue.Value:N}** and **{VBIntegerType.MaxValue.Value:N}**."));
        }
        return this with { ManagedValue = (short)value };
    }

    public VBIntegerValue WithValue(short value) => WithValue((double)value);

    public bool Equals(IVBTypedValue<VBIntegerValue, short>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
