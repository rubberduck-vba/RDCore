using RDCore.External.Automation;

namespace RDCore.External.Hosting;

/// <summary>
/// The objects the external host holds for its environment host, by the handles they cross the wire as.
/// </summary>
/// <remarks>
/// An object has one handle for as long as it is held: a server that hands over the same object twice hands over the same handle, which is how the environment host
/// knows it is the same object - what <c>Is</c> asks.
/// </remarks>
internal sealed class ExternalHandles
{
    // RPC_E_DISCONNECTED: the object the handle was the handle of was let go of.
    private const int Disconnected = unchecked((int)0x80010108);

    private readonly object _gate = new();
    private readonly Dictionary<long, object> _objects = [];
    private readonly Dictionary<object, long> _handles = new(ReferenceEqualityComparer.Instance);
    private long _last;

    /// <summary>
    /// The handle of an object, which it is given the first time it crosses.
    /// </summary>
    public long HandleOf(object value)
    {
        lock (_gate)
        {
            if (!_handles.TryGetValue(value, out var handle))
            {
                handle = ++_last;
                _handles[value] = handle;
                _objects[handle] = value;
            }

            return handle;
        }
    }

    /// <summary>
    /// The object a handle is the handle of.
    /// </summary>
    /// <exception cref="AutomationException">The object was let go of.</exception>
    public object ObjectOf(long handle)
    {
        lock (_gate)
        {
            return _objects.TryGetValue(handle, out var value)
                ? value
                : throw new AutomationException(Disconnected, ExternalMessages.ObjectReleased);
        }
    }

    /// <summary>
    /// Forgets a handle, and gives the object it was the handle of.
    /// </summary>
    public bool TryRemove(long handle, out object? value)
    {
        lock (_gate)
        {
            if (!_objects.Remove(handle, out value))
            {
                return false;
            }

            _ = _handles.Remove(value);
            return true;
        }
    }
}
