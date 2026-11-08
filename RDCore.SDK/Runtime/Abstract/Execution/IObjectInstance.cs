using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Shared;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// Represents a single live object instance: a class module's activation record, owning the storage
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
    /// <paramref name="value"/>, its initial value: the class's declared type default for a freshly
    /// created instance.
    /// </summary>
    /// <param name="field">
    /// A field of the class; or a <c>Static</c> local of one of its members, which the first call of the member on this instance declares: as in
    /// <strong>MS-VBA</strong>, each object has its own, where <strong>MS-VBAL §5.4.3.1</strong> gives a <c>Static</c> local module extent.
    /// </param>
    /// <param name="value">The initial value.</param>
    /// <exception cref="InvalidOperationException"><paramref name="field"/> is already declared on this instance.</exception>
    void Push(Symbol field, VBTypedValue value);

    /// <summary>
    /// The <c>Static</c> locals of the members of the class that this instance holds: the ones of the members called on it so far, which go with it, and
    /// let go of the objects they hold when it does.
    /// </summary>
    IReadOnlyCollection<Symbol> StaticLocals { get; }

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

    /// <summary>
    /// Gets the address this instance reserved for the specified instance field <see cref="Symbol"/>, if any: what a
    /// <c>ByRef</c> argument that names the field is aliased to.
    /// </summary>
    bool TryGetAddress(Symbol field, out MemoryAddress address);
}
