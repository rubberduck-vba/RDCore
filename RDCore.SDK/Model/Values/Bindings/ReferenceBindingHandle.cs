using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.SDK.Model.Values.Bindings;

/// <summary>
/// Represents a handle to an internally addressed, writable reference to a <see cref="VBTypedValue"/>.
/// </summary>
public record class ReferenceBindingHandle : IBindingHandle
{
    private VBRuntimeReference _value;
    public ReferenceBindingHandle(VBRuntimeReference value)
    {
        _value = value;
    }

    public IRuntimeValue Value => _value;

    public BindingCapabilities BindingCapabilities => BindingCapabilities.GetValue | BindingCapabilities.SetValue;

    // TODO now that a resolver is in hand, GetValue should follow the reference through
    // resolver.TryRead(_value.Value, …) rather than returning the reference itself.
    public IRuntimeValue GetValue(ISymbolResolver resolver) => _value;

    public void SetValue(ISymbolResolver resolver, IRuntimeValue value) => _value = value is VBRuntimeReference reference
        ? reference : throw new ArgumentException($"Expected {nameof(VBRuntimeReference)} value", nameof(value));

    public IRuntimeValue Invoke(ISymbolResolver resolver, IRuntimeValue[] args) => throw new NotSupportedException();
}
