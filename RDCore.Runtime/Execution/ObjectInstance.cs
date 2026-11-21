using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.Runtime.Execution;

/// <summary>
/// A live object instance: the activation record of one <c>New</c>-created object. Each instance field its class module declares is held in a slot of
/// its own, numbered by the class's layout (<see cref="ObjectLayout"/>), as <see cref="Frames.CallStackFrame"/> holds a procedure's locals: this is
/// what gives two instances of the same class module independent bindings for the "same" declared field. A field is given an address in the
/// <see cref="ISessionStorage"/> the session's module/global symbols and call-stack frames use only when one is asked for (<see cref="TryGetAddress"/>).
/// </summary>
/// <remarks>
/// Given the table its class is declared in, the instance is an instance of the class as it is declared now: a class module defined again while the object
/// lives - an edit made while the program waits - gives it the members the class has now.
/// </remarks>
public sealed class ObjectInstance : IObjectInstance
{
    private readonly VBRuntimeObjectId _objectId;
    private readonly VBClassModuleSymbol _classModule;
    private readonly ISessionStorage _storage;
    private readonly ObjectLayout _layout;
    private readonly SymbolTable? _table;

    // the slots of the layout's fields, then one for each field or Static local declared on the object since it was created (Push), whose numbers _added
    // keeps.
    private Slot[] _slots;
    private Dictionary<SemanticId, int>? _added;
    private List<Symbol>? _fields;
    private List<Symbol>? _staticLocals;

    /// <summary>
    /// An instance with no field: each is declared on it with <see cref="Push"/>.
    /// </summary>
    /// <param name="objectId">The identity of the object.</param>
    /// <param name="classModule">Its class.</param>
    /// <param name="storage">The storage a field is given an address in.</param>
    public ObjectInstance(VBRuntimeObjectId objectId, VBClassModuleSymbol classModule, ISessionStorage storage)
        : this(objectId, classModule, storage, null, ObjectLayout.Empty)
    {
    }

    // an instance laid out as its class is in the table: each field of the layout is held in its slot once Hold gives it its initial value.
    internal ObjectInstance(VBRuntimeObjectId objectId, VBClassModuleSymbol classModule, ISessionStorage storage, SymbolTable? table, ObjectLayout layout)
    {
        _objectId = objectId;
        _classModule = classModule;
        _storage = storage;
        _table = table;
        _layout = layout;
        _slots = layout.Fields.Length == 0 ? [] : new Slot[layout.Fields.Length];
    }

    public VBRuntimeObjectId ObjectId => _objectId;

    // a class the table does not declare (the library's, a late-bound server's) is the one the object was created as. The class the table declares is
    // looked up again only after the table changes (SymbolTable.Version): a member is called on an object far more often than a module is defined.
    public VBClassModuleSymbol ClassModule
    {
        get
        {
            if (_table is null)
            {
                return _classModule;
            }

            if (_declaredClass is { } known && known.Version == _table.Version)
            {
                return known.Class;
            }

            var found = _table.TryGet(_classModule, _classModule.ScopeKind, out var current) && current is VBClassModuleSymbol declared
                ? declared
                : _classModule;
            _declaredClass = new DeclaredClass(_table.Version, found);
            return found;
        }
    }

    // the class the table declared when it was at a version, kept together so a thread that reads them while another replaces them reads a pair.
    private sealed record class DeclaredClass(int Version, VBClassModuleSymbol Class);

    private DeclaredClass? _declaredClass;

    /// <inheritdoc/>
    public IReadOnlyCollection<Symbol> StaticLocals => (IReadOnlyCollection<Symbol>?)_staticLocals ?? [];

    /// <inheritdoc/>
    public IReadOnlyList<Symbol> Fields => (IReadOnlyList<Symbol>?)_fields ?? _layout.Fields;

    /// <summary>
    /// Holds <paramref name="value"/>, the initial value of the field of the layout numbered <paramref name="slot"/>, in that field's slot.
    /// </summary>
    /// <param name="slot">The number of the field in the layout the instance was made with.</param>
    /// <param name="value">Its initial value.</param>
    internal void Hold(int slot, VBTypedValue value) => _slots[slot] = Slot.Of(value);

    /// <inheritdoc/>
    public void Push(Symbol field, VBTypedValue value)
    {
        if (TryGetSlot(field, out _))
        {
            throw new InvalidOperationException($"'{field.Uri}' is already declared on this instance.");
        }

        var slot = _slots.Length;
        Array.Resize(ref _slots, slot + 1);
        _slots[slot] = Slot.Of(value);
        (_added ??= [])[field.SemanticId] = slot;
        if (field is VBLocalVariableSymbol { IsStatic: true })
        {
            (_staticLocals ??= []).Add(field);
        }
        else
        {
            (_fields ??= [.. _layout.Fields]).Add(field);
        }
    }

