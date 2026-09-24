using RDCore.Runtime.Execution;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
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
    private readonly Dictionary<int, VBTypedValue> _blockState = [];
    private readonly Dictionary<int, ForLoopState> _forLoopState = [];

    /// <inheritdoc/>
    public int Pc { get; set; }

    /// <summary>
    /// Stashes <paramref name="value"/> as this activation's hidden state for the block-opening
    /// instruction at <paramref name="openerOffset"/> — a <c>With</c>'s target, a <c>Select Case</c>'s
    /// selector.
    /// </summary>
    public void SetBlockState(int openerOffset, VBTypedValue value) => _blockState[openerOffset] = value;

    /// <inheritdoc/>
    public bool TryGetBlockState(int openerOffset, [NotNullWhen(true)][MaybeNullWhen(false)] out VBTypedValue? value)
        => _blockState.TryGetValue(openerOffset, out value);

    /// <summary>
    /// Stashes <paramref name="state"/> as this activation's hidden state for the <c>ForOpener</c>
    /// instruction at <paramref name="openerOffset"/>.
    /// </summary>
    public void SetForLoopState(int openerOffset, ForLoopState state) => _forLoopState[openerOffset] = state;

    /// <inheritdoc/>
    public bool TryGetForLoopState(int openerOffset, out ForLoopState state)
        => _forLoopState.TryGetValue(openerOffset, out state);

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

    /// <inheritdoc/>
    public IBindingHandle GetValue(Symbol symbol) => _addresses.GetValue(symbol);

    /// <inheritdoc/>
    public bool TryResolve(Symbol symbol, [NotNullWhen(true)][MaybeNullWhen(false)] out IBindingHandle? value)
        => _addresses.TryRead(symbol, out value);

    /// <summary>
    /// Frees every local this frame allocated. Called when the frame is popped off the
    /// <see cref="ICallStack"/> that owns it — a frame is never partially torn down.
    /// </summary>
    public void ReleaseAll() => _addresses.ReleaseAll();
}
