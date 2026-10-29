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
/// A live object instance — the activation record of one <c>New</c>-created object. Every instance
/// field its class module declares is reserved storage through the same <see cref="ISessionStorage"/>
/// the session's module/global symbols and call-stack frames use, exactly like
/// <see cref="Frames.CallStackFrame"/> does for a procedure's locals — this is what gives two
/// instances of the same class module independent bindings for the "same" declared field.
/// </summary>
/// <remarks>
/// Given the table its class is declared in, the instance is an instance of the class as it is declared now: a class module defined again while the object
/// lives - an edit made while the program waits - gives it the members the class has now.
/// </remarks>
public sealed class ObjectInstance(VBRuntimeObjectId objectId, VBClassModuleSymbol classModule, ISessionStorage storage) : IObjectInstance
{
    private readonly SymbolAddressTable _addresses = new(storage);
    private readonly HashSet<SemanticId> _declared = [];
    private readonly List<Symbol> _staticLocals = [];
    private readonly List<Symbol> _fields = [];
    private readonly SymbolTable? _table;

    internal ObjectInstance(VBRuntimeObjectId objectId, VBClassModuleSymbol classModule, ISessionStorage storage, SymbolTable table)
        : this(objectId, classModule, storage)
    {
        _table = table;
    }

    public VBRuntimeObjectId ObjectId => objectId;

    // a class the table does not declare - the library's, a late-bound server's - is the one the object was created as.
    public VBClassModuleSymbol ClassModule
        => _table is not null && _table.TryGet(classModule, classModule.ScopeKind, out var current) && current is VBClassModuleSymbol declared
            ? declared
            : classModule;

    /// <inheritdoc/>
    public IReadOnlyCollection<Symbol> StaticLocals => _staticLocals;

    // the fields the object was given storage for: those of its class as it was when the object was created, which a field added to the class while the
    // object lives is not one of, and a field removed from it still is.
    internal IReadOnlyCollection<Symbol> Fields => _fields;

    /// <inheritdoc/>
    public void Push(Symbol field, VBTypedValue value)
    {
        if (!_declared.Add(field.SemanticId))
        {
            throw new InvalidOperationException($"'{field.Uri}' is already declared on this instance.");
        }

        if (field is VBLocalVariableSymbol { IsStatic: true })
        {
            _staticLocals.Add(field);
        }
        else
        {
            _fields.Add(field);
        }

        // a field of a scalar type (or a Variant holding one) is held as a payload, as a local is (IR lowering S1.4c, Frames.CallStackFrame.CellFor): what
        // the code of the class evaluates its fields on.
        _ = _addresses.TryAllocate(field, value.Size, Frames.CallStackFrame.CellFor(value), out _);
    }

    /// <inheritdoc/>
    public IBindingHandle GetValue(Symbol field) => _addresses.GetValue(field);

    /// <inheritdoc/>
    public bool TryResolve(Symbol field, [NotNullWhen(true)][MaybeNullWhen(false)] out IBindingHandle? value)
        => _addresses.TryRead(field, out value);

    /// <inheritdoc/>
    public bool TryGetAddress(Symbol field, out MemoryAddress address) => _addresses.TryGetAddress(field, out address);

    /// <summary>
    /// Frees every field this instance allocated. Called once the instance's reference count reaches
    /// zero and <see cref="ISessionObjects.TryRemoveObject"/> succeeds — an instance is never partially
    /// torn down.
    /// </summary>
    public void ReleaseAll() => _addresses.ReleaseAll();
}
