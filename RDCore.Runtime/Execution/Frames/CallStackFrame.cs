using RDCore.Runtime.Execution;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Binding;
using RDCore.SDK.Semantics.Instructions;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.Runtime.Execution.Frames;

/// <summary>
/// Represents a single <em>call stack</em> frame — the activation record of one procedure call. Every
/// locally-scoped <see cref="Symbol"/> the procedure declares (its parameters and its <c>Dim</c>
/// locals alike, MS-VBAL drawing no distinction between the two for name-resolution purposes) is
/// <see cref="Push"/>ed here and reserved storage through the same <see cref="ISessionStorage"/> the
/// session's module/global symbols use — this is what makes the frame's members addressable, not just
/// held in a private lookup, and what lets <see cref="ReleaseAll"/> free them all in one pass when the
/// frame is popped.
/// </summary>
public sealed record class CallStackFrame(SyntaxNodeId NodeId, StaticSymbol StaticSymbol, ImmutableArray<VBTypedValue> Inputs, ISessionStorage Storage, ModuleDirectives Directives = default) : ICallStackFrame
{
    // made when the activation first needs one (IR lowering S0.9, S1.3): most activations have no ByRef alias, no block, no loop and no GoSub, and one that has
    // none does not hash a symbol's identity to find out - and, laid out, no variable without a slot and no variable that was given an address.
    private SymbolAddressTable? _addresses;
    private HashSet<SemanticId>? _declared;

    // what few activations have - a ByRef alias by address, a block, a loop, a GoSub, a variable declared at a stop - is kept apart, made when it is first
    // needed (IR lowering S1.7): an activation that has none is that much smaller, and most calls make one that has none.
    private sealed class Occasional
    {
        public Dictionary<SemanticId, MemoryAddress>? ByRefAliases;
        public Dictionary<SemanticId, VBType>? ReferencedTypes;
        public Dictionary<int, VBTypedValue>? BlockState;
        public Dictionary<int, ScalarPayload>? SelectorPayloads;
        public Dictionary<int, ForLoopState>? ForLoopState;
        public Dictionary<int, ForEachState>? ForEachState;
        public Stack<int>? GoSubReturns;
        public Dictionary<int, (int? End, ValueBindingHandle Cell)>? BlockHolders;
        public List<Symbol>? DeclaredAtAStop;
    }

    private Occasional? _occasional;

    private Occasional Occasionally => _occasional ??= new();

    /// <inheritdoc/>
    public int Pc { get; set; }

    /// <summary>
    /// The procedure this is an activation of, with the module that declares it; <see cref="ICallStackFrame.StaticSymbol"/> has only its name and type.
    /// </summary>
    public VBTypeMemberSymbol? Procedure { get; set; }

    /// <summary>
    /// The body this activation runs: what <see cref="Pc"/> is an offset into. Not replaced by loading the module again, which is for the calls made after it - an
    /// activation goes on with the code it started with.
    /// </summary>
    public InstructionList? Body { get; set; }

    /// <summary>
    /// The offset the program waited before, and is resumed at: a breakpoint there is not waited at again, once.
    /// </summary>
    public int WaitedAt { get; set; } = -1;

    private bool _moved;

    /// <summary>
    /// Moves the program counter to <paramref name="offset"/> of the <see cref="Body"/> while the activation waits at a stop, so that it goes on from there and
    /// not from where it stopped (<c>Set Next Statement</c>).
    /// </summary>
    /// <param name="offset">An offset of the body, or its length for the end of the procedure.</param>
    public void MoveTo(int offset)
    {
        Pc = offset;
        _moved = true;
    }

    /// <summary>
    /// Whether the program counter was moved while the activation waited, which forgets it: the instruction it stopped at is then not the one to go on after.
    /// </summary>
    public bool TakeMove()
    {
        var moved = _moved;
        _moved = false;
        return moved;
    }

    /// <inheritdoc/>
    public VBRuntimeObjectId? Target { get; set; }

    /// <inheritdoc/>
    public ErrorHandlerState ErrorHandler { get; set; } = ErrorHandlerState.Disabled;

