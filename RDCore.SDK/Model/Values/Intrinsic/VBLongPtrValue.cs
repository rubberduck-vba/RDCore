using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <c>LongPtr</c> value.
/// </summary>
/// <param name="Is64Bit">Indicates whether the pointer is a 32-bit (<c>false</c>) or 64-bit (<c>true</c>) pointer.</param>
/// <param name="Symbol">The symbol associated with this value.</param>
public sealed record class VBLongPtrValue(bool Is64Bit, Symbol Symbol) 
    : VBNumericTypedValue(Is64Bit ? VBLongPtrType_x64.TypeInfo : VBLongPtrType_x86.TypeInfo, Symbol), IVBTypedValue<VBLongPtrValue, long>, INumericValue<VBLongPtrValue>
{
    public long Value => (long)ManagedValue;
    public override int Size => Is64Bit ? VBLongPtrType_x64.TypeInfo.Size : VBLongPtrType_x86.TypeInfo.Size;
    public override double ManagedValue { get; init; }

    public override object BoxedValue => ManagedValue;

    public bool Equals(IVBTypedValue<VBLongPtrValue, long>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
