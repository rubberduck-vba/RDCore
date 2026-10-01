using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// The root of an execution session. A single session holds the statically-defined symbols and,
/// depending on the host mode, serves as the run-time or break (debug) session — it is independent
/// of the host's current mode.
/// </summary>
/// <remarks>
/// ⚖️<strong>RDCore</strong> provides an implementation of this interface <strong>licensed under GPLv3</strong>.
/// </remarks>
public interface IRuntimeSession
{
    /// <summary>
    /// The host environment this session runs in — bitness (<c>Environment.Is64Bit</c> drives
    /// <c>LongPtr</c> width and <c>#If Win64</c> / <c>#If VBA7</c>), locale, code page, …
    /// </summary>
    IRuntimeEnvironmentProfile Environment { get; }

    /// <summary>
    /// The session's memory allocator — tracks allocation size and fragmentation, MSVBVM-style.
    /// </summary>
    ISessionMemoryAllocator Memory { get; }

    /// <summary>
    /// Where this session's <c>Print</c> output goes (<strong>MS-VBAL §5.4.5.8</strong>) — the
    /// <c>Immediate</c> window's analogue. <see cref="NullRuntimeOutput"/> when the session was
    /// composed without one, so a <c>Debug.Print</c> is a no-op rather than an error.
    /// </summary>
    IRuntimeOutput Output { get; }

    /// <summary>
    /// The session's value storage — what is bound at each address its allocator handed out.
    /// </summary>
    /// <remarks>
    /// Reachable from the session because direct, byte-level access to a session's memory is a
    /// session-level operation: a debugger reading a variable's bytes, a <c>PEEK</c>, a <c>POKE</c>.
    /// Ordinary name-based reads and writes go through <see cref="ISessionSymbols.Resolver"/> instead.
    /// </remarks>
    ISessionStorage Storage { get; }

    /// <summary>
    /// The session's symbol table.
    /// </summary>
    ISessionSymbols Symbols { get; }

    /// <summary>
    /// The session's object lifetime manager.
    /// </summary>
    ISessionObjects Objects { get; }

    /// <summary>
    /// The session's error state — what the <c>Err</c> object reports
    /// (<strong>MS-VBAL §6.1.3.2</strong>), and what outlives the activation that raised it.
    /// </summary>
    ISessionErrorState Errors { get; }

    /// <summary>
    /// The file numbers this session has open (<strong>MS-VBAL §5.4.5</strong>).
    /// </summary>
    /// <remarks>
    /// Session-scoped because the association an <c>Open</c> makes "remains in effect until... explicitly
    /// disassociated using a <c>close-statement</c>" - it outlives the procedure that opened it.
    /// </remarks>
    IFileChannels Files { get; }

    /// <summary>
    /// The session's call stack — pushing and popping an <see cref="ICallStackFrame"/> per procedure
    /// activation is what makes a procedure's locals and parameters visible through
    /// <see cref="ISessionSymbols"/>'s <see cref="ISessionSymbols.Resolver"/> (<strong>RD-VBAL
    /// §2.3.1.2</strong>'s local <em>stack frame</em> heap tier).
    /// </summary>
    ICallStack CallStack { get; }

    /// <summary>
    /// The workspace's references in declaration order (<strong>RD-VBAL §2.3.1.2</strong>): index
    /// <c>0</c> appears first and is the lowest precedence (the <c>VBA</c> standard library), so a
    /// later entry shadows it on a global-scope name collision. This is the precedence order only —
    /// a reference's members are resolved through <c>ISymbolResolver</c>, not from here. Preserved
    /// exactly as the language server provides it; empty when the session was composed without a
    /// project (e.g. a bare REPL).
    /// </summary>
    IReadOnlyList<ReferencePriorityInfo> References { get; }

