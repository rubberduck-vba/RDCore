using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("RDCore.Tests")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

namespace RDCore.Runtime.Execution;

internal sealed class RuntimeSession(
    IRuntimeEnvironmentProfile environment,
    ISessionMemoryAllocator memory,
    ISessionStorage storage,
    ISessionSymbols symbols,
    ISessionObjects objects,
    ISessionErrorState errors,
    IFileChannels files,
    ICallStack callStack,
    ISessionHalt halt,
    IReadOnlyList<ReferencePriorityInfo> references,
    IRuntimeOutput output) : IRuntimeSession
{
    public IRuntimeEnvironmentProfile Environment { get; init; } = environment;
    public ISessionMemoryAllocator Memory { get; init; } = memory;
    public ISessionStorage Storage { get; init; } = storage;
    public ISessionSymbols Symbols { get; init; } = symbols;
    public ISessionObjects Objects { get; init; } = objects;
    public ISessionErrorState Errors { get; init; } = errors;
    public IFileChannels Files { get; init; } = files;
    public ICallStack CallStack { get; init; } = callStack;
    public ISessionHalt Halt { get; init; } = halt;
    public IReadOnlyList<ReferencePriorityInfo> References { get; init; } = references;
    public IRuntimeOutput Output { get; init; } = output;

    public IObjectLifecycle? Lifecycle { get; set; }

    public bool ReleaseReference(VBRuntimeObjectId instance, IBindingHandle handle)
    {
        if (Objects.RemoveRef(instance, handle) != 0)
        {
            return false;
        }

        // MS-VBAL §5.3.1.10: Terminate runs while the object is still whole, and may give it a reference again; an
        // object that has one is not destroyed, and is a candidate again when it next loses its last.
        // 🚧 TODO an error the handler leaves unhandled is dropped here: a release has no operation to fail, and
        // the callers of this have no result to carry it in. It belongs to whatever dropped the reference.
        if (Lifecycle is { } lifecycle && Objects.TryBeginTerminate(instance))
        {
            _ = lifecycle.Terminate(instance);
        }

        if (Objects.RefCount(instance) != 0)
        {
            return false;
        }

        // the object is going away: what it held is let go of, which can be what destroys that in turn, and nothing
        // of it handles an event any more.
        ReleaseFieldsOf(instance);
        Objects.DetachSubscriber(instance);

        return Objects.TryRemoveObject(instance) && Symbols.DestroyInstance(instance);
    }

    // an object's variables hold the objects they were set to, and cease to when it does (MS-VBAL §2.3: the variables
    // of an object have the extent of the object). A field that holds nothing it counted, or no object, releases nothing.
    private void ReleaseFieldsOf(VBRuntimeObjectId instance)
    {
        if (!Symbols.TryGetInstance(instance, out var live))
        {
            return;
        }

        foreach (var field in live.ClassModule.Members.Where(member
            => member is VBModuleFieldVariableMemberSymbol or VBInstanceFieldVariableMemberSymbol
            && member.ResolvedType is VBClassType or VBObjectType))
        {
            var handle = live.GetValue(field);
            if (field.ResolvedType.CreateValue(handle) is VBObjectValue held)
            {
                ObjectReferences.Release(this, handle, new VBObjectValue(held.Value));
            }
        }
    }
}

internal sealed class SessionHalt : ISessionHalt
{
    public RuntimeHaltKind? Pending { get; private set; }

    public SourceLocation? Location { get; private set; }

    public void Request(RuntimeHaltKind kind, SourceLocation? location = null)
    {
        // the first request stands: what unwinds a program that is stopping is not another reason to stop it.
        if (Pending is not null)
        {
            return;
        }

        Pending = kind;
        Location = location;
    }

    public void Clear()
    {
        Pending = null;
        Location = null;
    }
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
    private readonly HashSet<VBRuntimeObjectId> _terminating = [];
    private readonly Dictionary<VBRuntimeObjectId, List<EventSubscription>> _subscribers = [];

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

        // a handle is a root by identity: handles are records, and two variables holding the same object compare
        // equal by value while being two references.
        var held = roots.FindIndex(root => ReferenceEquals(root, handle));
        if (held >= 0)
        {
            roots.RemoveAt(held);
        }

