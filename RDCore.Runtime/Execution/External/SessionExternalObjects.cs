using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.Runtime.Execution.External;

/// <inheritdoc cref="ISessionExternalObjects"/>
/// <remarks>
/// A handle is found again by reference: a provider that hands out the same handle for the same external object (a COM runtime-callable wrapper is one)
/// gets the same object identity for it, and two variables holding it are the same object.
/// </remarks>
// a provider is a server, told of a release with the turn given up, as any call of it is made: what it does to let go - an RDForms form destroying its window
// on the thread of its windows - can wait for a thread that is handling an event of this session, which takes the turn.
internal sealed class SessionExternalObjects(ISessionTurn turn) : ISessionExternalObjects
{
    private readonly record struct Binding(IExternalObjectOwner Owner, object Handle);

    private readonly Dictionary<VBRuntimeObjectId, Binding> _bound = [];
    private readonly Dictionary<object, VBRuntimeObjectId> _identities = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<VBRuntimeObjectId> _listened = [];

    /// <inheritdoc/>
    public bool TryBeginListening(VBRuntimeObjectId objectId) => _bound.ContainsKey(objectId) && _listened.Add(objectId);

    /// <inheritdoc/>
    public bool TryEndListening(VBRuntimeObjectId objectId) => _listened.Remove(objectId);

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
        _listened.Remove(objectId);
        LetGo([binding]);
    }

    /// <inheritdoc/>
    public void ReleaseAll()
    {
        var all = _bound.Values.ToArray();
        _bound.Clear();
        _identities.Clear();
        _listened.Clear();
        LetGo(all);
    }

    private void LetGo(Binding[] bindings)
    {
        using (turn.Yield())
        {
            foreach (var binding in bindings)
            {
                binding.Owner.Release(binding.Handle);
            }
        }
    }
}
