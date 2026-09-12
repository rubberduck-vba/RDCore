using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents an <c>Error</c> value.
/// </summary>
public sealed record class VBErrorValue : VBTypedValue,
    IVBTypedValue<VBErrorValue, int>
{
    public VBErrorValue(IBindingHandle handle) : base(VBErrorType.TypeInfo)
    {
        Handle = handle;
    }
    public VBErrorValue(int value = 0) : this(new ValueBindingHandle(new VBRuntimeValue<int>(value))) { }

    public int Value => ((VBRuntimeValue<int>)RuntimeValue).StoredValue;
    public override int Size => sizeof(int);

    public bool Equals(IVBTypedValue<VBErrorValue, int>? other) => Value == other?.Value;
}