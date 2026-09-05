using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// A <see cref="VBNumericTypedValue"/> representing a runtime value of the <see cref="VBByteType"/> data type.
/// </summary>
public sealed record class VBByteValue() : VBNumericTypedValue(VBByteType.TypeInfo),
    IVBTypedValue<VBByteValue, byte>,
    INumericValue<VBByteValue>
{
    public VBByteValue(IBindingHandle handle) : this() { Handle = handle; }
    public VBByteValue(byte value) : this(new ValueBindingHandle(new VBRuntimeValue<byte>(value))) { }

    public byte Value => ((VBRuntimeValue<byte>)UnderlyingValue.RuntimeValue!).Value;
    public override int Size { get; } = sizeof(byte);

    public bool Equals(IVBTypedValue<VBByteValue, byte>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
