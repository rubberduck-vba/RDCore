using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.SDK.Model.Values.Bindings;

/// <summary>
/// Represents a handle to an internally addressed, writable reference to a <see cref="VBTypedValue"/>.
/// </summary>
public record class ReferenceBindingHandle : IBindingHandle
{
    private  VBTypedValue _value;
    public ReferenceBindingHandle(VBTypedValue value)
    {
        _value = value;
    }

    public BindingCapabilities BindingCapabilities => BindingCapabilities.GetValue | BindingCapabilities.SetValue;

    public VBTypedValue GetValue(IVBExecutionContext context) => _value;

    public void SetValue(IVBExecutionContext context, VBTypedValue value) => _value = value;

    public VBTypedValue Invoke(IVBExecutionContext context, VBTypedValue[] args) => throw new NotSupportedException();
}
