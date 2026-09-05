using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <c>LongPtr</c> value.
/// </summary>
/// <param name="Is64Bit">Indicates whether the pointer is a 32-bit (<c>false</c>) or 64-bit (<c>true</c>) pointer.</param>
public sealed record class VBLongPtrValue(bool Is64Bit)
    : VBNumericTypedValue(Is64Bit ? VBLongPtrType_x64.TypeInfo : VBLongPtrType_x86.TypeInfo),
    IVBTypedValue<VBLongPtrValue, long>, INumericValue<VBLongPtrValue>
{
    public VBLongPtrValue(IBindingHandle handle) : this(true) { Handle = handle; }
    public VBLongPtrValue(long value) : this(true)
    {
        UnderlyingValue = new(new VBRuntimeValue<long>(value));
        Size = VBLongPtrType_x64.TypeInfo.Size;
    }
    public VBLongPtrValue(int value) : this(true)
    {
        UnderlyingValue = new(new VBRuntimeValue<int>(value));
        Size = VBLongPtrType_x86.TypeInfo.Size;
    }

    public long Value => (long)UnderlyingValue.RuntimeValue!.BoxedValue;
    public override int Size { get; }

    public bool Equals(IVBTypedValue<VBLongPtrValue, long>? other) => Value.Equals(other?.Value);
    public override int GetHashCode() => Value.GetHashCode();
}