    /// <inheritdoc/>
    public VBTypedValue? ReturnValue
    {
        get
        {
            if (_hasResultPayload)
            {
                _returnValue = _resultPayload.Type.CreateValue(new ValueBindingHandle(_resultPayload.Box()));
                _hasResultPayload = false;
            }

            return _returnValue;
        }
        set
        {
            _returnValue = value;
            _hasResultPayload = false;
        }
    }

    private VBTypedValue? _returnValue;

    // the function result variable of a scalar type assigned on payloads (IR lowering S1.3): a value is made of it once, when it is read as one.
    private ScalarPayload _resultPayload;
    private bool _hasResultPayload;

    /// <summary>
    /// Assigns the function result variable a payload of its declared type (IR lowering S1.3), which <see cref="ReturnValue"/> is when it is read.
    /// </summary>
    internal void AssignResult(ScalarPayload payload)
    {
        _resultPayload = payload;
        _hasResultPayload = true;
    }

    /// <summary>
    /// The function result variable as a payload, when it holds one: what it was last assigned on payloads, or a value of a scalar type.
    /// </summary>
    internal bool TryReadResult(out ScalarPayload payload)
    {
        if (_hasResultPayload)
        {
            payload = _resultPayload;
            return true;
        }

        payload = default;
        return _returnValue is { IsIndeterminate: false, Handle: ValueBindingHandle boxed } value && ScalarPayload.TryOf(value.TypeInfo, boxed.Value, out payload);
    }

    private ValueBindingHandle? _resultCell;

    /// <summary>
    /// The cell of the function result variable, which holds the objects the result does while the activation runs
    /// (<see cref="ObjectReferences.AssignResult"/>); made when a result that can hold one is first assigned.
    /// </summary>
    public ValueBindingHandle ResultCell => _resultCell ??= BindingKnowledge.NewCell(VBEmptyValue.Empty.RuntimeValue, isKnown: true);

    /// <summary>
    /// Whether the function result was ever assigned a value that can hold an object: whether there is a <see cref="ResultCell"/> to let go of.
    /// </summary>
    public bool HasResultCell => _resultCell is not null;

    /// <summary>
    /// The cell that holds what the block opened at <paramref name="openerOffset"/> holds for as long as it runs - a <c>For Each</c>'s enumerator,
    /// which ends at <paramref name="end"/>, the offset right past the block; a <c>With</c>'s target, which is held until its <c>End With</c> lets go of it
    /// (<see cref="TakeBlockHolder"/>), wherever the program jumps meanwhile: <paramref name="end"/> is <see langword="null"/>.
    /// </summary>
    public ValueBindingHandle BlockHolder(int openerOffset, int? end)
    {
        var holders = Occasionally.BlockHolders ??= [];
        if (!holders.TryGetValue(openerOffset, out var holder))
        {
            holder = (end, BindingKnowledge.NewCell(VBEmptyValue.Empty.RuntimeValue, isKnown: true));
            holders[openerOffset] = holder;
        }

        return holder.Cell;
    }

    /// <summary>
    /// Takes away the holders of the blocks <paramref name="pc"/> is not in, for what they hold to be let go of: every block, when
    /// <paramref name="pc"/> is <see langword="null"/> - the activation is over.
    /// </summary>
    public IReadOnlyList<ValueBindingHandle> TakeBlockHoldersOutside(int? pc)
    {
        if (_occasional?.BlockHolders is not { Count: > 0 } holders)
        {
            return [];
        }

        List<ValueBindingHandle>? left = null;
        foreach (var (opener, (end, cell)) in holders)
        {
            if (pc is not { } at || (end is { } blockEnd && (at < opener || at >= blockEnd)))
            {
                (left ??= []).Add(cell);
                holders.Remove(opener);
            }
        }

        return left ?? [];
    }

    /// <summary>
    /// Takes away the holder of the block opened at <paramref name="openerOffset"/>, for what it holds to be let go of: a <c>With</c>'s, at its
    /// <c>End With</c>.
    /// </summary>
    /// <returns>The holder; <see langword="null"/> when the block holds nothing.</returns>
    public ValueBindingHandle? TakeBlockHolder(int openerOffset)
        => _occasional?.BlockHolders is { } holders && holders.Remove(openerOffset, out var holder) ? holder.Cell : null;

