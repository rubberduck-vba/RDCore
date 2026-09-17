using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.Runtime.Execution;

/// <summary>
/// A live object instance — the activation record of one <c>New</c>-created object. Every instance
/// field its class module declares is reserved storage through the same <see cref="ISessionStorage"/>
/// the session's module/global symbols and call-stack frames use, exactly like
/// <see cref="Frames.CallStackFrame"/> does for a procedure's locals — this is what gives two
/// instances of the same class module independent bindings for the "same" declared field.
/// </summary>
public sealed class ObjectInstance(VBRuntimeObjectId objectId, VBClassModuleSymbol classModule, ISessionStorage storage) : IObjectInstance
{
    private readonly SymbolAddressTable _addresses = new(storage);
    private readonly HashSet<SemanticId> _declared = [];

    public VBRuntimeObjectId ObjectId => objectId;
    public VBClassModuleSymbol ClassModule => classModule;

    /// <inheritdoc/>
    public void Push(Symbol field, VBTypedValue value)
    {
        if (!_declared.Add(field.SemanticId))
        {
            throw new InvalidOperationException($"'{field.Uri}' is already declared on this instance.");
        }

        _ = _addresses.TryAllocate(field, value, out _);
    }

    /// <inheritdoc/>
    public IBindingHandle GetValue(Symbol field) => _addresses.GetValue(field);

    /// <inheritdoc/>
    public bool TryResolve(Symbol field, [NotNullWhen(true)][MaybeNullWhen(false)] out IBindingHandle? value)
        => _addresses.TryRead(field, out value);

    /// <summary>
    /// Frees every field this instance allocated. Called once the instance's reference count reaches
    /// zero and <see cref="ISessionObjects.TryRemoveObject"/> succeeds — an instance is never partially
    /// torn down.
    /// </summary>
    public void ReleaseAll() => _addresses.ReleaseAll();
}
