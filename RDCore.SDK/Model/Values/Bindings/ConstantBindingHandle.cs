using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

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

    public IRuntimeValue GetValue(ISymbolResolver resolver) => _value;

    public void SetValue(ISymbolResolver resolver, IRuntimeValue value) => throw new NotSupportedException();

    public IRuntimeValue Invoke(ISymbolResolver resolver, IRuntimeValue[] args) => throw new NotSupportedException();
}
