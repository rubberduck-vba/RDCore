using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("RDCore.Tests")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

namespace RDCore.Runtime.Execution;

internal sealed class RuntimeSession(
    IRuntimeEnvironmentProfile environment,
    ISessionMemoryAllocator memory,
    ISessionSymbols symbols,
    ISessionObjects objects,
    IReadOnlyList<ProjectReference> references) : IRuntimeSession
{
    public IRuntimeEnvironmentProfile Environment { get; init; } = environment;
    public ISessionMemoryAllocator Memory { get; init; } = memory;
    public ISessionSymbols Symbols { get; init; } = symbols;
    public ISessionObjects Objects { get; init; } = objects;
    public IReadOnlyList<ProjectReference> References { get; init; } = references;
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

internal sealed class SessionSymbols : ISessionSymbols
{
    // one bucket per RD-VBAL §2.3.1.2 heap: global, workspace (module), instance, and the local
    // frame. TryDefine keeps the first symbol of a colliding uri; the scope tree walks all four.
    private readonly HashSet<Symbol> _globalSymbols = [];
    private readonly HashSet<Symbol> _workspaceSymbols = [];
    private readonly HashSet<Symbol> _instanceSymbols = [];
    private readonly HashSet<Symbol> _localSymbols = [];

    private ScopeTree? _scopeTree;

    public bool TryDefine(Symbol symbol, ScopeKind scope)
    {
        _scopeTree = null; // resolution rebuilds the scope tree on next use

        var table = scope switch
        {
            ScopeKind.Module => _workspaceSymbols,
            ScopeKind.Instance => _instanceSymbols,
            ScopeKind.Local or ScopeKind.External => _localSymbols,
            _ => _globalSymbols,
        };
        return table.Add(symbol);
    }

    public bool TryResolve(string name, Symbol scope, out Symbol? symbol)
    {
        foreach (var lexicalScope in EnsureScopeTree().ScopeFor(scope.Uri).SelfAndAncestors())
        {
            var matches = lexicalScope.DeclaredAs(name).Take(2).ToArray();
            if (matches.Length == 0)
            {
                continue;
            }

            // a name declared more than once in one scope is an MS-VBAL "ambiguous name"
            // (RD-VBAL VBC09303); reporting that as a compile error needs a result channel this
            // interface does not have yet, so for now an ambiguous name reads as unresolved.
            symbol = matches.Length == 1 ? matches[0] : null;
            return symbol is not null;
        }

        symbol = null;
        return false;
    }

    private ScopeTree EnsureScopeTree()
        => _scopeTree ??= ScopeTreeBuilder.Build(
            [.. _globalSymbols, .. _workspaceSymbols, .. _instanceSymbols, .. _localSymbols]);
}