    /// <summary>
    /// Forgets the hidden state of the block opened at <paramref name="openerOffset"/>: a <c>With</c>'s target, after its <c>End With</c>.
    /// </summary>
    public void ClearBlockState(int openerOffset)
    {
        _ = _occasional?.BlockState?.Remove(openerOffset);
        _ = _occasional?.SelectorPayloads?.Remove(openerOffset);
    }

    /// <summary>
    /// Stashes <paramref name="value"/> as this activation's hidden state for the block-opening
    /// instruction at <paramref name="openerOffset"/> — a <c>With</c>'s target, a <c>Select Case</c>'s
    /// selector.
    /// </summary>
    public void SetBlockState(int openerOffset, VBTypedValue value)
    {
        (Occasionally.BlockState ??= [])[openerOffset] = value;
        _ = _occasional!.SelectorPayloads?.Remove(openerOffset);
    }

    /// <inheritdoc/>
    public bool TryGetBlockState(int openerOffset, [NotNullWhen(true)][MaybeNullWhen(false)] out VBTypedValue? value)
    {
        value = null;
        return _occasional?.BlockState is { } blockState && blockState.TryGetValue(openerOffset, out value);
    }

    /// <summary>
    /// Stashes a <c>Select Case</c>'s selector evaluated on payloads (IR lowering S1.6), in place of the value <see cref="SetBlockState"/> would stash: the
    /// <c>Case</c> headers compare it as it is, and make a value of it only for a clause compared boxed.
    /// </summary>
    internal void SetSelectorPayload(int openerOffset, ScalarPayload selector)
    {
        (Occasionally.SelectorPayloads ??= [])[openerOffset] = selector;
        _ = _occasional!.BlockState?.Remove(openerOffset);
    }

    internal bool TryGetSelectorPayload(int openerOffset, out ScalarPayload selector)
    {
        selector = default;
        return _occasional?.SelectorPayloads is { } selectors && selectors.TryGetValue(openerOffset, out selector);
    }

    /// <summary>
    /// Stashes <paramref name="state"/> as this activation's hidden state for the <c>ForOpener</c>
    /// instruction at <paramref name="openerOffset"/>.
    /// </summary>
    public void SetForLoopState(int openerOffset, ForLoopState state) => (Occasionally.ForLoopState ??= [])[openerOffset] = state;

    /// <inheritdoc/>
    public bool TryGetForLoopState(int openerOffset, out ForLoopState state)
    {
        state = default;
        return _occasional?.ForLoopState is { } loops && loops.TryGetValue(openerOffset, out state);
    }

    /// <summary>
    /// Stashes <paramref name="state"/> as this activation's hidden state for the <c>ForEachOpener</c>
    /// instruction at <paramref name="openerOffset"/>.
    /// </summary>
    public void SetForEachState(int openerOffset, ForEachState state) => (Occasionally.ForEachState ??= [])[openerOffset] = state;

    /// <inheritdoc/>
    public bool TryGetForEachState(int openerOffset, out ForEachState state)
    {
        state = default;
        return _occasional?.ForEachState is { } loops && loops.TryGetValue(openerOffset, out state);
    }

    /// <inheritdoc/>
    public int GoSubDepth => _occasional?.GoSubReturns?.Count ?? 0;

    /// <summary>
    /// Pushes <paramref name="returnOffset"/> onto this activation's own GoSub Resumption List — a
    /// <c>GoSub</c> or a successful <c>On…GoSub</c> branch adds the offset right after itself, for a
    /// later <c>Return</c> to pop back to.
    /// </summary>
    public void PushGoSubReturn(int returnOffset) => (Occasionally.GoSubReturns ??= new()).Push(returnOffset);

    /// <summary>
    /// The offsets this activation returns to, innermost first: the instruction after each <c>GoSub</c> that has not been <c>Return</c>ed from yet (its GoSub Resumption List).
    /// </summary>
    public IReadOnlyCollection<int> GoSubReturns => (IReadOnlyCollection<int>?)_occasional?.GoSubReturns ?? [];

    /// <summary>
    /// Where the <c>GoSub</c> statements this activation has not <c>Return</c>ed from are, innermost first: how it got to where it is. Each is the instruction
    /// before its return offset; <see langword="null"/> for one the body does not place.
    /// </summary>
    public IEnumerable<SourceLocation?> GoSubLocations()
        => Body is not { } body
            ? []
            : GoSubReturns.Select(returnOffset => returnOffset - 1 >= 0 && returnOffset - 1 < body.Items.Length ? body.Items[returnOffset - 1].Node?.SourceLocation : null);

