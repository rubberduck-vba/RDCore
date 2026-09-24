using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Runtime.Shared;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// Represents a single <em>call stack</em> frame: the activation record of one procedure call,
/// owning the storage for every locally-scoped <see cref="Symbol"/> it declares — its parameters and
/// its <c>Dim</c>/procedure-local declarations alike (<strong>RD-VBAL §2.3.1.2</strong>, the local
/// <em>stack frame</em> heap tier). A <c>Static</c> local is the one exception: it lives in the
/// module-level statics heap instead, so it keeps its value between calls.
/// </summary>
/// <remarks>
/// ⚠️ A <c>CallStackFrame</c> frame is <strong>not immutable</strong>: the value retrieved for a
/// given symbol may be different at a subsequent retrieval, and a frame is torn down (its storage
/// freed) when it is popped from the <see cref="ICallStack"/> that owns it.
/// </remarks>
public interface ICallStackFrame : IStackFrame
{
    /// <summary>
    /// The <see cref="StaticSymbol"/> identifying the procedure this frame is an activation of.
    /// </summary>
    StaticSymbol StaticSymbol { get; }

    /// <summary>
    /// The <see cref="ModuleDirectives"/> of the module declaring the procedure: what the code of this activation is executed
    /// under — its comparison mode, chiefly (<strong>MS-VBAL §5.2.1.1</strong>).
    /// </summary>
    /// <remarks>
    /// 👉 A <see cref="StaticSymbol"/> identifies a procedure by its name and type alone; it does not say which module it is in.
    /// </remarks>
    ModuleDirectives Directives { get; }

    /// <summary>
    /// The offset, into this activation's own <c>InstructionList</c>, of the next instruction to fetch
    /// (<strong>RD-VBAL §3.5.1</strong>). Read-only here: only the interpreter's executor (RDCore.Runtime)
    /// advances it, through the concrete frame type it constructs.
    /// </summary>
    int Pc { get; }

    /// <summary>
    /// Declares <paramref name="symbol"/> on this frame and reserves storage sized for
    /// <paramref name="value"/>, its initial value — the caller's argument for a parameter, the
    /// declared type's default value for a fresh <c>Dim</c>. MS-VBAL draws no distinction between a
    /// parameter and a <c>Dim</c> local for name-resolution purposes, so both are declared this way.
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="symbol"/> is already declared on this frame — a compile-time <c>DuplicateDeclaration</c> that should never reach runtime.</exception>
    void Push(Symbol symbol, VBTypedValue value);

    /// <summary>
    /// Gets the <see cref="IBindingHandle"/> currently held in this frame for the specified
    /// locally-scoped <see cref="Symbol"/>.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No binding exists yet for <paramref name="symbol"/> on this frame.</exception>
    IBindingHandle GetValue(Symbol symbol);

    /// <summary>
    /// Gets the <see cref="IBindingHandle"/> currently held in this frame for the specified
    /// locally-scoped <see cref="Symbol"/>, if any.
    /// </summary>
    bool TryResolve(Symbol symbol, [NotNullWhen(true)][MaybeNullWhen(false)] out IBindingHandle? value);

    /// <summary>
    /// Gets the hidden value a block-opening instruction (a <c>With</c>'s target, a <c>Select Case</c>'s
    /// selector) stashed on this activation, keyed by that instruction's own offset
    /// (<strong>RD-VBAL §3.5.4</strong>) — however control reached the instruction reading it, a
    /// <c>GoTo</c> included, since the stash lives on the activation rather than on any call stack a
    /// structured walk would otherwise need to unwind.
    /// </summary>
    /// <param name="openerOffset">The offset of the block-opening instruction that stashed the value.</param>
    /// <param name="value">The stashed value, if one exists for <paramref name="openerOffset"/>.</param>
    /// <remarks>Read-only here: only the interpreter's executor (RDCore.Runtime) stashes a value, through the concrete frame type it constructs.</remarks>
    bool TryGetBlockState(int openerOffset, [NotNullWhen(true)][MaybeNullWhen(false)] out VBTypedValue? value);

    /// <summary>
    /// Gets the <see cref="ForLoopState"/> a <c>For</c> loop's own opener stashed on this activation,
    /// keyed by that instruction's own offset — mirrors <see cref="TryGetBlockState"/>, but a <c>For</c>
    /// loop's per-activation hidden state is more than the single value that mechanism holds.
    /// </summary>
    /// <param name="openerOffset">The offset of the <c>ForOpener</c> instruction that stashed the state.</param>
    /// <param name="state">The stashed state, if one exists for <paramref name="openerOffset"/> — its
    /// absence when a <c>ForNext</c> instruction looks it up is <strong>MS-VBAL §5.4.2.3</strong> error
    /// 92, "For loop not initialized" (a <c>GoTo</c> landed directly on the closer this activation).</param>
    /// <remarks>Read-only here: only the interpreter's executor (RDCore.Runtime) stashes a value, through the concrete frame type it constructs.</remarks>
    bool TryGetForLoopState(int openerOffset, out ForLoopState state);

    /// <summary>
    /// Gets the <see cref="ForEachState"/> a <c>For Each</c> loop's own opener stashed on this
    /// activation, keyed by that instruction's own offset — mirrors <see cref="TryGetForLoopState"/> for
    /// the different per-activation shape a <c>For Each</c> loop's own enumeration cursor needs.
    /// </summary>
    /// <param name="openerOffset">The offset of the <c>ForEachOpener</c> instruction that stashed the state.</param>
    /// <param name="state">The stashed state, if one exists for <paramref name="openerOffset"/> — its
    /// absence when a <c>ForEachNext</c> instruction looks it up is <strong>MS-VBAL §5.4.2.4</strong>
    /// error 92, "For loop not initialized" (a <c>GoTo</c> landed directly on the closer this activation).</param>
    /// <remarks>Read-only here: only the interpreter's executor (RDCore.Runtime) stashes a value, through the concrete frame type it constructs.</remarks>
    bool TryGetForEachState(int openerOffset, out ForEachState state);
}