    /// <summary>
    /// Whether the object holds <paramref name="local"/>, a <c>Static</c> of one of its class's members, given storage by a call of it.
    /// </summary>
    /// <param name="local">The <c>Static</c>.</param>
    internal bool HoldsStatic(Symbol local) => _staticLocals is { } held && held.Exists(one => one.SemanticId == local.SemanticId);

    /// <summary>
    /// Lets go of <paramref name="local"/>, a <c>Static</c> of one of its class's members whose edited definition declares it as another type, or no longer
    /// declares it: what it holds is let go of, its storage is freed, and the member's next call on the object gives it storage again.
    /// </summary>
    /// <param name="local">The <c>Static</c>.</param>
    /// <param name="letGo">What lets go of what it holds.</param>
    /// <returns>Whether the object held it.</returns>
    internal bool ForgetStatic(Symbol local, Action<IBindingHandle>? letGo)
    {
        var index = _staticLocals?.FindIndex(held => held.SemanticId == local.SemanticId) ?? -1;
        if (index < 0 || !TryGetSlot(local, out var slot))
        {
            return false;
        }

        if (letGo is not null && TryResolve(local, out var cell))
        {
            letGo(cell);
        }

        Free(_slots[slot]);
        _slots[slot] = default;
        _ = _added!.Remove(local.SemanticId);
        _staticLocals!.RemoveAt(index);
        return true;
    }

    /// <inheritdoc/>
    public IBindingHandle GetValue(Symbol field)
        => TryResolve(field, out var value) ? value : throw new KeyNotFoundException($"No runtime binding exists yet for '{field.Uri}'.");

    /// <inheritdoc/>
    public bool TryResolve(Symbol field, [NotNullWhen(true)][MaybeNullWhen(false)] out IBindingHandle? value)
    {
        value = null;
        if (!TryGetSlot(field, out var slot))
        {
            return false;
        }

        ref readonly var held = ref _slots[slot];
        if (held.Cell is { } cell)
        {
            value = cell;
            return true;
        }

        return _storage.TryRead(held.Address, out value);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A field is given its address the first time one is asked for, and keeps it for as long as the object lives. A field whose cell a <c>ByRef</c>
    /// parameter of a call was asked the address of first has that address, which is its own from then on: <c>VarPtr</c> of a <c>ByRef</c> parameter is
    /// its variable's (<strong>MS-VBA</strong>).
    /// </remarks>
    public bool TryGetAddress(Symbol field, out MemoryAddress address)
    {
        address = default;
        if (!TryGetSlot(field, out var slot))
        {
            return false;
        }

        ref var held = ref _slots[slot];
        if (held.Cell is not { } cell)
        {
            address = held.Address;
            return true;
        }

        // the cell is bound at its address, and read there from then on: something other than a cell may be bound at an address (a POKE rebinds one that is
        // not a cell), which a slot that held the cell would not see.
        if (cell is Frames.IAddressedCell { Address: { } bound })
        {
            address = bound;
        }
        else if (!_storage.TryAllocate(held.Size, cell, out address))
        {
            return false;
        }

        held = new Slot(true, null, address, 0);
        return true;
    }

    /// <summary>
    /// Frees every field this instance allocated. Called once the instance's reference count reaches
    /// zero and <see cref="ISessionObjects.TryRemoveObject"/> succeeds — an instance is never partially
    /// torn down.
    /// </summary>
    public void ReleaseAll()
    {
        foreach (var held in _slots)
        {
            Free(held);
        }

        _slots = [];
        _added = null;
    }

    // a field is found by its number in the layout, or among those declared since; a slot of a layout is the object's only while it holds its fields
    // (ReleaseAll).
    private bool TryGetSlot(Symbol field, out int slot)
        => (_layout.TryGetSlot(field, out slot) || _added is { } added && added.TryGetValue(field.SemanticId, out slot)) && (uint)slot < (uint)_slots.Length
            && _slots[slot].IsHeld;

    // the address a field was given: by the object, or by a ByRef parameter of a call that was given the field's cell and asked for its address.
    private void Free(in Slot held)
    {
        if (!held.IsHeld)
        {
            return;
        }

        if (held.Cell is null)
        {
            _ = _storage.TryDeallocate(held.Address);
        }
        else if (held.Cell is Frames.IAddressedCell { Address: { } given })
        {
            _ = _storage.TryDeallocate(given);
        }
    }

    // a field of the object: its cell until it is given an address, and the address from then on. Size: what it is given an address for.
    private readonly record struct Slot(bool IsHeld, IBindingHandle? Cell, MemoryAddress Address, int Size)
    {
        // a field of a scalar type (or a Variant holding one) is held as a payload, as a local is (Frames.CallStackFrame.CellFor): what the code of the
        // class evaluates its fields on.
        public static Slot Of(VBTypedValue value) => new(true, Frames.CallStackFrame.CellFor(value), default, value.Size);
    }
}
