using RDCore.SDK.Model;
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
}

/// <summary>
/// The symbol table of an execution session: defines and resolves symbols by scope.
/// </summary>
public interface ISessionSymbols
{
    /// <summary>
    /// Defines <paramref name="symbol"/> in the given <paramref name="scope"/>.
    /// </summary>
    /// <returns><c>true</c> if the symbol was added; <c>false</c> if it was already defined in that scope.</returns>
    bool TryDefine(Symbol symbol, ScopeKind scope);

    /// <summary>
    /// Resolves <paramref name="name"/> visible from <paramref name="scope"/>.
    /// </summary>
    bool TryResolve(string name, Symbol scope, out Symbol? symbol);
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