        return roots.Count;
    }

    public bool IsHeldBy(VBRuntimeObjectId instance, IBindingHandle handle)
        => _roots.TryGetValue(instance, out var roots) && roots.Any(root => ReferenceEquals(root, handle));

    public int RefCount(VBRuntimeObjectId instance) => _roots.TryGetValue(instance, out var roots) ? roots.Count : 0;

    public bool TryBeginTerminate(VBRuntimeObjectId instance) => _roots.ContainsKey(instance) && _terminating.Add(instance);

    public void AttachEventHandlers(VBRuntimeObjectId source, VBRuntimeObjectId subscriber, Symbol variable)
    {
        // an assignment moves the variable to the end of the order, so it is detached before it is attached again.
        DetachEventHandlers(source, subscriber, variable);
        if (!_subscribers.TryGetValue(source, out var subscriptions))
        {
            _subscribers[source] = subscriptions = [];
        }

        subscriptions.Add(new EventSubscription(subscriber, variable));
    }

    public void DetachEventHandlers(VBRuntimeObjectId source, VBRuntimeObjectId subscriber, Symbol variable)
    {
        if (_subscribers.TryGetValue(source, out var subscriptions))
        {
            subscriptions.RemoveAll(subscription => IsSubscription(subscription, subscriber, variable));
        }
    }

    public void DetachSubscriber(VBRuntimeObjectId subscriber)
    {
        foreach (var subscriptions in _subscribers.Values)
        {
            subscriptions.RemoveAll(subscription => subscription.Subscriber.Equals(subscriber));
        }
    }

    public IReadOnlyList<EventSubscription> EventSubscribers(VBRuntimeObjectId source)
        => _subscribers.TryGetValue(source, out var subscriptions) ? [.. subscriptions] : [];

    // a Uri's fragment is where a symbol's identity lives, so the variable is compared by its SemanticId.
    private static bool IsSubscription(EventSubscription subscription, VBRuntimeObjectId subscriber, Symbol variable)
        => subscription.Subscriber.Equals(subscriber) && subscription.Variable.SemanticId.Equals(variable.SemanticId);

    public bool TryRemoveObject(VBRuntimeObjectId instance)
    {
        if (_roots.TryGetValue(instance, out var roots) && roots.Count == 0)
        {
            _terminating.Remove(instance);
            _subscribers.Remove(instance);
            return _roots.Remove(instance);
        }
        return false;
    }

    public void Clear()
    {
        _roots.Clear();
        _terminating.Clear();
        _subscribers.Clear();
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
    private readonly Dictionary<SymbolIdentity, Symbol> _globalSymbols = [];
    private readonly Dictionary<SymbolIdentity, Symbol> _workspaceSymbols = [];
    private readonly Dictionary<SymbolIdentity, Symbol> _instanceSymbols = [];
    private readonly Dictionary<SymbolIdentity, Symbol> _localSymbols = [];

    /// <inheritdoc/>
    public IVariableDefaults? Defaults { get; set; }

    private VBTypedValue DefaultValueOf(Symbol variable)
        => Defaults?.DefaultValueOf(variable) ?? ((ITypedSymbol)variable).ResolvedType.DefaultValue;

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

    // live objects, keyed by the identity ISessionObjects.CreateObject minted for them - a separate
    // registry from the four buckets above, since those hold declared *symbols* (one per declared
    // field, shared by every instance of the class), while this holds each instance's own storage.
    private readonly Dictionary<VBRuntimeObjectId, ObjectInstance> _instances = [];

    // a stable component for the session's lifetime — unlike the compile-time name lookup below, the
    // symbol->address map it owns must survive a scope-tree rebuild, not be discarded with it.
    private RuntimeSymbolResolver? _sessionBindingsField;
    private RuntimeSymbolResolver SessionBindings => _sessionBindingsField ??= new RuntimeSymbolResolver(new LiveScopeResolver(this), storage);

    private CallStackAwareSymbolResolver? _bindingsField;
    private CallStackAwareSymbolResolver Bindings => _bindingsField ??= new CallStackAwareSymbolResolver(callStack, SessionBindings, this);

    private ScopeTree? _scopeTree;

    // one bucket per RD-VBAL §2.3.1.2 heap tier; every scope maps onto exactly one.
    private Dictionary<SymbolIdentity, Symbol> TableFor(ScopeKind scope) => scope switch
    {
        ScopeKind.Module => _workspaceSymbols,
        ScopeKind.Instance => _instanceSymbols,
        ScopeKind.Local or ScopeKind.External => _localSymbols,
        _ => _globalSymbols,
    };

    public bool TryDefine(Symbol symbol, ScopeKind scope)
    {
        _scopeTree = null;  // resolution rebuilds the tree on next use

        if (!TableFor(scope).TryAdd(SymbolIdentity.Of(symbol), symbol))
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
            && symbol is ITypedSymbol
            && symbol.Kind is SymbolKindExt.Field or SymbolKindExt.Variable)
        {
            _ = SessionBindings.TryAllocate(symbol, DefaultValueOf(symbol), out _);
        }

        return true;
    }

    public bool TryUndefine(Symbol symbol, ScopeKind scope)
    {
        var table = TableFor(scope);
        if (!table.Remove(SymbolIdentity.Of(symbol)))
        {
            return false;
        }

        // resolution rebuilds the scope tree on next use; the storage the definition allocated (a
        // module field, a Static local) is freed here, since nothing can reach it by name any more.
        _scopeTree = null;
        SessionBindings.TryDeallocate(symbol);
        return true;
    }

    public bool TryRedefine(Symbol symbol, ScopeKind scope)
    {
        var table = TableFor(scope);
        var identity = SymbolIdentity.Of(symbol);
        if (!table.TryGetValue(identity, out var existing))
        {
            return false;
        }

        // the same declaration, written again: a variable that holds storage and is declared as the type it was.
        // Anything else is a different variable - or no variable - and is replaced the long way.
        var sameDeclaration = existing is ITypedSymbol { ResolvedType: var before }
            && symbol is ITypedSymbol { ResolvedType: var after }
            && Equals(before, after)
            && SessionBindings.TryGetAddress(existing, out _);
        if (!sameDeclaration)
        {
            return TryUndefine(existing, scope) && TryDefine(symbol, scope);
        }

        // storage is keyed by the symbol's semantic identity, which the newest definition shares, so replacing the
        // symbol itself leaves what it holds where it is.
        table[identity] = symbol;
        _scopeTree = null;
        return true;
    }

    public void ResetStorage()
    {
        // an object goes with the program that made it: nothing of that program is left to run a Terminate.
        foreach (var instance in _instances.Values)
        {
            instance.ReleaseAll();
        }

        _instances.Clear();

        // a variable of a module, or a global, starts again from what it was defined as. Taken before any of them is touched: freeing and allocating
        // storage is what the lookup of the next one would otherwise be made over.
        var variables = _globalSymbols.Values.Concat(_workspaceSymbols.Values)
            .Where(symbol => symbol is ITypedSymbol && symbol.Kind is SymbolKindExt.Field or SymbolKindExt.Variable && SessionBindings.TryGetAddress(symbol, out _))
            .ToList();
        foreach (var variable in variables)
        {
            SessionBindings.TryDeallocate(variable);
            _ = SessionBindings.TryAllocate(variable, DefaultValueOf(variable), out _);
        }

        // a Static local is allocated by its procedure's first call, and so starts from nothing: the call that comes next allocates it again.
        var statics = _localSymbols.Values.OfType<VBLocalVariableSymbol>()
            .Where(symbol => symbol.IsStatic && SessionBindings.TryGetAddress(symbol, out _))
            .ToList();
        foreach (var local in statics)
        {
            SessionBindings.TryDeallocate(local);
        }
    }

    public bool TryComposeClassModule(
        string moduleName, ImmutableArray<string> implementedInterfaceNames, ImmutableArray<SourceRange> implementedInterfaceRanges = default)
    {
        var module = AllSymbols().OfType<VBClassModuleSymbol>()
            .FirstOrDefault(candidate => string.Equals(candidate.Name, moduleName, StringComparison.OrdinalIgnoreCase));
        if (module is null)
        {
            return false;
        }

        // what the class declares: every member defined under its identity.
        var composed = module with { Members = [.. MembersOf(module.Uri)], ImplementedInterfaceNames = implementedInterfaceNames,
            ImplementedInterfaceRanges = implementedInterfaceRanges.IsDefault ? [] : implementedInterfaceRanges,
        };
        Replace(module, composed with { DefaultInterfaceMembers = VBClassType.FromClassModule(composed).Members });

        // every class that names an interface is resolved again, whichever it is that has just been composed: it may be the
        // interface another holds, as it was.
        var classModules = AllSymbols().OfType<VBClassModuleSymbol>().ToList();
        var resolved = ImplementedInterfaceResolution.Resolve(classModules);
        foreach (var classModule in classModules)
        {
            if (resolved.TryGetValue(classModule.Uri.AbsoluteUri, out var withInterfaces))
            {
                Replace(classModule, withInterfaces);
            }
        }

        return true;
    }

    public IReadOnlyList<VBTypeMemberSymbol> MembersOf(Uri moduleUri)
        => [.. AllSymbols().OfType<VBTypeMemberSymbol>().Where(member => member.ParentUri.AbsoluteUri == moduleUri.AbsoluteUri)];

    // A symbol's Uri is its parent's with the fragment extended by its own name: a symbol is under a module when it is of the same document and its
    // fragment continues the module's with a dot. VBA names are not case sensitive, and so neither is the comparison.
    public IReadOnlyList<Symbol> DeclaredIn(Uri moduleUri)
    {
        var document = moduleUri.GetLeftPart(UriPartial.Path);
        var prefix = moduleUri.Fragment.TrimStart('#') + ".";

        return [.. AllSymbols().Where(symbol =>
            string.Equals(symbol.Uri.GetLeftPart(UriPartial.Path), document, StringComparison.Ordinal)
            && symbol.Uri.Fragment.TrimStart('#').StartsWith(prefix, StringComparison.OrdinalIgnoreCase))];
    }

    public LexicalScope? ScopeOf(Uri uri) => EnsureScopeTree().TryGetScope(uri, out var scope) ? scope : null;

    private IEnumerable<Symbol> AllSymbols()
        => _globalSymbols.Values.Concat(_workspaceSymbols.Values).Concat(_instanceSymbols.Values).Concat(_localSymbols.Values).ToList();

    // a class module symbol holds no storage and is keyed by its identity, which the newer one shares.
    private void Replace(Symbol existing, Symbol replacement)
    {
        if (TryUndefine(existing, existing.ScopeKind))
        {
            TryDefine(replacement, replacement.ScopeKind);
        }
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

    public bool TryResolveConditionalConstant(string name, Symbol scope, out Symbol? symbol)
    {
        symbol = Resolver.ResolveConditionalConstant(name, ScopeKind.Unallocated, scope.Uri).Symbol;
        return symbol is not null;
    }

    public ICallStackFrame CreateFrame(SyntaxNodeId nodeId, StaticSymbol procedure, ModuleDirectives directives = default)
        => new CallStackFrame(nodeId, procedure, [], storage, directives);

    public IObjectInstance CreateInstance(VBRuntimeObjectId objectId, VBClassModuleSymbol classModule)
    {
        var instance = new ObjectInstance(objectId, classModule, storage);
        var fields = _instanceSymbols.Values.Where(field =>
            field.ParentUri.AbsoluteUri == classModule.Uri.AbsoluteUri
            && field.Kind is SymbolKindExt.Field or SymbolKindExt.Variable
            && field is ITypedSymbol { ResolvedType: var _ });

        foreach (var field in fields)
        {
            instance.Push(field, DefaultValueOf(field));
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
            [.. _globalSymbols.Values, .. _workspaceSymbols.Values, .. _instanceSymbols.Values, .. _localSymbols.Values]);

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

        public SymbolResolutionResult ResolveConditionalConstant(string name, ScopeKind scope, Uri handle)
            => new ScopeTreeSymbolResolver(owner.EnsureScopeTree()).ResolveConditionalConstant(name, scope, handle);

        public SymbolResolutionResult ResolveMember(Symbol qualifier, string name, Uri handle)
            => new ScopeTreeSymbolResolver(owner.EnsureScopeTree()).ResolveMember(qualifier, name, handle);

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