    /// <summary>
    /// Pops this activation's own GoSub Resumption List and returns the offset a <c>Return</c> statement
    /// branches to.
    /// </summary>
    /// <param name="returnOffset">The most recently pushed offset, if the list isn't empty.</param>
    /// <returns><c>false</c> if the list is empty — error 3, "Return without GoSub".</returns>
    public bool TryPopGoSubReturn(out int returnOffset)
    {
        if (_occasional?.GoSubReturns is not { Count: > 0 } returns)
        {
            returnOffset = 0;
            return false;
        }

        returnOffset = returns.Pop();
        return true;
    }

    /// <summary>
    /// Declares <paramref name="symbol"/> on this frame and reserves storage sized for
    /// <paramref name="value"/>, its initial value.
    /// </summary>
    /// <param name="symbol">The locally-scoped <see cref="Symbol"/> being declared — a parameter or a <c>Dim</c> local.</param>
    /// <param name="value">The symbol's initial value: the caller's argument for a parameter, the declared type's default value for a fresh <c>Dim</c>.</param>
    /// <exception cref="InvalidOperationException"><paramref name="symbol"/> is already declared on this frame — a compile-time <c>DuplicateDeclaration</c> that should never reach runtime.</exception>
    public void Push(Symbol symbol, VBTypedValue value) => Push(symbol, value, -1);

    /// <inheritdoc cref="Push(Symbol, VBTypedValue)"/>
    /// <param name="symbol">The locally-scoped <see cref="Symbol"/> being declared — a parameter or a <c>Dim</c> local.</param>
    /// <param name="value">The symbol's initial value: the caller's argument for a parameter, the declared type's default value for a fresh <c>Dim</c>.</param>
    /// <param name="slot">
    /// The number of the symbol's slot in the activation's layout (<see cref="FrameLayout.ParameterSlots"/>, <see cref="FrameLayout.LocalSlots"/>), when the
    /// caller knows it; <c>-1</c> to look it up by the symbol.
    /// </param>
    public void Push(Symbol symbol, VBTypedValue value, int slot)
    {
        // the variable is a cell of the activation, which takes no session memory until something asks for its address (IR lowering S1.1, Q1): most
        // locals are never passed by reference, and an activation of them reserves nothing. One the layout has a slot for is held in it (S1.1b).
        Hold(symbol, CellFor(value), value.Size, slot);
    }

    /// <summary>
    /// The cell a variable that starts as <paramref name="value"/> is held in - a local's, a field's of an object: a variable of a scalar type holds its value
    /// as a payload, which an expression of it is evaluated on (IR lowering S1.2), in a cell made of the value as it is; a Variant that holds a scalar (or
    /// nothing yet) holds it as a payload too (S1.4), which its subtype is the type of; anything else is a fresh copy of the value
    /// (<see cref="SymbolAddressTable.FreshBinding"/>).
    /// </summary>
    internal static IBindingHandle CellFor(VBTypedValue value)
        => UnboxedEvaluation.IsEnabled && ScalarPayload.IsScalar(value.TypeInfo) && value is { IsIndeterminate: false, Handle: ValueBindingHandle boxed }
            && ScalarCell.TryCreate(value.TypeInfo, boxed.Value, out var scalar)
                ? scalar!
                : UnboxedEvaluation.IsEnabled && value is VBVariantValue { IsIndeterminate: false, TypedValue: var held } && IsImmutable(held)
                    && VariantCell.TryCreate(VBVariantType.TypeInfo, new VBRuntimeVariantValue(held.TypeInfo.VarType(), held), out var variant)
                    ? variant!
                    : SymbolAddressTable.FreshBinding(value);

    // a value a Variant cell can share with what it was made of, which nothing changes in place: a scalar, Empty, Null.
    private static bool IsImmutable(VBTypedValue value) => ScalarPayload.IsScalar(value.TypeInfo) || value is VBEmptyValue or VBNullValue;

