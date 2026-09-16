using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;

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
    /// The session's symbol table.
    /// </summary>
    ISessionSymbols Symbols { get; }

    /// <summary>
    /// The session's object lifetime manager.
    /// </summary>
    ISessionObjects Objects { get; }

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
    /// Resolves <paramref name="name"/> visible from <paramref name="scope"/>.
    /// </summary>
    bool TryResolve(string name, Symbol scope, out Symbol? symbol);

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
    ICallStackFrame CreateFrame(SyntaxNodeId nodeId, StaticSymbol procedure);
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
