using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a <c>Double</c> value.
/// </summary>
public sealed record class VBDoubleValue : VBNumericTypedValue,
    IVBTypedValue<VBDoubleValue, double>, INumericValue<VBDoubleValue>
{
    public VBDoubleValue(IBindingHandle handle)
        : base(VBDoubleType.TypeInfo)
    {
        Handle = handle;
    }
    public VBDoubleValue(double value) : this(new ValueBindingHandle(new VBRuntimeValue<double>(value))) { }

    public double Value => ((VBRuntimeValue<double>)RuntimeValue).Value;
    public override int Size => 8;

    public bool Equals(IVBTypedValue<VBDoubleValue, double>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
