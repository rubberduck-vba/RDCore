using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.Runtime.Execution;

/// <summary>
/// The declared symbols of a session, and how a name finds one: the <em>static context</em> that code is evaluated in
/// (<strong>RD-VBAL §2.3.1.2</strong>).
/// </summary>
/// <remarks>
/// <para>
/// Holds what the code declares and nothing that happens when it runs: no storage, no bindings, no objects. That is what lets
/// an evaluation that must not touch the live session's state (an analysis) be given its own state over the same table,
/// instead of a copy of thousands of symbols.
/// </para>
/// <para>
/// Not thread-safe: the scope tree is built on first use after a change, so a table that is shared has to be done changing,
/// and have had its tree built, before anything else reads it.
/// </para>
/// </remarks>
internal sealed class SymbolTable
{
    // one bucket per RD-VBAL §2.3.1.2 heap: global, workspace (module), instance, and the local
    // frame. TryAdd keeps the first symbol of a colliding uri; the scope tree walks all four.
    private readonly Dictionary<SymbolIdentity, Symbol> _globalSymbols = [];
    private readonly Dictionary<SymbolIdentity, Symbol> _workspaceSymbols = [];
    private readonly Dictionary<SymbolIdentity, Symbol> _instanceSymbols = [];
    private readonly Dictionary<SymbolIdentity, Symbol> _localSymbols = [];

    private ScopeTree? _scopeTree;

    /// <summary>
    /// What makes two definitions the same declaration: the symbol's own semantic identity, plus its
    /// concrete type.
    /// </summary>
    /// <remarks>
    /// Not the symbol itself. A <c>Symbol</c> is a record, so record equality compares every member,
    /// derived ones included — two definitions of the SAME declaration that differ only in what was
    /// known about it (a procedure that gained its locals, a field whose declared type resolved on the
    /// second pass) compare unequal, and the table would hold both. The name would then resolve to
    /// neither of them, since resolution requires exactly one match.
    /// <para>
    /// The concrete type is part of it because a <c>Property</c>'s <c>Get</c>, <c>Let</c> and
    /// <c>Set</c> accessors are three declarations that legitimately share one URI.
    /// </para>
    /// </remarks>
    private readonly record struct SymbolIdentity(SemanticId Id, Type Declaration)
    {
        public static SymbolIdentity Of(Symbol symbol) => new(symbol.SemanticId, symbol.GetType());
    }

    /// <summary>
    /// Resolves names against the table as it is when the name is looked up.
    /// </summary>
    public ISymbolResolver Names => new LiveScopeResolver(this);

    /// <summary>
    /// The symbols declared in the instance tier: the fields of the classes.
    /// </summary>
    public IEnumerable<Symbol> InstanceSymbols => _instanceSymbols.Values;

    /// <summary>
    /// The symbols declared in the global and workspace tiers: the variables and fields that outlive a call.
    /// </summary>
    public IEnumerable<Symbol> ModuleLevelSymbols => _globalSymbols.Values.Concat(_workspaceSymbols.Values);

    /// <summary>
    /// The symbols declared in the local tier: parameters, local variables and what a procedure declares inside.
    /// </summary>
    public IEnumerable<Symbol> LocalSymbols => _localSymbols.Values;

    /// <summary>
    /// Every symbol declared, whichever tier it is in.
    /// </summary>
    /// <remarks>
    /// A snapshot: the caller may define and undefine symbols while enumerating it.
    /// </remarks>
    public IReadOnlyList<Symbol> All()
        => [.. _globalSymbols.Values, .. _workspaceSymbols.Values, .. _instanceSymbols.Values, .. _localSymbols.Values];

    /// <summary>
    /// The scope tree of the symbols as they are now.
    /// </summary>
    public ScopeTree ScopeTree => _scopeTree ??= ScopeTreeBuilder.Build(All());

    /// <summary>
    /// Finds the module of this name, a standard module or a class module: the project's, or a library's when the project has none of the name.
    /// </summary>
    /// <remarks>
    /// A lookup of the modules themselves, not a binding of the name: what a name means in code - a class's default instance, a variable that shadows a
    /// module - is no concern of it. VBA names are not case sensitive, and neither is the lookup.
    /// </remarks>
    public bool TryGetModule(string name, [NotNullWhen(true)] out VBModuleSymbol? module)
        => (_modules ??= IndexModules()).TryGetValue(name, out module);

    // the modules by name, built when one is asked for after the table changed: a project's module before a library's of the same name.
    private Dictionary<string, VBModuleSymbol> IndexModules()
    {
        var modules = new Dictionary<string, VBModuleSymbol>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in All().OfType<VBModuleSymbol>().OrderBy(module => module.GetProperty(SymbolProperties.Library) is null ? 0 : 1))
        {
            modules.TryAdd(module.Name, module);
        }

