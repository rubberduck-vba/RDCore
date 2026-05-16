using RDCore.Parsing.Model.Symbols.Abstract;
using RDCore.Parsing.Model.Types.Intrinsic;
using RDCore.Parsing.Model.Values.Abstract;

namespace RDCore.Parsing.Model.Values.Intrinsic;

internal record class VBIntegerValue(Symbol? Symbol = null) : VBNumericTypedValue(VBIntegerType.TypeInfo, Symbol),
    IVBTypedValue<VBIntegerValue, short>,
    INumericValue<VBIntegerValue>
{
    public static VBIntegerValue MinValue { get; } = new VBIntegerValue { NumericValue = short.MinValue };
    public static VBIntegerValue MaxValue { get; } = new VBIntegerValue { NumericValue = short.MaxValue };
    public static VBIntegerValue Zero { get; } = new VBIntegerValue { NumericValue = 0 };

    VBIntegerValue INumericValue<VBIntegerValue>.MinValue => MinValue;
    VBIntegerValue INumericValue<VBIntegerValue>.Zero => Zero;
    VBIntegerValue INumericValue<VBIntegerValue>.MaxValue => MaxValue;

    public short Value => (short)NumericValue;
    public override int Size { get; } = sizeof(short);
    public override double NumericValue { get; init; }


    // this method should only ever need to be called for a value that lives allocated in an execution context.
    public new VBIntegerValue WithValue(double value)
    {
        if (value > MaxValue.Value || value < MinValue.Value)
        {
            // NOTE: if you are here debugging a NullReferenceException thrown here, **YOUR PRINCESS IS IN ANOTHER CASTLE**
            throw VBRuntimeErrorException.Overflow(Symbol?.Range!, $"`{TypeInfo.Name}` values must be between **{MinValue.Value:N}** and **{MaxValue.Value:N}**.");
        }
        return this with { NumericValue = (short)value };
    }

    public VBIntegerValue WithValue(int value) => WithValue((double)value);

    public bool Equals(IVBTypedValue<VBIntegerValue, short>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
