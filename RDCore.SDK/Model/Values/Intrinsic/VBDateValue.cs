using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents a runtime value of the <see cref="VBDateType"/> data type.
/// </summary>
public sealed record class VBDateValue : VBTypedValue, 
    IVBTypedValue<VBDateValue, DateTime>
{
    public VBDateValue(IBindingHandle handle)
        : base(VBDateType.TypeInfo)
    {
        Handle = handle;
    }
    public VBDateValue(double value) : this(new ValueBindingHandle(new VBRuntimeValue<double>(value))) { }

    /// <summary>
    /// Gets the <c>DateSerial</c> (<c>double</c>) underlying numeric representation of the date value.
    /// </summary>
    /// <remarks>
    /// This representation is natively compatible with how dates are represented in <em>Microsoft Excel</em>.
    /// </remarks>
    public double SerialValue => ((VBRuntimeValue<double>)RuntimeValue).StoredValue;

    public DateTime Value => DateTime.FromOADate(((VBRuntimeValue<double>)RuntimeValue).StoredValue);
    public override int Size => sizeof(double);

    public bool Equals(IVBTypedValue<VBDateValue, DateTime>? other) => Value == other?.Value;
}