    /// <summary>
    /// Declares the parameter <paramref name="symbol"/> on this frame, holding <paramref name="value"/> - the argument a call passes by value, already
    /// let-coerced to <paramref name="type"/>, the parameter's declared type.
    /// </summary>
    /// <remarks>
    /// The same as <see cref="Push"/> with a value of <paramref name="type"/> made of <paramref name="value"/>, which a parameter of a scalar type is not
    /// made of (IR lowering S1.3): its cell holds the argument as it is.
    /// </remarks>
    /// <param name="slot">The number of the parameter's slot, when the caller knows it (<see cref="Push"/>); <c>-1</c> to look it up.</param>
    /// <exception cref="InvalidOperationException"><paramref name="symbol"/> is already declared on this frame.</exception>
    public void PushArgument(Symbol symbol, VBType type, IRuntimeValue value, int slot = -1) => _ = HoldArgument(symbol, type, value, slot);

    /// <summary>
    /// <see cref="PushArgument"/>, which says what cell the parameter was given - <see langword="null"/> when it was given one the way any declaration is
    /// (<see cref="Push(Symbol, VBTypedValue, int)"/>), which is looked up to be read.
    /// </summary>
    internal IBindingHandle? HoldArgument(Symbol symbol, VBType type, IRuntimeValue value, int slot)
    {
        if (UnboxedEvaluation.IsEnabled && ScalarCell.TryCreate(type, value, out var scalar))
        {
            return Hold(symbol, scalar!, type.DefaultValue.Size, slot);
        }

        if (UnboxedEvaluation.IsEnabled && value is VBRuntimeVariantValue { WrappedValue: { IsIndeterminate: false } held } && IsImmutable(held)
            && VariantCell.TryCreate(type, value, out var variant))
        {
            return Hold(symbol, variant!, type.DefaultValue.Size, slot);
        }

        // an object - the Me of a member, an object passed by value - is the cell of its reference, which is what a value of it would be copied into (S1.4c).
        if (UnboxedEvaluation.IsEnabled && type is VBClassType or VBObjectType && value is VBRuntimeValue<VBRuntimeObjectId>)
        {
            return Hold(symbol, new ValueBindingHandle(value), type.DefaultValue.Size, slot);
        }

        Push(symbol, type.CreateValue(new ValueBindingHandle(value)), slot);
        return null;
    }

    /// <summary>
    /// The cell held in slot <paramref name="slot"/> of the activation's layout, when it holds one rather than an address (IR lowering S1.4c): what a
    /// variable that has a slot is, read by number rather than looked up by its symbol.
    /// </summary>
    internal bool TryGetSlotCell(int slot, [NotNullWhen(true)] out IBindingHandle? cell)
    {
        cell = (uint)slot < (uint)_slots.Length && _slots[slot] is { IsFilled: true, Cell: { } held } ? held : null;
        return cell is not null;
    }

    /// <summary>
    /// Declares the parameter <paramref name="symbol"/>, a scalar passed by value, holding <paramref name="payload"/>: its argument, of its type (IR
    /// lowering S1.3).
    /// </summary>
    /// <param name="symbol">The parameter.</param>
    /// <param name="payload">The argument, let-coerced to the parameter's type.</param>
    /// <param name="slot">The number of the parameter's slot, when the caller knows it (<see cref="Push(Symbol, VBTypedValue, int)"/>); <c>-1</c> to look it up.</param>
    internal void PushPayload(Symbol symbol, ScalarPayload payload, int slot)
    {
        // a Variant parameter holds the scalar it is passed as it is, of its own type (IR lowering S1.7).
        if (symbol is ITypedSymbol { ResolvedType: VBVariantType })
        {
            Hold(symbol, VariantCell.Of(payload), VBVariantType.TypeInfo.DefaultValue.Size, slot);
            return;
        }

        Hold(symbol, ScalarCell.Of(payload), payload.Type.DefaultValue.Size, slot);
    }

    /// <summary>
    /// Declares the <c>ByRef</c> parameter <paramref name="symbol"/> as a second name for a variable of the caller held in <paramref name="cell"/>: the
    /// parameter's slot holds that very cell (IR lowering S1.3, <see cref="CellReference"/>).
    /// </summary>
    /// <param name="symbol">The parameter.</param>
    /// <param name="cell">The cell of the caller's variable.</param>
    /// <param name="slot">The number of the parameter's slot, when the caller knows it; <c>-1</c> to look it up.</param>
    internal void PushAlias(Symbol symbol, IBindingHandle cell, int slot)
        => Hold(symbol, cell, cell is ScalarCell scalar ? scalar.Type.DefaultValue.Size : 0, slot);

