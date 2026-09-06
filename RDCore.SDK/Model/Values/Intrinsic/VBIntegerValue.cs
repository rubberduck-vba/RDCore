using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents an <c>Integer</c> value
/// </summary>
public record class VBIntegerValue : VBNumericTypedValue,
    IVBTypedValue<VBIntegerValue, short>,
    INumericValue<VBIntegerValue>
{
    public VBIntegerValue(IBindingHandle handle)
        : base(VBIntegerType.TypeInfo)
    {
        Handle = handle;
    }
    public VBIntegerValue(short value) : this(new ValueBindingHandle(new VBRuntimeValue<short>(value))) { }

    public short Value => ((VBRuntimeValue<short>)RuntimeValue).StoredValue;
    public override int Size { get; } = sizeof(short);

    public bool Equals(IVBTypedValue<VBIntegerValue, short>? other) => Value == other?.Value;
}