    /// <summary>
    /// Drops <paramref name="handle"/>'s reference to <paramref name="instance"/> and, if that was its
    /// last remaining reference, destroys the object: frees the storage its live instance allocated
    /// (<see cref="ISessionSymbols.DestroyInstance"/>) and forgets it (<see cref="ISessionObjects.TryRemoveObject"/>).
    /// </summary>
    /// <remarks>
    /// This is the only correct way to drop a reference — calling <see cref="ISessionObjects.RemoveRef"/>
    /// directly leaves the instance's field storage allocated forever once the count reaches zero,
    /// since nothing else would go on to call <see cref="ISessionSymbols.DestroyInstance"/> for it.
    /// </remarks>
    /// <returns><c>true</c> if the object was destroyed as a result of this call.</returns>
    bool ReleaseReference(VBRuntimeObjectId instance, IBindingHandle handle);
}

/// <summary>
/// The symbol table of an execution session: defines and resolves symbols by scope.
/// </summary>
public interface ISessionSymbols
{
    /// <summary>
    /// Defines <paramref name="symbol"/> in the given <paramref name="scope"/>. A program-lifetime
    /// declaration — a standard module's or the global scope's field or variable — is allocated
    /// storage immediately, reachable afterwards through <see cref="Resolver"/>'s <c>GetValue</c>.
    /// </summary>
    /// <returns><c>true</c> if the symbol was added; <c>false</c> if it was already defined in that scope.</returns>
    bool TryDefine(Symbol symbol, ScopeKind scope);

    /// <summary>
    /// Removes <paramref name="symbol"/> from <paramref name="scope"/>, freeing whatever storage its
    /// definition allocated.
    /// </summary>
    /// <remarks>
    /// What makes a definition replaceable, which a live session needs: a module the user edits is
    /// defined again, and the second definition is the one that is true. Undefining a symbol whose
    /// storage held a value discards that value — it is a redefinition, not a rename.
    /// </remarks>
    /// <param name="symbol">The symbol to remove.</param>
    /// <param name="scope">The scope it was defined in.</param>
    /// <returns><c>false</c> if no such symbol was defined in that scope.</returns>
    bool TryUndefine(Symbol symbol, ScopeKind scope);

    /// <summary>
    /// Replaces the definition of a symbol that is already defined in <paramref name="scope"/> with
    /// <paramref name="symbol"/>, and keeps the value its storage holds when that value is still the
    /// declaration's own.
    /// </summary>
    /// <remarks>
    /// What a live session needs when a module is read again but a variable in it was not touched:
    /// a module-level variable, or a <c>Static</c> local, is the same declaration before and after, so what it
    /// held is still what it holds. Redefining it with <see cref="TryUndefine"/> and <see cref="TryDefine"/>
    /// instead would hand back its type's default every time anything else in the module changed.
    /// <para>
    /// The value is kept only when the declared type is identical. A variable redeclared as another type is not
    /// the same variable, and its storage — sized for the old type — is freed and allocated again, as it would
    /// be by <see cref="TryUndefine"/> followed by <see cref="TryDefine"/>. A symbol that holds no storage is
    /// simply replaced.
    /// </para>
    /// </remarks>
    /// <param name="symbol">The newest definition, which takes the place of the one with the same identity.</param>
    /// <param name="scope">The scope it was defined in.</param>
    /// <returns><c>false</c> if no symbol with that identity was defined in that scope.</returns>
    bool TryRedefine(Symbol symbol, ScopeKind scope);

    /// <summary>
    /// Resolves <paramref name="name"/> visible from <paramref name="scope"/> in the default binding
    /// context (<see cref="ISymbolResolver.ResolveValue"/>) — the context of a simple name expression.
    /// </summary>
    /// <returns><c>true</c> if the name bound to exactly one symbol.</returns>
    bool TryResolveValue(string name, Symbol scope, out Symbol? symbol);

    /// <summary>
    /// Resolves <paramref name="name"/> visible from <paramref name="scope"/> in the type binding
    /// context (<see cref="ISymbolResolver.ResolveType"/>) — the context of an <c>As</c> clause or the
    /// operand of <c>New</c>.
    /// </summary>
    /// <returns><c>true</c> if the name bound to exactly one symbol.</returns>
    bool TryResolveType(string name, Symbol scope, out Symbol? symbol);