    /// <summary>The layout the activation was laid out as (<see cref="LayOut"/>), if it was.</summary>
    internal FrameLayout? Layout => _layout;

    // a slot the caller numbered is the one of this activation's layout, which is what the caller read the number from: no symbol is looked up (S1.3).
    private IBindingHandle Hold(Symbol symbol, IBindingHandle cell, int size, int slot)
    {
        if ((uint)slot < (uint)_slots.Length || TryGetOwnSlot(symbol, out slot))
        {
            var index = slot;
            ref var held = ref _slots[index];
            if (held.IsFilled || IsByRefParameter(symbol))
            {
                throw new InvalidOperationException($"'{symbol.Uri}' is already declared on this frame.");
            }

            held = new Slot(true, default, cell, null, size);
            return cell;
        }

        if (!(_declared ??= []).Add(symbol.SemanticId))
        {
            throw new InvalidOperationException($"'{symbol.Uri}' is already declared on this frame.");
        }

        (_cells ??= [])[symbol.SemanticId] = (cell, size);
        return cell;
    }

    /// <summary>
    /// Lays the activation out as <paramref name="layout"/> says (IR lowering S1.1b): a variable it has a slot for is held in that slot from the moment it
    /// is declared, and is found by its number.
    /// </summary>
    /// <param name="layout">The layout of the procedure this is an activation of (<see cref="FrameLayout.For"/>).</param>
    public void LayOut(FrameLayout layout)
    {
        _layout = layout;
        _slots = layout.Count == 0 ? [] : new Slot[layout.Count];
    }

    // the slot of the activation's layout a variable of it is held in.
    private bool TryGetOwnSlot(Symbol symbol, out int index)
    {
        index = -1;
        if (_layout is null || _slots.Length == 0 || !_layout.TryGetSlot(symbol, out var slot))
        {
            return false;
        }

        index = slot.Index;
        return true;
    }

    // the cells of the activation's variables that no slot holds and that have no address yet (TryGetAddress gives one, and the cell moves to the address
    // table).
    private Dictionary<SemanticId, (IBindingHandle Cell, int Size)>? _cells;

    private bool TryGetCell(Symbol symbol, [NotNullWhen(true)] out IBindingHandle? cell)
    {
        cell = null;
        if (TryGetOwnSlot(symbol, out var index) && _slots[index].Cell is { } slotCell)
        {
            cell = slotCell;
            return true;
        }

        if (_cells is not null && _cells.TryGetValue(symbol.SemanticId, out var held))
        {
            cell = held.Cell;
            return true;
        }

        return false;
    }

    // the address of a variable that has none yet: the cell is bound at it, and is reached through it from now on - a POKE rebinds what is at an address,
    // which a slot that held the cell would not see, so the slot holds the address instead.
    // NOTE: TryAllocate failing here (storage exhausted) needs to surface as a coded VBRuntimeErrorId.OutOfMemory runtime error once this has a caller with
    // a source location to attach it to - matches RuntimeSymbolResolver.TryAllocate's own documented contract.
    private bool TryGiveAddress(Symbol symbol, out MemoryAddress address)
    {
        address = default;
        if (TryGetOwnSlot(symbol, out var index) && _slots[index] is { Cell: { } slotCell } held)
        {
            if (!(_addresses ??= new(Storage)).TryAllocate(symbol, held.Size, slotCell, out address))
            {
                return false;
            }

            _slots[index] = held with { Address = address, Cell = null };
            _cells?.Remove(symbol.SemanticId);
            return true;
        }

        if (_cells is null || !_cells.TryGetValue(symbol.SemanticId, out var cell) || !(_addresses ??= new(Storage)).TryAllocate(symbol, cell.Size, cell.Cell, out address))
        {
            return false;
        }

        _cells.Remove(symbol.SemanticId);
        return true;
    }

