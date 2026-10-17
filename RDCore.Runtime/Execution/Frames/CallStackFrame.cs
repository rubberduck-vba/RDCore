using RDCore.Runtime.Execution;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;
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
    private readonly SymbolAddressTable _addresses = new(Storage);
    private readonly HashSet<SemanticId> _declared = [];
    // made when the activation first needs one (IR lowering S0.9): most activations have no ByRef alias, no block, no loop and no GoSub, and one that has
    // none does not hash a symbol's identity to find out.
    private Dictionary<SemanticId, MemoryAddress>? _byRefAliases;
    private Dictionary<SemanticId, VBType>? _referencedTypes;
    private Dictionary<int, VBTypedValue>? _blockState;
    private Dictionary<int, ForLoopState>? _forLoopState;
    private Dictionary<int, ForEachState>? _forEachState;
    private Stack<int>? _goSubReturns;

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
    public VBTypedValue? ReturnValue { get; set; }

    private ValueBindingHandle? _resultCell;
    private Dictionary<int, (int End, ValueBindingHandle Cell)>? _blockHolders;

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
    /// The cell that holds what the block opened at <paramref name="openerOffset"/> holds for as long as it runs - a <c>With</c>'s target, a
    /// <c>For Each</c>'s enumerator - which ends at <paramref name="end"/>, the offset right past the block.
    /// </summary>
    public ValueBindingHandle BlockHolder(int openerOffset, int end)
    {
        _blockHolders ??= [];
        if (!_blockHolders.TryGetValue(openerOffset, out var holder))
        {
            holder = (end, BindingKnowledge.NewCell(VBEmptyValue.Empty.RuntimeValue, isKnown: true));
            _blockHolders[openerOffset] = holder;
        }

        return holder.Cell;
    }

    /// <summary>
    /// Takes away the holders of the blocks <paramref name="pc"/> is not in, for what they hold to be let go of: every block, when
    /// <paramref name="pc"/> is <see langword="null"/> - the activation is over.
    /// </summary>
    public IReadOnlyList<ValueBindingHandle> TakeBlockHoldersOutside(int? pc)
    {
        if (_blockHolders is null || _blockHolders.Count == 0)
        {
            return [];
        }

        List<ValueBindingHandle>? left = null;
        foreach (var (opener, (end, cell)) in _blockHolders)
        {
            if (pc is not { } at || at < opener || at >= end)
            {
                (left ??= []).Add(cell);
                _blockHolders.Remove(opener);
            }
        }

        return left ?? [];
    }

    /// <summary>
    /// Stashes <paramref name="value"/> as this activation's hidden state for the block-opening
    /// instruction at <paramref name="openerOffset"/> — a <c>With</c>'s target, a <c>Select Case</c>'s
    /// selector.
    /// </summary>
    public void SetBlockState(int openerOffset, VBTypedValue value) => (_blockState ??= [])[openerOffset] = value;

    /// <inheritdoc/>
    public bool TryGetBlockState(int openerOffset, [NotNullWhen(true)][MaybeNullWhen(false)] out VBTypedValue? value)
    {
        value = null;
        return _blockState is not null && _blockState.TryGetValue(openerOffset, out value);
    }

    /// <summary>
    /// Stashes <paramref name="state"/> as this activation's hidden state for the <c>ForOpener</c>
    /// instruction at <paramref name="openerOffset"/>.
    /// </summary>
    public void SetForLoopState(int openerOffset, ForLoopState state) => (_forLoopState ??= [])[openerOffset] = state;

    /// <inheritdoc/>
    public bool TryGetForLoopState(int openerOffset, out ForLoopState state)
    {
        state = default;
        return _forLoopState is not null && _forLoopState.TryGetValue(openerOffset, out state);
    }

    /// <summary>
    /// Stashes <paramref name="state"/> as this activation's hidden state for the <c>ForEachOpener</c>
    /// instruction at <paramref name="openerOffset"/>.
    /// </summary>
    public void SetForEachState(int openerOffset, ForEachState state) => (_forEachState ??= [])[openerOffset] = state;

    /// <inheritdoc/>
    public bool TryGetForEachState(int openerOffset, out ForEachState state)
    {
        state = default;
        return _forEachState is not null && _forEachState.TryGetValue(openerOffset, out state);
    }

    /// <inheritdoc/>
    public int GoSubDepth => _goSubReturns?.Count ?? 0;

    /// <summary>
    /// Pushes <paramref name="returnOffset"/> onto this activation's own GoSub Resumption List — a
    /// <c>GoSub</c> or a successful <c>On…GoSub</c> branch adds the offset right after itself, for a
    /// later <c>Return</c> to pop back to.
    /// </summary>
    public void PushGoSubReturn(int returnOffset) => (_goSubReturns ??= new()).Push(returnOffset);

    /// <summary>
    /// The offsets this activation returns to, innermost first: the instruction after each <c>GoSub</c> that has not been <c>Return</c>ed from yet (its GoSub Resumption List).
    /// </summary>
    public IReadOnlyCollection<int> GoSubReturns => (IReadOnlyCollection<int>?)_goSubReturns ?? [];

    /// <summary>
    /// Pops this activation's own GoSub Resumption List and returns the offset a <c>Return</c> statement
    /// branches to.
    /// </summary>
    /// <param name="returnOffset">The most recently pushed offset, if the list isn't empty.</param>
    /// <returns><c>false</c> if the list is empty — error 3, "Return without GoSub".</returns>
    public bool TryPopGoSubReturn(out int returnOffset)
    {
        if (_goSubReturns is not { Count: > 0 })
        {
            returnOffset = 0;
            return false;
        }

        returnOffset = _goSubReturns.Pop();
        return true;
    }

    /// <summary>
    /// Declares <paramref name="symbol"/> on this frame and reserves storage sized for
    /// <paramref name="value"/>, its initial value.
    /// </summary>
    /// <param name="symbol">The locally-scoped <see cref="Symbol"/> being declared — a parameter or a <c>Dim</c> local.</param>
    /// <param name="value">The symbol's initial value: the caller's argument for a parameter, the declared type's default value for a fresh <c>Dim</c>.</param>
    /// <exception cref="InvalidOperationException"><paramref name="symbol"/> is already declared on this frame — a compile-time <c>DuplicateDeclaration</c> that should never reach runtime.</exception>
    public void Push(Symbol symbol, VBTypedValue value)
    {
        if (!_declared.Add(symbol.SemanticId))
        {
            throw new InvalidOperationException($"'{symbol.Uri}' is already declared on this frame.");
        }

        // NOTE: TryAllocate failing here (storage exhausted) needs to surface as a coded
        // VBRuntimeErrorId.OutOfMemory runtime error once this has a caller with a source location to
        // attach it to — matches RuntimeSymbolResolver.TryAllocate's own documented contract.
        _ = _addresses.TryAllocate(symbol, value, out _);
    }

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
        if (!_declared.Add(symbol.SemanticId))
        {
            throw new InvalidOperationException($"'{symbol.Uri}' is already declared on this frame.");
        }

        (_byRefAliases ??= [])[symbol.SemanticId] = address;
        if (referencedType is not null)
        {
            (_referencedTypes ??= [])[symbol.SemanticId] = referencedType;
        }
    }

    /// <inheritdoc/>
    public bool TryGetReferencedType(Symbol symbol, [NotNullWhen(true)] out VBType? declaredType)
    {
        declaredType = null;
        return _referencedTypes is not null && _referencedTypes.TryGetValue(symbol.SemanticId, out declaredType);
    }

    // the address a ByRef parameter is an alias of, if the symbol is one.
    private bool TryGetAlias(Symbol symbol, out MemoryAddress address)
    {
        address = default;
        return _byRefAliases is not null && _byRefAliases.TryGetValue(symbol.SemanticId, out address);
    }

    /// <inheritdoc/>
    public IBindingHandle GetValue(Symbol symbol)
        => TryGetAlias(symbol, out var aliased)
            ? Storage.TryRead(aliased, out var handle) ? handle : throw new KeyNotFoundException($"No runtime binding exists yet for '{symbol.Uri}'.")
            : _addresses.GetValue(symbol);

    /// <inheritdoc/>
    public bool TryResolve(Symbol symbol, [NotNullWhen(true)][MaybeNullWhen(false)] out IBindingHandle? value)
        => TryGetAlias(symbol, out var aliased)
            ? Storage.TryRead(aliased, out value)
            : _addresses.TryRead(symbol, out value);

    /// <inheritdoc/>
    public bool TryGetAddress(Symbol symbol, out MemoryAddress address)
        => TryGetAlias(symbol, out address) || _addresses.TryGetAddress(symbol, out address);

    // the slots of the activation (IR lowering D2), filled as they are read: each holds the address of its variable - this activation's own, or the one a
    // ByRef parameter is an alias of - and the type a Variant reference parameter reads its variable as. A variable's address does not change while the
    // activation runs (Push and PushByRef declare each variable once); the binding at the address does (a POKE rebinds it), which is why it is read there
    // every time rather than kept.
    private readonly record struct Slot(bool IsFilled, MemoryAddress Address, VBType? ReferencedType);

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
            if (!TryGetAddress(variable, out var address))
            {
                return false;
            }

            held = new Slot(true, address, TryGetReferencedType(variable, out var referenced) ? referenced : null);
        }

        referencedType = held.ReferencedType;
        return Storage.TryRead(held.Address, out handle);
    }

    /// <inheritdoc/>
    public bool IsByRefParameter(Symbol symbol) => _byRefAliases is not null && _byRefAliases.ContainsKey(symbol.SemanticId);

    /// <inheritdoc/>
    public bool LocksAddress(MemoryAddress address) => _byRefAliases is not null && _byRefAliases.ContainsValue(address);

    /// <summary>
    /// Frees every local this frame allocated. Called when the frame is popped off the
    /// <see cref="ICallStack"/> that owns it — a frame is never partially torn down.
    /// </summary>
    /// <remarks>
    /// A <c>ByRef</c> alias (<see cref="PushByRef"/>) is never freed here: its address belongs to
    /// whichever frame originally allocated it (the caller's own), and this frame only ever borrowed it.
    /// </remarks>
    public void ReleaseAll() => _addresses.ReleaseAll();
}
