using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// Represents a single live object instance — a class module's activation record, owning the storage
/// for every instance field it declares (<strong>RD-VBAL §2.3.1.2</strong>'s instance heap tier). Two
/// instances of the same class module never share a field's storage: each gets its own independent
/// binding, exactly as two activations of the same procedure never share a local's storage on
/// <see cref="ICallStackFrame"/>.
/// </summary>
/// <remarks>
/// ⚠️ An <c>IObjectInstance</c> is <strong>not immutable</strong>: the value retrieved for a given
/// field may be different at a subsequent retrieval. Its lifetime is tracked separately, by
/// <see cref="ISessionObjects"/> and the <see cref="VBRuntimeObjectId"/> it was created for.
/// </remarks>
public interface IObjectInstance
{
    /// <summary>
    /// The identity <see cref="ISessionObjects"/> tracks this instance's lifetime under.
    /// </summary>
    VBRuntimeObjectId ObjectId { get; }

    /// <summary>
    /// The <see cref="VBClassModuleSymbol"/> this is an instance of.
    /// </summary>
    VBClassModuleSymbol ClassModule { get; }

    /// <summary>
    /// Declares <paramref name="field"/> on this instance and reserves storage sized for
    /// <paramref name="value"/>, its initial value — the class's declared type default for a freshly
    /// created instance.
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="field"/> is already declared on this instance.</exception>
    void Push(Symbol field, VBTypedValue value);

    /// <summary>
    /// Gets the <see cref="IBindingHandle"/> currently held on this instance for the specified
    /// instance field <see cref="Symbol"/>.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No binding exists for <paramref name="field"/> on this instance.</exception>
    IBindingHandle GetValue(Symbol field);

    /// <summary>
    /// Gets the <see cref="IBindingHandle"/> currently held on this instance for the specified
    /// instance field <see cref="Symbol"/>, if any.
    /// </summary>
    bool TryResolve(Symbol field, [NotNullWhen(true)][MaybeNullWhen(false)] out IBindingHandle? value);
}