    /// <summary>
    /// Declares <paramref name="symbol"/> on this frame as <see cref="Push"/> does, for a local the procedure's code does not declare: one a statement typed where the
    /// program waits declared implicitly (<strong>MS-VBAL §5.6.10</strong>). It is a local of this activation from then on, and is among
    /// <see cref="DeclaredAtAStop"/>, since the procedure's symbol does not list it.
    /// </summary>
    /// <param name="symbol">The local.</param>
    /// <param name="value">Its initial value: <c>Empty</c>, for the <c>Variant</c> an implicit declaration is.</param>
    public void PushDeclaredAtAStop(Symbol symbol, VBTypedValue value)
    {
        Push(symbol, value);
        (Occasionally.DeclaredAtAStop ??= []).Add(symbol);
    }

    /// <summary>
    /// The locals of this activation that statements typed where the program waited declared (<see cref="PushDeclaredAtAStop"/>), in the order they were.
    /// </summary>
    public IReadOnlyList<Symbol> DeclaredAtAStop => (IReadOnlyList<Symbol>?)_occasional?.DeclaredAtAStop ?? [];

    /// <summary>
    /// Declares <paramref name="symbol"/> on this frame as a <c>ByRef</c> reference parameter binding
    /// (<strong>MS-VBAL §5.3.1.11</strong>): <paramref name="address"/> is an existing address — the
    /// caller's own argument variable, not storage this frame allocates or ever frees — so every
    /// subsequent read or write through <paramref name="symbol"/> goes straight to it. This is real
    /// aliasing, the same variable under a second local name, not a copy: a write inside this activation
    /// is visible to the caller the instant it happens, with no copy-back step needed.
    /// </summary>
    /// <param name="symbol">The parameter.</param>
    /// <param name="address">The address of the variable it is a second name for.</param>
    /// <param name="referencedType">
    /// The declared type of that variable when the parameter does not have it - a <c>Variant</c> parameter given a variable of another type
    /// (<see cref="TryGetReferencedType"/>); <see langword="null"/> when it does.
    /// </param>
    /// <exception cref="InvalidOperationException"><paramref name="symbol"/> is already declared on this frame.</exception>
    public void PushByRef(Symbol symbol, MemoryAddress address, VBType? referencedType = null)
    {
        if (!(_declared ??= []).Add(symbol.SemanticId))
        {
            throw new InvalidOperationException($"'{symbol.Uri}' is already declared on this frame.");
        }

        (Occasionally.ByRefAliases ??= [])[symbol.SemanticId] = address;
        if (referencedType is not null)
        {
            (_occasional!.ReferencedTypes ??= [])[symbol.SemanticId] = referencedType;
        }
    }

    /// <inheritdoc/>
    public bool TryGetReferencedType(Symbol symbol, [NotNullWhen(true)] out VBType? declaredType)
    {
        declaredType = null;
        return _occasional?.ReferencedTypes is { } referencedTypes && referencedTypes.TryGetValue(symbol.SemanticId, out declaredType);
    }

    // the address a ByRef parameter is an alias of, if the symbol is one.
    private bool TryGetAlias(Symbol symbol, out MemoryAddress address)
    {
        address = default;
        return _occasional?.ByRefAliases is { } aliases && aliases.TryGetValue(symbol.SemanticId, out address);
    }

    /// <inheritdoc/>
    public IBindingHandle GetValue(Symbol symbol)
        => TryResolve(symbol, out var value) ? value : throw new KeyNotFoundException($"No runtime binding exists yet for '{symbol.Uri}'.");

    /// <inheritdoc/>
    public bool TryResolve(Symbol symbol, [NotNullWhen(true)][MaybeNullWhen(false)] out IBindingHandle? value)
        => TryGetAlias(symbol, out var aliased)
            ? Storage.TryRead(aliased, out value)
            : TryGetCell(symbol, out value) || _addresses is not null && _addresses.TryRead(symbol, out value);

    /// <inheritdoc/>
    public bool TryGetAddress(Symbol symbol, out MemoryAddress address)
        => TryGetAlias(symbol, out address) || _addresses is not null && _addresses.TryGetAddress(symbol, out address) || TryGiveAddress(symbol, out address);

