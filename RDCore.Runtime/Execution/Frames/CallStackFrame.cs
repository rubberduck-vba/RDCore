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
        public Dictionary<SemanticId, MemoryAddress>? ReferenceVariants;
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
    /// activation goes on with the code it started with, unless it is moved into the edited body while the program waits (<see cref="TryRebase"/>).
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

    /// <summary>
    /// Whether the activation waits after the instruction at <see cref="Pc"/>, which has run - a <c>Stop</c>, a failed <c>Debug.Assert</c> - and goes on
    /// from the one after it; and not before it, as at a breakpoint or a step.
    /// </summary>
    public bool WaitsAfterPc { get; internal set; }

    /// <summary>
    /// Whether the activation waits at an error the instruction at <see cref="Pc"/> raised, under a debugger that asked to see errors: when it goes on, the
    /// error is caught by its handler, or ends the program, from that instruction.
    /// </summary>
    public bool WaitsAtAnError { get; internal set; }

    /// <summary>
    /// Whether the error the activation waits at (<see cref="WaitsAtAnError"/>) has a handler on the call stack to catch it when the program goes on. One
    /// that has none is not given to the program: it goes on by running the statement that raised it again, as it now is (MS-VBA, measured in Excel).
    /// </summary>
    public bool WaitsAtAHandledError { get; internal set; }

    /// <summary>
    /// Whether the activation runs compiled code: it keeps its variables, and the state of its loops, in compiled code's own locals, and its frame has them
    /// only when it is handed to the interpreter - or, under a debugger, as they were when it made the call it is in the middle of. <see cref="Pc"/> is then
    /// the call's.
    /// </summary>
    public bool RunsCompiled { get; internal set; }

    /// <summary>
    /// Why the activation can't go on in <paramref name="body"/>, the edited body of its procedure, from where it is in <see cref="Body"/>; none when it can.
    /// </summary>
    /// <param name="body">The procedure's body, as it was edited.</param>
    /// <param name="alignment">The instructions of <see cref="Body"/> that <paramref name="body"/> has as they were.</param>
    /// <param name="waits">
    /// Whether this is the activation the program waits in. Any other is in the middle of the call the instruction at its <see cref="Pc"/> makes.
    /// </param>
    public FrameRebaseRefusal? WhyNotRebased(InstructionList body, InstructionAlignment alignment, bool waits)
        => Rebased(body, alignment, waits, out _);

    /// <summary>
    /// Goes on in <paramref name="body"/>, the edited body of its procedure, from where the activation is in <see cref="Body"/>: its program counter, and
    /// what it holds of the blocks, the loops, the <c>GoSub</c>s and the error handler it is in, are moved to where they now are.
    /// </summary>
    /// <param name="body">The procedure's body, as it was edited.</param>
    /// <param name="alignment">The instructions of <see cref="Body"/> that <paramref name="body"/> has as they were.</param>
    /// <param name="waits">Whether this is the activation the program waits in.</param>
    /// <returns>Whether it goes on in <paramref name="body"/>; when not, nothing of it was moved (<see cref="WhyNotRebased"/>).</returns>
    /// <remarks>
    /// Its variables are where they were: the edited body names them by the same identities, in the same slots, as long as the procedure's locals are
    /// the ones it had.
    /// </remarks>
    public bool TryRebase(InstructionList body, InstructionAlignment alignment, bool waits)
    {
        if (Rebased(body, alignment, waits, out var rebased) is not null)
        {
            return false;
        }

        // an activation whose body, edited, has every instruction it had at the offset it had - only its lines moved, or none - is given it, and nothing of
        // it moves: what it keeps by offset is where it was.
        if (rebased.Stays)
        {
            Body = body;
            return true;
        }

        (Body, Pc, WaitedAt, ErrorHandler) = (body, rebased.Pc, rebased.WaitedAt, rebased.ErrorHandler);
        _moved = rebased.Moved;
        if (_occasional is { } occasional)
        {
            occasional.BlockState = Rekeyed(occasional.BlockState, rebased.Offsets);
            occasional.SelectorPayloads = Rekeyed(occasional.SelectorPayloads, rebased.Offsets);
            occasional.ForLoopState = Rekeyed(occasional.ForLoopState, rebased.Offsets);
            occasional.ForEachState = Rekeyed(occasional.ForEachState, rebased.Offsets);
            // a With's target is held until its End With, and has no end; any other block's is held until the program is past it.
            occasional.BlockHolders = occasional.BlockHolders?.ToDictionary(holder => rebased.Offsets[holder.Key],
                holder => (holder.Value.End is null ? null : body.Items[rebased.Offsets[holder.Key]].End, holder.Value.Cell));
            if (occasional.GoSubReturns is { } returns)
            {
                // a stack enumerates from its top, and is pushed from its bottom.
                occasional.GoSubReturns = new Stack<int>(returns.Select(offset => alignment[offset - 1]!.Value + 1).Reverse());
            }
        }

        return true;
    }

    // what a block the activation has left behind kept, and that can't be moved, is let go of.
    private static Dictionary<int, T>? Rekeyed<T>(Dictionary<int, T>? state, IReadOnlyDictionary<int, int> offsets)
        => state?.Where(entry => offsets.ContainsKey(entry.Key)).ToDictionary(entry => offsets[entry.Key], entry => entry.Value);

    private sealed record class Rebasing(int Pc, bool Moved, int WaitedAt, ErrorHandlerState ErrorHandler, IReadOnlyDictionary<int, int> Offsets)
    {
        // nothing of the activation is moved: its body, edited, is the one it runs.
        public bool Stays { get; init; }
    }

    private FrameRebaseRefusal? Rebased(InstructionList body, InstructionAlignment alignment, bool waits, [NotNullWhen(false)] out Rebasing? rebased)
    {
        rebased = null;
        if (Body is not { } before)
        {
            return FrameRebaseRefusal.CallChanged;
        }

        // an activation running compiled code has its variables and its loops in compiled code's locals, which nothing here can move: it goes on in its body
        // when that is as it was - its module loaded again with no edit of it - and is not moved otherwise.
        if (RunsCompiled)
        {
            if (!alignment.IsIdentity)
            {
                return FrameRebaseRefusal.RunsCompiled;
            }

            rebased = new Rebasing(Pc, false, WaitedAt, ErrorHandler, new Dictionary<int, int>()) { Stays = true };
            return null;
        }

        // the activation the program waits in goes on from where it was moved to, or from after the instruction it stopped at, or from the one it waits
        // before - which is the statement that raised an error no handler catches, run again as it now is. One that waits at an error a handler catches is
        // given the error when it goes on, at the statement that raised it, which must be as it was. Any other goes on from the call it is in the middle of,
        // which must be as it was - and a call made by a statement, not by a condition or the header of a loop, which go on by their own offsets. Each is
        // moved only to where every longest common subsequence of the edit places it.
        int pc;
        var moved = false;
        if (waits && WaitsAtAnError && (_moved || !WaitsAtAHandledError))
        {
            if (alignment.Before(Pc) is not { } next)
            {
                return FrameRebaseRefusal.Uncertain;
            }

            (pc, moved) = (next, _moved);
        }
        else if (waits && WaitsAtAnError)
        {
            if (!alignment.IsCertain(Pc))
            {
                return FrameRebaseRefusal.Uncertain;
            }

            if (alignment[Pc] is not { } faulted)
            {
                return FrameRebaseRefusal.FaultChanged;
            }

            pc = faulted;
        }
        else if (waits)
        {
            if ((_moved || !WaitsAfterPc ? alignment.Before(Pc) : alignment.After(Pc)) is not { } next)
            {
                return FrameRebaseRefusal.Uncertain;
            }

            (pc, moved) = (next, true);
        }
        else if (!alignment.IsCertain(Pc))
        {
            return FrameRebaseRefusal.Uncertain;
        }
        else if (alignment[Pc] is not { } call)
        {
            return FrameRebaseRefusal.CallChanged;
        }
        else if (before.Items[Pc].Kind is not InstructionKind.Simple)
        {
            return FrameRebaseRefusal.InTheMiddleOfAHeader;
        }
        else
        {
            pc = call;
        }

        var handler = ErrorHandler;

        // an error is being handled when an On Error GoTo caught it: its Resume goes back to the statement that raised it. One On Error Resume Next went on
        // past is gone by.
        var handling = handler is { Mode: ErrorHandlingMode.Disabled, ActiveError: not null, HandlerEntry: not null };

        // where the activation goes on from, and may go back to: where it is, where each GoSub it is in returns to, and the statement a Resume goes back to.
        var returns = _occasional?.GoSubReturns?.ToList() ?? [];
        List<int> positions = [Pc, .. returns, .. handling && handler.FaultStatementOffset is { } resumed ? [resumed] : Array.Empty<int>()];

        // what is kept by the offset of the block it is of - a With's target, a Select's value, a loop's state, what a block holds - is of a block whose
        // opening is as it was, while the activation is in it. What a block it has left behind keeps is of no use to it, and goes when it can't be moved.
        var offsets = new Dictionary<int, int>();
        if (_occasional is { } occasional)
        {
            bool IsIn(int opener) => before.Items[opener].End is not { } end || positions.Any(position => position >= opener && position < end);
            IEnumerable<int> openers = [.. occasional.BlockState?.Keys ?? Enumerable.Empty<int>(), .. occasional.SelectorPayloads?.Keys ?? Enumerable.Empty<int>(),
                .. occasional.ForLoopState?.Keys ?? Enumerable.Empty<int>(), .. occasional.ForEachState?.Keys ?? Enumerable.Empty<int>()];
            foreach (var opener in openers.Concat(occasional.BlockHolders?.Keys ?? Enumerable.Empty<int>()).Distinct())
            {
                // what a block holds is let go of only once the activation is past the block, so it moves with the block, or the edit is refused.
                var holds = occasional.BlockHolders?.ContainsKey(opener) ?? false;
                if (!holds && !IsIn(opener))
                {
                    if (alignment[opener] is { } left)
                    {
                        offsets[opener] = left;
                    }

                    continue;
                }

                if (!alignment.IsCertain(opener))
                {
                    return FrameRebaseRefusal.Uncertain;
                }

                if (alignment[opener] is not { } kept)
                {
                    return FrameRebaseRefusal.BlockChanged;
                }

                offsets[opener] = kept;
            }

            // a GoSub returns to the instruction after it, which is after the GoSub as it now is.
            foreach (var offset in returns)
            {
                if (!alignment.IsCertain(offset - 1))
                {
                    return FrameRebaseRefusal.Uncertain;
                }

                if (alignment[offset - 1] is null)
                {
                    return FrameRebaseRefusal.BlockChanged;
                }
            }
        }

        // the handler of an On Error GoTo is its label, as it was named or where it was; where an error being handled was raised is where its Resume goes
        // back to.
        int? Handler(int? offset)
            => offset is not { } target ? null
                : before.LabelAt(target) is { } label && body.TryGetLabelOffset(label, out var named) ? named
                : alignment[target];
        if ((handler.HandlerTarget is not null && Handler(handler.HandlerTarget) is null) || (handler.HandlerEntry is not null && Handler(handler.HandlerEntry) is null))
        {
            return FrameRebaseRefusal.HandlerChanged;
        }

        if (handling && handler.FaultStatementOffset is { } fault)
        {
            if (!alignment.IsCertain(fault))
            {
                return FrameRebaseRefusal.Uncertain;
            }

            if (alignment[fault] is null)
            {
                return FrameRebaseRefusal.HandlerChanged;
            }
        }

        handler = handler with
        {
            HandlerTarget = Handler(handler.HandlerTarget),
            HandlerEntry = Handler(handler.HandlerEntry),
            FaultStatementOffset = handler.FaultStatementOffset is { } raisedAt ? alignment[raisedAt] : null,
        };

        var waitedAt = WaitedAt >= 0 && alignment[WaitedAt] is { } waited ? waited : -1;
        rebased = new Rebasing(pc, moved, waitedAt, handler, offsets);
        return null;
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

            // a result variable at an address is what is bound there, which a POKE writes.
            if (_resultAddress is { } at && _returnValue is { } declared && Storage.TryRead(at, out var bound))
            {
                return declared.TypeInfo.CreateValue(bound);
            }

            return _returnValue;
        }
        set
        {
            _returnValue = value;
            _hasResultPayload = false;
            if (_resultAddress is { } at && value is not null)
            {
                Storage.TryRebind(at, SymbolAddressTable.FreshBinding(value));
            }
        }
    }

    private VBTypedValue? _returnValue;

    // where the function result variable is, once its address was asked for (TryGetResultAddress).
    private MemoryAddress? _resultAddress;

    /// <summary>
    /// The address of the function result variable (<strong>MS-VBAL §5.3.1</strong>), given the first time it is asked for: what <c>VarPtr</c> of the
    /// function's own name is in its code, one address for the whole activation and none of its locals' (<strong>MS-VBA</strong>, recorded in Excel, oracle\s24
    /// F01-F03). The variable is at the address from then on: what is assigned to it is written there, and what is there is what it returns.
    /// </summary>
    /// <param name="size">How many bytes a value of the function's type occupies.</param>
    /// <param name="address">The address.</param>
    /// <returns><see langword="false"/> when there is no room for it.</returns>
    internal bool TryGetResultAddress(int size, out MemoryAddress address)
    {
        if (_resultAddress is { } known)
        {
            address = known;
            return true;
        }

        if (!Storage.TryAllocate(size, SymbolAddressTable.FreshBinding(ReturnValue ?? VBEmptyValue.Empty), out address))
        {
            return false;
        }

        _resultAddress = address;
        return true;
    }

    // the function result variable of a scalar type assigned on payloads (IR lowering S1.3): a value is made of it once, when it is read as one.
    private ScalarPayload _resultPayload;
    private bool _hasResultPayload;

    /// <summary>
    /// Assigns the function result variable a payload of its declared type (IR lowering S1.3), which <see cref="ReturnValue"/> is when it is read.
    /// </summary>
    internal void AssignResult(ScalarPayload payload)
    {
        // a result variable at an address is written there.
        if (_resultAddress is not null)
        {
            ReturnValue = payload.Type.CreateValue(new ValueBindingHandle(payload.Box()));
            return;
        }

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
        return (_resultAddress is null ? _returnValue : ReturnValue) is { IsIndeterminate: false, Handle: ValueBindingHandle boxed } value
            && ScalarPayload.TryOf(value.TypeInfo, boxed.Value, out payload);
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
        => Hold(symbol, cell, cell is ScalarCell scalar ? scalar.Type.DefaultValue.Size : 0, slot, isAlias: true);

    /// <summary>The layout the activation was laid out as (<see cref="LayOut"/>), if it was.</summary>
    internal FrameLayout? Layout => _layout;

    // a slot the caller numbered is the one of this activation's layout, which is what the caller read the number from: no symbol is looked up (S1.3).
    // isAlias: the cell is a variable of the caller's (PushAlias), which this activation has a second name for and does not free.
    private IBindingHandle Hold(Symbol symbol, IBindingHandle cell, int size, int slot, bool isAlias = false)
    {
        if ((uint)slot < (uint)_slots.Length || TryGetOwnSlot(symbol, out slot))
        {
            var index = slot;
            ref var held = ref _slots[index];
            if (held.IsFilled || IsByRefParameter(symbol))
            {
                throw new InvalidOperationException($"'{symbol.Uri}' is already declared on this frame.");
            }

            held = new Slot(true, default, cell, null, size, isAlias);
            return cell;
        }

        if (!(_declared ??= []).Add(symbol.SemanticId))
        {
            throw new InvalidOperationException($"'{symbol.Uri}' is already declared on this frame.");
        }

        (_cells ??= [])[symbol.SemanticId] = (cell, size, isAlias);
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
    private Dictionary<SemanticId, (IBindingHandle Cell, int Size, bool IsAlias)>? _cells;

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
            if (!TryGiveAddress(symbol, slotCell, held.Size, held.IsAlias, out address))
            {
                return false;
            }

            _slots[index] = held with { Address = address, Cell = null };
            _cells?.Remove(symbol.SemanticId);
            return true;
        }

        if (_cells is null || !_cells.TryGetValue(symbol.SemanticId, out var cell) || !TryGiveAddress(symbol, cell.Cell, cell.Size, cell.IsAlias, out address))
        {
            return false;
        }

        _cells.Remove(symbol.SemanticId);
        return true;
    }

    // a variable has one address, whichever of its names is asked for it first (MS-VBA: VarPtr of a ByRef parameter is the caller's variable's): the one
    // its cell is bound at, if it is, and a new one otherwise. The activation that declared the variable frees it - a parameter that is a second name for
    // a variable of the caller's (PushAlias) is then the reference parameter it is (PushByRef), and the address is the caller's to free, as is one that the
    // caller's variable was given by a parameter of a call it made (ReleaseAll).
    private bool TryGiveAddress(Symbol symbol, IBindingHandle cell, int size, bool isAlias, out MemoryAddress address)
    {
        if (cell is ScalarCell { Address: { } bound })
        {
            address = bound;
            if (isAlias)
            {
                (Occasionally.ByRefAliases ??= [])[symbol.SemanticId] = address;
            }
            else
            {
                (_addresses ??= new(Storage)).Adopt(symbol, address);
            }

            return true;
        }

        if (!isAlias)
        {
            return (_addresses ??= new(Storage)).TryAllocate(symbol, size, cell, out address);
        }

        if (!Storage.TryAllocate(size, cell, out address))
        {
            return false;
        }

        (Occasionally.ByRefAliases ??= [])[symbol.SemanticId] = address;
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

    /// <summary>
    /// The address of the <c>VARIANT</c> a <c>Variant</c> reference parameter given a variable of another type is (<see cref="TryGetReferencedType"/>):
    /// <strong>MS-VBA</strong> passes it a <c>VARIANT</c> of its own that refers to the variable, at one address for as long as the activation runs
    /// (recorded in Excel, oracle\s21 R19, R27). It is what <c>VarPtr</c> of the parameter is; the parameter reads and writes the variable it was given.
    /// </summary>
    /// <param name="symbol">The parameter.</param>
    /// <param name="size">The size of a <c>VARIANT</c> in the session's memory.</param>
    /// <param name="address">The address.</param>
    /// <returns><see langword="false"/> when the parameter is not one bound to a variable of another type.</returns>
    internal bool TryGetReferenceVariantAddress(Symbol symbol, int size, out MemoryAddress address)
    {
        address = default;
        if (!TryGetReferencedType(symbol, out _))
        {
            return false;
        }

        var variants = Occasionally.ReferenceVariants ??= [];
        if (variants.TryGetValue(symbol.SemanticId, out address))
        {
            return true;
        }

        if (!Storage.TryAllocateBytes(size, out address))
        {
            return false;
        }

        variants[symbol.SemanticId] = address;
        return true;
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
    private readonly record struct Slot(bool IsFilled, MemoryAddress Address, IBindingHandle? Cell, VBType? ReferencedType, int Size, bool IsAlias = false);

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
        // the result variable at an address is what the activation returns, which is read before the address is let go of.
        if (_resultAddress is { } result)
        {
            _returnValue = ReturnValue;
            _resultAddress = null;
            Storage.TryDeallocate(result);
        }

        _addresses?.ReleaseAll();
        foreach (var (_, variant) in _occasional?.ReferenceVariants ?? [])
        {
            Storage.TryDeallocate(variant);
        }

        // a variable of this activation that a parameter of a call it made was asked the address of, and that was not asked for it itself since: the
        // address is this activation's (TryGiveAddress).
        foreach (var slot in _slots)
        {
            if (slot is { IsAlias: false, Cell: ScalarCell { Address: { } given } })
            {
                Storage.TryDeallocate(given);
            }
        }

        foreach (var (_, held) in _cells ?? [])
        {
            if (held is { IsAlias: false, Cell: ScalarCell { Address: { } given } })
            {
                Storage.TryDeallocate(given);
            }
        }

        _cells = null;
    }
}
