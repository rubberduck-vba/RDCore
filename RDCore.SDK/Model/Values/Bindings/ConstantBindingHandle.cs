using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.SDK.Model.Values.Bindings;

/// <summary>
/// Represents a handle to an internally addressed, read-only <see cref="IRuntimeValue"/>.
/// </summary>
public record class ConstantBindingHandle : IBindingHandle
{
    private IRuntimeValue _value;

    public ConstantBindingHandle(IRuntimeValue value)
    {
        _value = value;
    }

    public IRuntimeValue Value => _value;

    public BindingCapabilities BindingCapabilities => BindingCapabilities.GetValue;

    public IRuntimeValue GetValue() => _value;

    public void SetValue(IRuntimeValue value) => throw new NotSupportedException();

    public IRuntimeValue Invoke(IRuntimeValue[] args) => throw new NotSupportedException();
}
