using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// A <see cref="VBNumericTypedValue"/> representing a runtime value of the <see cref="VBDecimalType"/> data type.
/// </summary>
public sealed record class VBDecimalValue()
    : VBNumericTypedValue(VBDecimalType.TypeInfo), IVBTypedValue<VBDecimalValue, decimal>, INumericValue<VBDecimalValue>
{
    public VBDecimalValue(IBindingHandle handle) : this() { Handle = handle; }
    public VBDecimalValue(decimal value) : this(new ValueBindingHandle(new VBRuntimeValue<VBRuntimeDecimalValue>(new VBRuntimeDecimalValue(value)))) { }

    public decimal Value => ((VBRuntimeValue<VBRuntimeDecimalValue>)RuntimeValue).Value.ManagedValue;
    public override int Size => sizeof(Decimal);

    public bool Equals(IVBTypedValue<VBDecimalValue, decimal>? other) => Value == other?.Value;
}
