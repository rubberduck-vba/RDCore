using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;
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
    ICallStack callStack,
    IReadOnlyList<ReferencePriorityInfo> references) : IRuntimeSession
{
    public IRuntimeEnvironmentProfile Environment { get; init; } = environment;
    public ISessionMemoryAllocator Memory { get; init; } = memory;
    public ISessionSymbols Symbols { get; init; } = symbols;
    public ISessionObjects Objects { get; init; } = objects;
    public ICallStack CallStack { get; init; } = callStack;
    public IReadOnlyList<ReferencePriorityInfo> References { get; init; } = references;

    public bool ReleaseReference(VBRuntimeObjectId instance, IBindingHandle handle)
        => Objects.RemoveRef(instance, handle) == 0
            && Objects.TryRemoveObject(instance)
            && Symbols.DestroyInstance(instance);
}

/// <summary>
/// Tracks each live object's roots — the <see cref="IBindingHandle"/>s currently holding a reference
/// to it. The reference count is the roots list's own <c>Count</c>, not a separately maintained
/// integer: a second counter can only drift from the list it's supposed to mirror (and did — see the
/// disabled consistency check this replaced), so the list is the single source of truth.
/// </summary>
internal sealed class SessionObjects : ISessionObjects
{
    private readonly Dictionary<VBRuntimeObjectId, List<IBindingHandle>> _roots = [];

    public VBRuntimeObjectId CreateObject()
    {
        var id = new VBRuntimeObjectId();
        _roots[id] = [];
        return id;
    }

    public void AddRef(VBRuntimeObjectId instance, IBindingHandle handle)
    {
        if (_roots.TryGetValue(instance, out var roots))
        {
            roots.Add(handle);
        }
    }

    public int RemoveRef(VBRuntimeObjectId instance, IBindingHandle handle)
    {
        if (!_roots.TryGetValue(instance, out var roots))
        {
            return 0;
        }

        roots.Remove(handle);
        return roots.Count;
    }

    public bool TryRemoveObject(VBRuntimeObjectId instance)
    {
        if (_roots.TryGetValue(instance, out var roots) && roots.Count == 0)
        {
            return _roots.Remove(instance);
        }
        return false;
    }
}

/// <param name="storage">
/// The session's value storage — a program-lifetime declaration (a standard module's or the global
/// scope's <see cref="SymbolKindExt.Field"/>/<see cref="SymbolKindExt.Variable"/>, and a <c>Static</c>
/// local — RD-VBAL's <em>static locals heap</em> lives at module level too) is allocated storage here
/// the moment it's <see cref="TryDefine"/>d, per <strong>RD-VBAL §2.3.1.2</strong>.
/// </param>
/// <param name="callStack">
/// The session's call stack — an ordinary (non-<c>Static</c>) local's storage is instead allocated per
/// activation, on the current <see cref="ICallStackFrame"/>, when a procedure call pushes one.
/// </param>
internal sealed class SessionSymbols(ISessionStorage storage, RuntimeCallStack callStack) : ISessionSymbols
{
    // one bucket per RD-VBAL §2.3.1.2 heap: global, workspace (module), instance, and the local
    // frame. TryDefine keeps the first symbol of a colliding uri; the scope tree walks all four.
    private readonly HashSet<Symbol> _globalSymbols = [];
    private readonly HashSet<Symbol> _workspaceSymbols = [];
    private readonly HashSet<Symbol> _instanceSymbols = [];
    private readonly HashSet<Symbol> _localSymbols = [];

    // live objects, keyed by the identity ISessionObjects.CreateObject minted for them - a separate
    // registry from the four buckets above, since those hold declared *symbols* (one per declared
    // field, shared by every instance of the class), while this holds each instance's own storage.
    private readonly Dictionary<VBRuntimeObjectId, ObjectInstance> _instances = [];

    // a stable component for the session's lifetime — unlike the compile-time name lookup below, the
    // symbol->address map it owns must survive a scope-tree rebuild, not be discarded with it.
    private RuntimeSymbolResolver? _sessionBindingsField;
    private RuntimeSymbolResolver SessionBindings => _sessionBindingsField ??= new RuntimeSymbolResolver(new LiveScopeResolver(this), storage);

