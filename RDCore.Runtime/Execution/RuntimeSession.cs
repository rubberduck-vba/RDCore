using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("RDCore.Tests")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

namespace RDCore.Runtime.Execution;

internal sealed class RuntimeSession(
    IRuntimeEnvironmentProfile environment,
    ISessionMemoryAllocator memory,
    ISessionSymbols symbols,
    ISessionObjects objects,
    IReadOnlyList<ReferencePriorityInfo> references) : IRuntimeSession
{
    public IRuntimeEnvironmentProfile Environment { get; init; } = environment;
    public ISessionMemoryAllocator Memory { get; init; } = memory;
    public ISessionSymbols Symbols { get; init; } = symbols;
    public ISessionObjects Objects { get; init; } = objects;
    public IReadOnlyList<ReferencePriorityInfo> References { get; init; } = references;
}

internal sealed class SessionObjects : ISessionObjects
{
    private readonly Dictionary<VBRuntimeObjectId, List<IBindingHandle>> _roots = [];
    private readonly Dictionary<VBRuntimeObjectId, int> _refs = [];

    public VBRuntimeObjectId CreateObject()
    {
        var id = new VBRuntimeObjectId();
        _roots[id] = [];
        _refs[id] = 0;
        return id;
    }

    public void AddRef(VBRuntimeObjectId instance, IBindingHandle handle)
    {
        if (_roots.TryGetValue(instance, out var roots))
        {
            roots.Add(handle);
        }
        if (_refs.TryGetValue(instance, out _))
        {
            _refs[instance]++;
        }        
    }

    public int RemoveRef(VBRuntimeObjectId instance, IBindingHandle handle)
    {
        if (_roots.TryGetValue(instance, out var roots))
        {
            _ = roots.Remove(handle);
        }
        if (_refs.TryGetValue(instance, out _))
        {
            _refs[instance]--;
        }

        return _refs[instance];
    }

    public bool TryRemoveObject(VBRuntimeObjectId instance)
    {
        if (_refs.TryGetValue(instance, out var refCount) && refCount == 0
            /*&& _roots[instance].Count == 0*/)
        {
            return _refs.Remove(instance)
                && _roots.Remove(instance);
        }
        return false;
    }
}

/// <param name="storage">
/// The session's value storage — a program-lifetime declaration (a standard module's or the global
/// scope's <see cref="SymbolKindExt.Field"/>/<see cref="SymbolKindExt.Variable"/>) is allocated
/// storage here the moment it's <see cref="TryDefine"/>d, per <strong>RD-VBAL §2.3.1.2</strong>.
/// </param>
internal sealed class SessionSymbols(ISessionStorage storage) : ISessionSymbols
{
    // one bucket per RD-VBAL §2.3.1.2 heap: global, workspace (module), instance, and the local
    // frame. TryDefine keeps the first symbol of a colliding uri; the scope tree walks all four.
    private readonly HashSet<Symbol> _globalSymbols = [];
    private readonly HashSet<Symbol> _workspaceSymbols = [];
    private readonly HashSet<Symbol> _instanceSymbols = [];
    private readonly HashSet<Symbol> _localSymbols = [];

    // a stable component for the session's lifetime — unlike the compile-time name lookup below, the
    // symbol->address map it owns must survive a scope-tree rebuild, not be discarded with it.
    private RuntimeSymbolResolver? _bindingsField;
    private RuntimeSymbolResolver Bindings => _bindingsField ??= new RuntimeSymbolResolver(new LiveScopeResolver(this), storage);

    private ScopeTree? _scopeTree;

    public bool TryDefine(Symbol symbol, ScopeKind scope)
    {
        _scopeTree = null;  // resolution rebuilds the tree on next use

        var table = scope switch
        {
            ScopeKind.Module => _workspaceSymbols,
            ScopeKind.Instance => _instanceSymbols,
            ScopeKind.Local or ScopeKind.External => _localSymbols,
            _ => _globalSymbols,
        };

        if (!table.Add(symbol))
        {
            return false;
        }

        // instance fields need a live object to allocate into (a later, separate concern); locals
        // live on the call stack frame, not here. A Const is a compile-time substitution, never a
        // runtime address, regardless of scope.
        if (scope is ScopeKind.Module or ScopeKind.Global
            && symbol is ITypedSymbol { ResolvedType: var type }
            && symbol.Kind is SymbolKindExt.Field or SymbolKindExt.Variable)
        {
            _ = Bindings.TryAllocate(symbol, type.DefaultValue, out _);
        }

        return true;
    }

    public ISymbolResolver Resolver => Bindings;

    public bool TryResolve(string name, Symbol scope, out Symbol? symbol)
    {
        symbol = Resolver.Resolve(name, ScopeKind.Unallocated, scope.Uri).Symbol;
        return symbol is not null;
    }

    private ScopeTree EnsureScopeTree()
        => _scopeTree ??= ScopeTreeBuilder.Build(
            [.. _globalSymbols, .. _workspaceSymbols, .. _instanceSymbols, .. _localSymbols]);

    /// <summary>
    /// The name-lookup half of <see cref="Bindings"/>: walks a fresh <see cref="ScopeTreeSymbolResolver"/>
    /// over <paramref name="owner"/>'s always-current scope tree. Cheap to rebuild per call —
    /// <see cref="EnsureScopeTree"/> itself is the memoized, expensive part.
    /// </summary>
    private sealed class LiveScopeResolver(SessionSymbols owner) : ISymbolResolver
    {
        public SymbolResolutionResult Resolve(string name, ScopeKind scope, Uri handle)
            => new ScopeTreeSymbolResolver(owner.EnsureScopeTree()).Resolve(name, scope, handle);

        public IBindingHandle GetValue(Symbol symbol)
            => throw new NotSupportedException("The scope-tree resolver binds names only; it holds no run-time bindings.");

        public bool TryRead(MemoryAddress address, [NotNullWhen(true)][MaybeNullWhen(false)] out IBindingHandle? value)
        {
            value = null;
            return false;
        }
    }
}
