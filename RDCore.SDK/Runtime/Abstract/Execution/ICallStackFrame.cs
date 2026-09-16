using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
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
}
