using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <c>LongPtr</c> value.
/// </summary>
/// <param name="Symbol">The symbol associated with this value.</param>
public sealed record class VBLongPtrValue(bool Is64Bit, Symbol Symbol) : VBNumericTypedValue(VBLongPtrType_x86.TypeInfo, Symbol),
    IVBTypedValue<VBLongPtrValue, long>,
    INumericValue<VBLongPtrValue>
{
    public long Value => (long)ManagedValue;
    public override int Size => VBLongPtrType_x86.TypeInfo.Size;
    public override double ManagedValue { get; init; }

    public new VBLongPtrValue WithValue(double value) => WithValue(value, Is64Bit ? VBLongLongType.TypeInfo : VBLongType.TypeInfo);
    public VBLongPtrValue WithValue(double value, VBType ptrType)
    {
        if (ptrType is VBLongLongType)
        {
            if (value > VBLongLongType.MaxValue.ManagedValue || value < VBLongLongType.MinValue.ManagedValue)
            {
                throw VBRuntimeErrorException.Overflow(Symbol?.SelectionRange!, $"`{TypeInfo.Name}` values must be between **{VBLongLongType.MinValue.Value:N}** and **{VBLongLongType.MaxValue.Value:N}**.");
            }

            return this with { ManagedValue = (long)value };
        }

        if (ptrType is VBLongType)
        {
            if (value > VBLongType.MaxValue.ManagedValue || value < VBLongType.MinValue.ManagedValue)
            {
                throw VBRuntimeErrorException.Overflow(Symbol?.SelectionRange!, $"`{TypeInfo.Name}` values must be between **{VBLongType.MinValue:N}** and **{VBLongType.MaxValue:N}**.");
            }

            return this with { ManagedValue = (int)value };
        }

        throw new VBRuntimeErrorInternalErrorException($"{ptrType.Name} is not a valid or supported pointer type.");
    }

    public bool Equals(IVBTypedValue<VBLongPtrValue, long>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