    private CallStackAwareSymbolResolver? _bindingsField;
    private CallStackAwareSymbolResolver Bindings => _bindingsField ??= new CallStackAwareSymbolResolver(callStack, SessionBindings);

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

        // instance fields need a live object to allocate into (a later, separate concern); an
        // ordinary local lives on the call stack frame, allocated per activation, not here. A Const
        // is a compile-time substitution, never a runtime address, regardless of scope. A Static
        // local is the one ScopeKind.Local exception: it's allocated here, same as a module field, so
        // it keeps its value between calls instead of being torn down when its frame pops.
        var isStaticLocal = scope is ScopeKind.Local && symbol is VBLocalVariableSymbol { IsStatic: true };
        if ((scope is ScopeKind.Module or ScopeKind.Global || isStaticLocal)
            && symbol is ITypedSymbol { ResolvedType: var type }
            && symbol.Kind is SymbolKindExt.Field or SymbolKindExt.Variable)
        {
            _ = SessionBindings.TryAllocate(symbol, type.DefaultValue, out _);
        }

        return true;
    }

    public ISymbolResolver Resolver => Bindings;

    public bool TryResolveValue(string name, Symbol scope, out Symbol? symbol)
    {
        symbol = Resolver.ResolveValue(name, ScopeKind.Unallocated, scope.Uri).Symbol;
        return symbol is not null;
    }

    public bool TryResolveType(string name, Symbol scope, out Symbol? symbol)
    {
        symbol = Resolver.ResolveType(name, ScopeKind.Unallocated, scope.Uri).Symbol;
        return symbol is not null;
    }

    public ICallStackFrame CreateFrame(SyntaxNodeId nodeId, StaticSymbol procedure, ModuleDirectives directives = default)
        => new CallStackFrame(nodeId, procedure, [], storage, directives);

    public IObjectInstance CreateInstance(VBRuntimeObjectId objectId, VBClassModuleSymbol classModule)
    {
        var instance = new ObjectInstance(objectId, classModule, storage);
        var fields = _instanceSymbols.Where(field =>
            field.ParentUri.AbsoluteUri == classModule.Uri.AbsoluteUri
            && field.Kind is SymbolKindExt.Field or SymbolKindExt.Variable
            && field is ITypedSymbol { ResolvedType: var _ });

        foreach (var field in fields)
        {
            instance.Push(field, ((ITypedSymbol)field).ResolvedType.DefaultValue);
        }

        _instances[objectId] = instance;
        return instance;
    }

    public bool TryGetInstance(VBRuntimeObjectId objectId, [NotNullWhen(true)][MaybeNullWhen(false)] out IObjectInstance? instance)
    {
        var found = _instances.TryGetValue(objectId, out var concrete);
        instance = concrete;
        return found;
    }

    public bool DestroyInstance(VBRuntimeObjectId objectId)
    {
        if (!_instances.Remove(objectId, out var instance))
        {
            return false;
        }

        instance.ReleaseAll();
        return true;
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
        public SymbolResolutionResult ResolveValue(string name, ScopeKind scope, Uri handle)
            => new ScopeTreeSymbolResolver(owner.EnsureScopeTree()).ResolveValue(name, scope, handle);

        public SymbolResolutionResult ResolveType(string name, ScopeKind scope, Uri handle)
            => new ScopeTreeSymbolResolver(owner.EnsureScopeTree()).ResolveType(name, scope, handle);

        public SymbolResolutionResult ResolveQualifier(string name, ScopeKind scope, Uri handle)
            => new ScopeTreeSymbolResolver(owner.EnsureScopeTree()).ResolveQualifier(name, scope, handle);

        public IBindingHandle GetValue(Symbol symbol)
            => throw new NotSupportedException("The scope-tree resolver binds names only; it holds no run-time bindings.");

        public bool TryRead(MemoryAddress address, [NotNullWhen(true)][MaybeNullWhen(false)] out IBindingHandle? value)
        {
            value = null;
            return false;
        }
    }
}
