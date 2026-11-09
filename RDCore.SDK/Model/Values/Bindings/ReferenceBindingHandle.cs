using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Model.Values.Bindings;

/// <summary>
/// Represents a handle to an internally addressed, writable reference to a <see cref="VBTypedValue"/>.
/// </summary>
public record class ReferenceBindingHandle : IKnowledgeBinding
{
    private VBRuntimeReference _value;
    private bool _isKnown = true;

    public ReferenceBindingHandle(VBRuntimeReference value)
    {
        _value = value;
    }

    public IRuntimeValue Value => _value;

    /// <inheritdoc/>
    public bool IsKnown => _isKnown;

    public BindingCapabilities BindingCapabilities => BindingCapabilities.GetValue | BindingCapabilities.SetValue;

    // follows the reference through the resolver's runtime memory map; a reference that doesn't (yet)
    // resolve to anything bound falls back to yielding itself, e.g. Nothing or a dangling address.
    public IRuntimeValue GetValue(IRuntimeSymbolResolver resolver) => GetValue(resolver, [_value.Value]);

    // a chain of references can cycle (Set a = b : Set b = a) or self-reference (Set x = x); each hop
    // through another ReferenceBindingHandle is tracked by the address it targets next, so a repeat
    // stops the walk instead of recursing until the stack overflows.
    private IRuntimeValue GetValue(IRuntimeSymbolResolver resolver, HashSet<MemoryAddress> visited)
    {
        if (!resolver.TryRead(_value.Value, out var bound))
        {
            return _value;
        }

        if (bound is not ReferenceBindingHandle next)
        {
            return bound.GetValue(resolver);
        }

        return visited.Add(next._value.Value) ? next.GetValue(resolver, visited) : _value;
    }

    /// <remarks>
    /// Stores a reference that is known: a write that may be of one that is not says so with <see cref="Store"/>.
    /// </remarks>
    /// <param name="resolver">Unused: the reference is held by the handle.</param>
    /// <param name="value">The reference to store.</param>
    public void SetValue(IRuntimeSymbolResolver resolver, IRuntimeValue value) => Store(value, isKnown: true);

    /// <inheritdoc/>
    public void Store(IRuntimeValue value, bool isKnown)
    {
        _value = value is VBRuntimeReference reference
            ? reference : throw new ArgumentException($"Expected {nameof(VBRuntimeReference)} value", nameof(value));
        _isKnown = isKnown;
    }

    /// <inheritdoc/>
    public void Forget() => _isKnown = false;

    public IRuntimeValue Invoke(IRuntimeSymbolResolver resolver, IRuntimeValue[] args) => throw new NotSupportedException();
}
