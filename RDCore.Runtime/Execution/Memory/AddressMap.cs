using System.Diagnostics.CodeAnalysis;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.Execution.Memory;

// what is held at addresses, by address and in address order: what lies in a range of memory being freed is found without looking at anything outside it.
internal sealed class AddressMap<T>
{
    private readonly Dictionary<MemoryAddress, T> _byAddress = [];
    private readonly SortedSet<int> _addresses = [];

    public int Count => _byAddress.Count;

    public T this[MemoryAddress address]
    {
        get => _byAddress[address];
        set
        {
            _byAddress[address] = value;
            _addresses.Add(address.Value);
        }
    }

    public bool TryGetValue(MemoryAddress address, [MaybeNullWhen(false)] out T value) => _byAddress.TryGetValue(address, out value);

    public T? GetValueOrDefault(MemoryAddress address) => _byAddress.GetValueOrDefault(address);

    public bool Remove(MemoryAddress address) => _byAddress.Remove(address) && _addresses.Remove(address.Value);

    // the addresses held in the size bytes from start, in address order, as they are before any of them is removed.
    public MemoryAddress[] In(MemoryAddress start, int size)
    {
        var end = (long)start.Value + size;
        if (size <= 0 || _addresses.Count == 0 || _addresses.Max < start.Value || _addresses.Min >= end)
        {
            return [];
        }

        var held = _addresses.GetViewBetween(start.Value, (int)Math.Min(int.MaxValue, end - 1));
        var found = new MemoryAddress[held.Count];
        var at = 0;
        foreach (var address in held)
        {
            found[at++] = new MemoryAddress(address);
        }

        return found;
    }
}
