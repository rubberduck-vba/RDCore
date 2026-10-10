using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.Runtime.Execution.External;

/// <inheritdoc cref="ISessionExternalObjects"/>
/// <remarks>
/// A handle is found again by reference: a provider that hands out the same handle for the same external object (a COM runtime-callable wrapper is one)
/// gets the same object identity for it, and two variables holding it are the same object.
/// </remarks>
internal sealed class SessionExternalObjects : ISessionExternalObjects
{
    private readonly record struct Binding(IExternalObjectOwner Owner, object Handle);

    private readonly Dictionary<VBRuntimeObjectId, Binding> _bound = [];
    private readonly Dictionary<object, VBRuntimeObjectId> _identities = new(ReferenceEqualityComparer.Instance);

    /// <inheritdoc/>
    public void Bind(VBRuntimeObjectId objectId, IExternalObjectOwner owner, object handle)
    {
        _bound[objectId] = new Binding(owner, handle);
        _identities[handle] = objectId;
    }

    /// <inheritdoc/>
    public bool TryGet(VBRuntimeObjectId objectId, [NotNullWhen(true)] out IExternalObjectOwner? owner, [NotNullWhen(true)] out object? handle)
    {
        if (_bound.TryGetValue(objectId, out var binding))
        {
            (owner, handle) = (binding.Owner, binding.Handle);
            return true;
        }

        (owner, handle) = (null, null);
        return false;
    }

    /// <inheritdoc/>
    public bool TryFind(IExternalObjectOwner owner, object handle, out VBRuntimeObjectId objectId)
        => _identities.TryGetValue(handle, out objectId) && _bound.TryGetValue(objectId, out var binding) && ReferenceEquals(binding.Owner, owner);

    /// <inheritdoc/>
    public void Release(VBRuntimeObjectId objectId)
    {
        if (!_bound.Remove(objectId, out var binding))
        {
            return;
        }

        _identities.Remove(binding.Handle);
        binding.Owner.Release(binding.Handle);
    }

    /// <inheritdoc/>
    public void ReleaseAll()
    {
        var all = _bound.Values.ToArray();
        _bound.Clear();
        _identities.Clear();
        foreach (var binding in all)
        {
            binding.Owner.Release(binding.Handle);
        }
    }
}
