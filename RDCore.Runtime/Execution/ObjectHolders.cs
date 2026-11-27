using RDCore.SDK.Model.Values.Bindings;

namespace RDCore.Runtime.Execution;

/// <summary>
/// The list of the cells that hold a reference to one object: a cell is in it once for each time it took the object and has not let go of it, and the
/// object's reference count is the list's length. A cell is found by its identity: a cell is a record, and two cells holding the same object compare
/// equal by value while being two cells.
/// </summary>
/// <remarks>
/// A list of few cells is searched from its last: the cell that lets go of an object is most often the last that took it. A list of more cells is given
/// an index beside it, which finds each cell's places in the list, so that adding, finding and removing a cell take the same time however many hold the
/// object: an array of a hundred thousand elements set to one object, or the children of an object model that each hold their parent. A cell removed from
/// an indexed list leaves its place to the last cell of the list.
/// </remarks>
internal sealed class ObjectHolders
{
    // past this many cells the list is indexed.
    internal const int Listed = 16;

    private IBindingHandle?[] _cells = [];
    private Index? _index;

    /// <summary>
    /// How many cells the list holds, a cell once for each time it holds the object: the object's reference count.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// Adds <paramref name="cell"/> to the list, once more if it is in it already.
    /// </summary>
    /// <param name="cell">The cell that took a reference to the object.</param>
    public void Add(IBindingHandle cell)
    {
        if (Count == _cells.Length)
        {
            Array.Resize(ref _cells, Math.Max(4, 2 * _cells.Length));
            _index?.Resize(_cells.Length);
        }

        _cells[Count] = cell;
        _index?.Add(cell, Count);
        Count++;
        if (_index is null && Count > Listed)
        {
            _index = new Index(_cells.Length);
            for (var at = 0; at < Count; at++)
            {
                _index.Add(_cells[at]!, at);
            }
        }
    }

    /// <summary>
    /// Takes <paramref name="cell"/> off the list once, when it is in it.
    /// </summary>
    /// <param name="cell">The cell that lets go of its reference to the object.</param>
    public void Remove(IBindingHandle cell)
    {
        var last = Count - 1;
        if (_index is { } index)
        {
            if (!index.TryTake(cell, out var at))
            {
                return;
            }

            if (at != last)
            {
                _cells[at] = _cells[last];
                index.Move(_cells[at]!, last, at);
            }
        }
        else
        {
            var at = IndexOf(cell);
            if (at < 0)
            {
                return;
            }

            Array.Copy(_cells, at + 1, _cells, at, last - at);
        }

        _cells[last] = null;
        Count--;
    }

    /// <summary>
    /// Whether <paramref name="cell"/> is in the list: whether it holds the object.
    /// </summary>
    /// <param name="cell">The cell.</param>
    public bool Contains(IBindingHandle cell) => _index?.Contains(cell) ?? IndexOf(cell) >= 0;

    // searched without a predicate, which would be made for each reference counted.
    private int IndexOf(IBindingHandle cell)
    {
        for (var at = Count - 1; at >= 0; at--)
        {
            if (ReferenceEquals(_cells[at], cell))
            {
                return at;
            }
        }

        return -1;
    }

    // where each cell is in the list: one of its places by its identity, and its other places, if any, linked to that one in both directions.
    private sealed class Index(int capacity)
    {
        private readonly Dictionary<IBindingHandle, int> _first = new(ReferenceEqualityComparer.Instance);
        private int[] _next = new int[capacity];
        private int[] _previous = new int[capacity];

        public void Resize(int capacity)
        {
            Array.Resize(ref _next, capacity);
            Array.Resize(ref _previous, capacity);
        }

        public bool Contains(IBindingHandle cell) => _first.ContainsKey(cell);

        // the cell at a new place, linked in front of its other places.
        public void Add(IBindingHandle cell, int at)
        {
            if (_first.TryGetValue(cell, out var first))
            {
                (_next[at], _previous[first]) = (first, at);
            }
            else
            {
                _next[at] = -1;
            }

            _previous[at] = -1;
            _first[cell] = at;
        }

        // one place of the cell, unlinked from its others.
        public bool TryTake(IBindingHandle cell, out int at)
        {
            if (!_first.TryGetValue(cell, out at))
            {
                return false;
            }

            if (_next[at] is >= 0 and var next)
            {
                (_first[cell], _previous[next]) = (next, -1);
            }
            else
            {
                _ = _first.Remove(cell);
            }

            return true;
        }

        // the cell at place from now at place to, which its links follow.
        public void Move(IBindingHandle cell, int from, int to)
        {
            var (next, previous) = (_next[from], _previous[from]);
            (_next[to], _previous[to]) = (next, previous);
            if (previous >= 0)
            {
                _next[previous] = to;
            }
            else
            {
                _first[cell] = to;
            }

            if (next >= 0)
            {
                _previous[next] = to;
            }
        }
    }
}
