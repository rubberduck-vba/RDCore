using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

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

    public IRuntimeValue GetValue(ISymbolResolver resolver) => _value;

    public void SetValue(ISymbolResolver resolver, IRuntimeValue value) => _value = value;

    public IRuntimeValue Invoke(ISymbolResolver resolver, IRuntimeValue[] args) => throw new NotSupportedException();
}
