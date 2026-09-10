using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.SDK.Model.Values.Bindings;

/// <summary>
/// Represents a handle to an internally addressed, writable <see cref="IRuntimeValue"/>.
/// </summary>
public record class ValueBindingHandle : IBindingHandle
{
    private IRuntimeValue _value;

    public ValueBindingHandle(IRuntimeValue value)
    {
        _value = value;
    }

    public IRuntimeValue Value => _value;

    public BindingCapabilities BindingCapabilities => BindingCapabilities.GetValue | BindingCapabilities.SetValue;

    public IRuntimeValue GetValue() => _value;

    public void SetValue(IRuntimeValue value) => _value = value;

    public IRuntimeValue Invoke(IRuntimeValue[] args) => throw new NotSupportedException();
}