        return modules;
    }

    private Dictionary<string, VBModuleSymbol>? _modules;

    /// <summary>
    /// A copy of the table as it is now, which nothing that is done to this one reaches, and so is safe to read from another thread.
    /// </summary>
    /// <remarks>
    /// The symbols are immutable and are not copied, only the tiers that hold them; the scope tree is built once and not changed after it is built (a table that
    /// changes builds another), so the copy shares it. This is taken by whoever owns the table, on the thread that changes it: the copy is consistent as of that call.
    /// </remarks>
    public SymbolTable Snapshot()
    {
        var snapshot = new SymbolTable();
        foreach (var (tier, copy) in new[]
        {
            (_globalSymbols, snapshot._globalSymbols), (_workspaceSymbols, snapshot._workspaceSymbols),
            (_instanceSymbols, snapshot._instanceSymbols), (_localSymbols, snapshot._localSymbols),
        })
        {
            foreach (var (identity, symbol) in tier)
            {
                copy.Add(identity, symbol);
            }
        }

        snapshot._scopeTree = ScopeTree;
        return snapshot;
    }

    /// <summary>
    /// Adds <paramref name="symbol"/> to the tier its scope belongs to.
    /// </summary>
    /// <returns><see langword="false"/> if that tier already holds the declaration: the first one wins.</returns>
    public bool TryAdd(Symbol symbol, ScopeKind scope)
    {
        _scopeTree = null;
        _modules = null;
        return TableFor(scope).TryAdd(SymbolIdentity.Of(symbol), symbol);
    }

    /// <summary>
    /// Removes the declaration <paramref name="symbol"/> is from the tier its scope belongs to.
    /// </summary>
    /// <returns><see langword="false"/> if there was none.</returns>
    public bool Remove(Symbol symbol, ScopeKind scope)
    {
        var removed = TableFor(scope).Remove(SymbolIdentity.Of(symbol));
        if (removed)
        {
            _scopeTree = null;
            _modules = null;
        }

        return removed;
    }

    /// <summary>
    /// Gets the declaration that <paramref name="symbol"/> is a newer definition of.
    /// </summary>
    public bool TryGet(Symbol symbol, ScopeKind scope, [NotNullWhen(true)][MaybeNullWhen(false)] out Symbol? existing)
        => TableFor(scope).TryGetValue(SymbolIdentity.Of(symbol), out existing);

    /// <summary>
    /// Replaces a declaration with a newer definition of it, in place.
    /// </summary>
    public void Replace(Symbol symbol, ScopeKind scope)
    {
        TableFor(scope)[SymbolIdentity.Of(symbol)] = symbol;
        _scopeTree = null;
        _modules = null;
    }

    // one bucket per RD-VBAL §2.3.1.2 heap tier; every scope maps onto exactly one.
    private Dictionary<SymbolIdentity, Symbol> TableFor(ScopeKind scope) => scope switch
    {
        ScopeKind.Module => _workspaceSymbols,
        ScopeKind.Instance => _instanceSymbols,
        ScopeKind.Local or ScopeKind.External => _localSymbols,
        _ => _globalSymbols,
    };

    /// <summary>
    /// The name-lookup half of a session's resolver: a <see cref="ScopeTreeSymbolResolver"/> over the table's always-current scope tree.
    /// </summary>
    /// <remarks>
    /// A resolver holds nothing but its tree, so the one made for a tree resolves every name looked up in it, until a declaration changes and the table
    /// builds another tree. The tree and its resolver are kept together, so a thread that reads them while another replaces them reads a pair.
    /// </remarks>
    private sealed class LiveScopeResolver(SymbolTable owner) : ISymbolResolver
    {
        private sealed record class TreeResolver(ScopeTree Tree, ScopeTreeSymbolResolver Resolver);

        private TreeResolver? _current;

        private ScopeTreeSymbolResolver Resolver
        {
            get
            {
                var tree = owner.ScopeTree;
                if (_current is not { } current || !ReferenceEquals(current.Tree, tree))
                {
                    _current = current = new TreeResolver(tree, new ScopeTreeSymbolResolver(tree));
                }

                return current.Resolver;
            }
        }

        public SymbolResolutionResult ResolveValue(string name, ScopeKind scope, Uri handle)
            => Resolver.ResolveValue(name, scope, handle);

        public SymbolResolutionResult ResolveType(string name, ScopeKind scope, Uri handle)
            => Resolver.ResolveType(name, scope, handle);

        public SymbolResolutionResult ResolveQualifier(string name, ScopeKind scope, Uri handle)
            => Resolver.ResolveQualifier(name, scope, handle);

        public SymbolResolutionResult ResolveProjectType(VBProjectSymbol project, string name)
            => Resolver.ResolveProjectType(project, name);

        public SymbolResolutionResult ResolveConditionalConstant(string name, ScopeKind scope, Uri handle)
            => Resolver.ResolveConditionalConstant(name, scope, handle);

        public SymbolResolutionResult ResolveMember(Symbol qualifier, string name, Uri handle)
            => Resolver.ResolveMember(qualifier, name, handle);

        public IBindingHandle GetValue(Symbol symbol)
            => throw new NotSupportedException("The scope-tree resolver binds names only; it holds no run-time bindings.");

        public bool TryRead(MemoryAddress address, [NotNullWhen(true)][MaybeNullWhen(false)] out IBindingHandle? value)
        {
            value = null;
            return false;
        }

        public bool TryGetAddress(Symbol symbol, out MemoryAddress address)
        {
            address = default;
            return false;
        }

        public bool TryAllocate(Symbol symbol, VBTypedValue value, out MemoryAddress address)
        {
            address = default;
            return false;
        }
    }
}
