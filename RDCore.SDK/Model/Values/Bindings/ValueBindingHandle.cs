using RDCore.SDK.Model.Values.Interop;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.SDK.Model.Values.Bindings;

/// <summary>
/// Represents a handle to an internally addressed, writable <see cref="IManagedInteropValue"/>.
/// </summary>
public record class ValueBindingHandle : IBindingHandle
{
    private IManagedInteropValue _value;

    public ValueBindingHandle(IManagedInteropValue value)
    {
        _value = value;
    }

    public BindingCapabilities BindingCapabilities => BindingCapabilities.GetValue | BindingCapabilities.SetValue;

    public IManagedInteropValue GetValue(IVBExecutionContext context) => _value;

    public void SetValue(IVBExecutionContext context, IManagedInteropValue value) => _value = value;

    public IManagedInteropValue Invoke(IVBExecutionContext context, IManagedInteropValue[] args) => throw new NotSupportedException();
}
