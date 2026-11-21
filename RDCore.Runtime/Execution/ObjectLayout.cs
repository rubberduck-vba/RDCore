using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.Runtime.Execution;

// the instance fields an object of a class is made with, in the order the class declares them, and the number of the slot each is held in
// (ObjectInstance): found once for each class and version of the session's table (RuntimeSession.LayoutOf), as a procedure's locals are numbered once
// for each definition of it (FrameLayout).
internal sealed class ObjectLayout
{
    public static readonly ObjectLayout Empty = new([]);

    private readonly Dictionary<SemanticId, int> _slots;

    public ObjectLayout(Symbol[] fields)
    {
        _slots = new(fields.Length);
        for (var i = 0; i < fields.Length; i++)
        {
            if (!_slots.TryAdd(fields[i].SemanticId, i))
            {
                throw new InvalidOperationException($"'{fields[i].Uri}' is already declared on this instance.");
            }
        }

        Fields = fields;
    }

    public Symbol[] Fields { get; }

    public bool TryGetSlot(Symbol field, out int slot) => _slots.TryGetValue(field.SemanticId, out slot);
}
