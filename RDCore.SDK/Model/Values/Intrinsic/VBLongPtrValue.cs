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
        Handle = new ValueBindingHandle(new VBRuntimeValue<long>(value));
        Size = VBLongPtrType_x64.BitnessAwarePtrSize;
    }
    // a 32-bit pointer is a Long: its own type, and an Int32 in storage.
    public VBLongPtrValue(int value) : this(false)
    {
        Handle = new ValueBindingHandle(new VBRuntimeValue<int>(value));
        Size = VBLongPtrType_x86.BitnessAwarePtrSize;
    }

    // widened from whichever integer the platform's pointer is stored as: unboxing an Int32 as an Int64 would throw.
    public long Value => Convert.ToInt64(RuntimeValue.BoxedValue, System.Globalization.CultureInfo.InvariantCulture);
    public override int Size { get; }

    public bool Equals(IVBTypedValue<VBLongPtrValue, long>? other) => Value.Equals(other?.Value);
}
