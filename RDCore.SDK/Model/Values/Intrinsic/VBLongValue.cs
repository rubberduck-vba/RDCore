using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <c>Long</c> value.
/// </summary>
public sealed record class VBLongValue : VBNumericTypedValue,
    IVBTypedValue<VBLongValue, int>,
    INumericValue<VBLongValue>
{
    public VBLongValue(IBindingHandle handle)
        : base(VBLongType.TypeInfo)
    {
        Handle = handle;
    }
    public VBLongValue(int value) : this(new ValueBindingHandle(new VBRuntimeValue<int>(value))) { }

    public int Value => ((VBRuntimeValue<int>)RuntimeValue).StoredValue;
    public override int Size => sizeof(int);

    public bool Equals(IVBTypedValue<VBLongValue, int>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
