using RDCore.SDK.Model;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("RDCore.Tests")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

namespace RDCore.Runtime.Execution;

internal sealed class RuntimeSession(ISessionMemoryAllocator memory, ISessionSymbols symbols, ISessionObjects objects) : IRuntimeSession
{
    public bool Is64Bit { get; init; }
    public ISessionMemoryAllocator Memory { get; init; } = memory;
    public ISessionSymbols Symbols { get; init; } = symbols;
    public ISessionObjects Objects { get; init; } = objects;
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
    private readonly HashSet<Symbol> _globalSymbols = [];
    private readonly HashSet<Symbol> _workspaceSymbols = [];
    private readonly HashSet<Symbol> _staticLocalSymbols = [];

    private readonly Dictionary<Uri, Symbol> _idMap = [];
    private readonly Dictionary<string, string> _nameTable = [];

    public bool TryDefine(Symbol symbol, ScopeKind scope)
    {
        _nameTable[symbol.Name.ToLowerInvariant()] = symbol.Name;
        _idMap[symbol.Uri] = symbol;

        var symbolTable = scope switch
        {
            ScopeKind.Global => _globalSymbols,
            ScopeKind.Module => _workspaceSymbols,
            ScopeKind.Instance => _staticLocalSymbols,
            _ => default
        };
        return symbolTable?.Add(symbol) ?? false;
    }

    private bool IsAccessibleFrom(AccessibleTypedSymbol? symbol, Symbol scope)
    {
        if (symbol is null)
        {
            return false;
        }

        if (symbol.ParentUri == scope.Uri)
        {
            // local scope
            return true;
        }

        var scopingParent = _idMap[scope.ParentUri];
        if (scopingParent.Children.Any(c => c == symbol.Uri))
        {
            // same module
            return true;
        }

        var scopingProject = _idMap[scopingParent.ParentUri];
        if (scopingProject.Children.Any(c => c == symbol.Uri))
        {
            // same project
            return symbol.AccessModifier != AccessModifier.Private;
        }

        if (_globalSymbols.Contains(symbol))
        {
            return symbol.AccessModifier != AccessModifier.Private;
        }

        return false;
    }

    public bool TryResolve(string name, Symbol scope, out Symbol? symbol)
    {
        symbol = FindCandidates(name, _staticLocalSymbols).SingleOrDefault(s => s.ParentUri == scope.Uri)
            ?? FindCandidates(name, _workspaceSymbols).OfType<AccessibleTypedSymbol>()
                .SingleOrDefault(s => IsAccessibleFrom(s, scope))
            ?? FindCandidates(name, _globalSymbols).OfType<AccessibleTypedSymbol>()
                .FirstOrDefault(s => IsAccessibleFrom(s, scope));
        return symbol != default;
    }

    private static Symbol[] FindCandidates(string name, HashSet<Symbol> symbolTable) 
        => [.. symbolTable.Where(s => s.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase))];
}
