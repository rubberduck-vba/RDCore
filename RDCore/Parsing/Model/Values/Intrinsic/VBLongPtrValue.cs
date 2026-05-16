using RDCore.Parsing.Model.Symbols.Abstract;
using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Types.Intrinsic;
using RDCore.Parsing.Model.Values.Abstract;

namespace RDCore.Parsing.Model.Values.Intrinsic;

internal record class VBLongPtrValue(Symbol? Symbol = null) : VBNumericTypedValue(VBLongPtrType.TypeInfo, Symbol),
    IVBTypedValue<VBLongPtrValue, long>,
    INumericValue<VBLongPtrValue>
{
    public static bool Is64Bit { get; set; } = true;
    public static int BitnessAwarePtrSize => Is64Bit ? sizeof(long) : sizeof(int);

    public static VBLongPtrValue MinValue { get; } = new VBLongPtrValue { NumericValue = Is64Bit ? long.MinValue : int.MinValue };
    public static VBLongPtrValue MaxValue { get; } = new VBLongPtrValue { NumericValue = Is64Bit ? long.MaxValue : int.MaxValue };
    public static VBLongPtrValue Zero { get; } = new VBLongPtrValue { NumericValue = 0 };

    VBLongPtrValue INumericValue<VBLongPtrValue>.MinValue => MinValue;
    VBLongPtrValue INumericValue<VBLongPtrValue>.Zero => Zero;
    VBLongPtrValue INumericValue<VBLongPtrValue>.MaxValue => MaxValue;

    public long Value => (long)NumericValue;
    public override int Size => BitnessAwarePtrSize;
    public override double NumericValue { get; init; }

    public new VBLongPtrValue WithValue(double value) => WithValue(value, Is64Bit ? VBLongLongType.TypeInfo : VBLongType.TypeInfo);
    public VBLongPtrValue WithValue(double value, VBType ptrType)
    {
        if (ptrType is VBLongLongType)
        {
            if (value > MaxValue.NumericValue || value < MinValue.NumericValue)
            {
                throw VBRuntimeErrorException.Overflow(Symbol?.SelectionRange!, $"`{TypeInfo.Name}` values must be between **{MinValue.Value:N}** and **{MaxValue.Value:N}**.");
            }

            return this with { NumericValue = (long)value };
        }

        if (ptrType is VBLongType)
        {
            if (value > VBLongValue.MaxValue.NumericValue || value < VBLongValue.MinValue.NumericValue)
            {
                throw VBRuntimeErrorException.Overflow(Symbol?.SelectionRange!, $"`{TypeInfo.Name}` values must be between **{int.MinValue:N}** and **{int.MaxValue:N}**.");
            }

            return this with { NumericValue = (int)value };
        }

        // this could be a bug in RDCore, but possibly also in the user code; if thrown, this exception will bubble unhandled through the execution context.
        // TODO test and see if there wouldn't happen to be a runtime or compile time error for this.
        throw new NotSupportedException($"{ptrType.Name} is not a valid or supported pointer type.");
    }

    public bool Equals(IVBTypedValue<VBLongPtrValue, long>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
