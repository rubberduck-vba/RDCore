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

    // follows the reference through the resolver's runtime memory map; a reference that doesn't (yet)
    // resolve to anything bound falls back to yielding itself, e.g. Nothing or a dangling address.
    public IRuntimeValue GetValue(ISymbolResolver resolver)
        => resolver.TryRead(_value.Value, out var bound) ? bound.GetValue(resolver) : _value;

    public void SetValue(ISymbolResolver resolver, IRuntimeValue value) => _value = value is VBRuntimeReference reference
        ? reference : throw new ArgumentException($"Expected {nameof(VBRuntimeReference)} value", nameof(value));

    public IRuntimeValue Invoke(ISymbolResolver resolver, IRuntimeValue[] args) => throw new NotSupportedException();
}
