using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents an <c>Error</c> value.
/// </summary>
/// <param name="Value">The numeric underlying value.</param>
public sealed record class VBErrorValue(int Value = 0) : VBTypedValue(VBErrorType.TypeInfo),
    IVBTypedValue<VBErrorValue, int>
{
    // for construction uniformity; the error code is carried positionally, not through the handle.
    public VBErrorValue(IBindingHandle handle) : this() { Handle = handle; }

    public override int Size => sizeof(int);

    public bool Equals(IVBTypedValue<VBErrorValue, int>? other) => Value == other?.Value;
}