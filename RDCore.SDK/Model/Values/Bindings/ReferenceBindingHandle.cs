using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Interop;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.SDK.Model.Values.Bindings;

/// <summary>
/// Represents a handle to an internally addressed, writable reference to a <see cref="VBTypedValue"/>.
/// </summary>
public record class ReferenceBindingHandle : IBindingHandle
{
    private ManagedInteropReference _value;
    public ReferenceBindingHandle(ManagedInteropReference value)
    {
        _value = value;
    }

    public BindingCapabilities BindingCapabilities => BindingCapabilities.GetValue | BindingCapabilities.SetValue;

    public IManagedInteropValue GetValue(IVBExecutionContext context) => _value;

    public void SetValue(IVBExecutionContext context, IManagedInteropValue value) => _value = value is ManagedInteropReference reference
        ? reference : throw new ArgumentException($"Expected {nameof(ManagedInteropReference)} value", nameof(value));

    public IManagedInteropValue Invoke(IVBExecutionContext context, IManagedInteropValue[] args) => throw new NotSupportedException();
}