    /// <summary>
    /// Resolves <paramref name="name"/> visible from <paramref name="scope"/> as a conditional
    /// compilation constant (<see cref="ISymbolResolver.ResolveConditionalConstant"/>) — the context of a
    /// <c>#If</c> directive's expression, and the only one such a constant is accessible to
    /// (<strong>MS-VBAL §3.4.1</strong>).
    /// </summary>
    /// <returns><c>true</c> if the name bound to exactly one constant.</returns>
    bool TryResolveConditionalConstant(string name, Symbol scope, out Symbol? symbol);

    /// <summary>
    /// The read face over this table — resolves a name visible from a scope by walking the scope tree
    /// the currently-defined symbols form (tracks later <see cref="TryDefine"/> calls), and reads the
    /// live run-time binding a defined symbol was allocated, if any.
    /// </summary>
    ISymbolResolver Resolver { get; }

    /// <summary>
    /// Creates a new, empty <see cref="ICallStackFrame"/> for an activation of
    /// <paramref name="procedure"/>, wired to this session's value storage. The caller
    /// <see cref="ICallStackFrame.Push"/>es each parameter and <c>Dim</c> local it declares, then
    /// pushes the frame onto <see cref="IRuntimeSession.CallStack"/> to make them resolvable.
    /// </summary>
    /// <param name="nodeId">The <c>Identity</c> of the call-site node this activation is for.</param>
    /// <param name="procedure">The <see cref="StaticSymbol"/> identifying the procedure being activated.</param>
    /// <param name="directives">The <see cref="ModuleDirectives"/> of the module declaring the procedure: what the code of the activation is executed under.</param>
    ICallStackFrame CreateFrame(SyntaxNodeId nodeId, StaticSymbol procedure, ModuleDirectives directives = default);

    /// <summary>
    /// Creates a new <see cref="IObjectInstance"/> for a freshly-created object of
    /// <paramref name="classModule"/>, allocating storage for every instance field the class declares
    /// (<strong>RD-VBAL §2.3.1.2</strong>'s instance heap tier) and registering it for later lookup by
    /// <paramref name="objectId"/>. This only allocates the instance's field storage — the object's
    /// reference-counted lifetime is tracked separately, by <see cref="IRuntimeSession.Objects"/>.
    /// </summary>
    /// <param name="objectId">The object's identity, minted by <see cref="ISessionObjects.CreateObject"/>.</param>
    /// <param name="classModule">The class module being instantiated.</param>
    IObjectInstance CreateInstance(VBRuntimeObjectId objectId, VBClassModuleSymbol classModule);

    /// <summary>
    /// Gets the <see cref="IObjectInstance"/> registered for <paramref name="objectId"/>, if any —
    /// e.g. to resolve a member-access expression's target once <paramref name="objectId"/> is known
    /// from evaluating the object reference it was accessed through.
    /// </summary>
    bool TryGetInstance(VBRuntimeObjectId objectId, [NotNullWhen(true)][MaybeNullWhen(false)] out IObjectInstance? instance);

    /// <summary>
    /// Frees every field <paramref name="objectId"/>'s instance allocated and forgets it. Called once
    /// the object's reference count reaches zero and <see cref="ISessionObjects.TryRemoveObject"/>
    /// succeeds.
    /// </summary>
    /// <returns><c>false</c> if no instance is registered for <paramref name="objectId"/>.</returns>
    bool DestroyInstance(VBRuntimeObjectId objectId);
}

/// <summary>
/// The object lifetime manager of an execution session: creates instances and tracks their roots and reference counts.
/// </summary>
public interface ISessionObjects
{
    /// <summary>Creates a new object instance and returns its identity.</summary>
    VBRuntimeObjectId CreateObject();

    /// <summary>Removes an instance whose reference count has reached zero.</summary>
    bool TryRemoveObject(VBRuntimeObjectId instance);

    /// <summary>Records a new reference (root) to an instance.</summary>
    void AddRef(VBRuntimeObjectId instance, IBindingHandle handle);

    /// <summary>Drops a reference to an instance and returns the remaining reference count.</summary>
    int RemoveRef(VBRuntimeObjectId instance, IBindingHandle handle);
}