    // the slots of the activation (IR lowering D2), filled as they are read: each holds the cell of a variable that has no address, or the address of one
    // that has - this activation's own, or the one a ByRef parameter is an alias of - and the type a Variant reference parameter reads its variable as. A
    // variable's address does not change while the activation runs (Push and PushByRef declare each variable once); the binding at the address does (a POKE
    // rebinds it), which is why it is read there every time rather than kept.
    private readonly record struct Slot(bool IsFilled, MemoryAddress Address, IBindingHandle? Cell, VBType? ReferencedType, int Size);

    private FrameLayout? _layout;
    private Slot[] _slots = [];

    /// <summary>
    /// Reads the binding of a variable through its slot (<see cref="FrameSlot"/>), filling the slot the first time it is read.
    /// </summary>
    /// <param name="slot">The slot the variable was bound to.</param>
    /// <param name="variable">The variable.</param>
    /// <param name="handle">Its binding.</param>
    /// <param name="referencedType">The declared type of the variable a Variant reference parameter is bound to, if the variable is such a parameter.</param>
    /// <returns>
    /// <see langword="false"/> when the slot is not this activation's to read - its layout is not the one of the definition of the procedure this is an
    /// activation of, or the variable is not declared yet - and the variable is to be read the way it always is.
    /// </returns>
    /// <remarks>
    /// An activation is of one definition of one procedure, which is what the layout of the first slot read in it is: a slot of any other layout is not
    /// read here.
    /// </remarks>
    public bool TryReadSlot(FrameSlot slot, Symbol variable, [NotNullWhen(true)] out IBindingHandle? handle, out VBType? referencedType)
    {
        handle = null;
        referencedType = null;

        _layout ??= slot.Layout;
        if (!ReferenceEquals(_layout, slot.Layout))
        {
            return false;
        }

        if (_slots.Length == 0)
        {
            _slots = new Slot[slot.Layout.Count];
        }

        ref var held = ref _slots[slot.Index];
        if (!held.IsFilled)
        {
            // a cell is read as it is, without giving the variable an address it does not need.
            var referenced = TryGetReferencedType(variable, out var type) ? type : null;
            if (!TryGetAlias(variable, out var address) && _cells is not null && _cells.TryGetValue(variable.SemanticId, out var cell))
            {
                held = new Slot(true, default, cell.Cell, referenced, cell.Size);
            }
            else if (TryGetAddress(variable, out address))
            {
                held = new Slot(true, address, null, referenced, 0);
            }
            else
            {
                return false;
            }
        }

        referencedType = held.ReferencedType;
        if (held.Cell is { } heldCell)
        {
            handle = heldCell;
            return true;
        }

        return Storage.TryRead(held.Address, out handle);
    }

    /// <inheritdoc/>
    public bool IsByRefParameter(Symbol symbol) => _occasional?.ByRefAliases is { } aliases && aliases.ContainsKey(symbol.SemanticId);

    /// <inheritdoc/>
    public bool LocksArray(VBArrayValue array)
    {
        if (array.Length == 0)
        {
            return false;
        }

        // an element passed by reference: the parameter's address is bound to the element's own cell.
        foreach (var address in _occasional?.ByRefAliases?.Values ?? Enumerable.Empty<MemoryAddress>())
        {
            if (Storage.TryRead(address, out var handle) && IsCellOf(array, handle))
            {
                return true;
            }
        }

        // a For Each that is running holds what it enumerates (ProcedureExecutor.HoldForBlock): an array that shares its cells is the one.
        foreach (var (_, (_, holder)) in _occasional?.BlockHolders ?? [])
        {
            if (holder.Value is VBRuntimeValue<VBRuntimeArrayValue> { StoredValue.Array: { Length: > 0 } enumerated }
                && ReferenceEquals(enumerated.CellAt(0), array.CellAt(0)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCellOf(VBArrayValue array, IBindingHandle handle)
    {
        for (var index = 0; index < array.Length; index++)
        {
            if (ReferenceEquals(array.CellAt(index), handle))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Frees every local this frame allocated. Called when the frame is popped off the
    /// <see cref="ICallStack"/> that owns it — a frame is never partially torn down.
    /// </summary>
    /// <remarks>
    /// A <c>ByRef</c> alias (<see cref="PushByRef"/>) is never freed here: its address belongs to
    /// whichever frame originally allocated it (the caller's own), and this frame only ever borrowed it.
    /// </remarks>
    public void ReleaseAll()
    {
        _addresses?.ReleaseAll();
        _cells = null;
    }
}
