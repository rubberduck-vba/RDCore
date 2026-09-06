using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// Produces the <see cref="Symbol"/>s a session or semantic context is composed from. Each provider
/// is constructed with its own source and yields the symbols that source defines.
/// </summary>
/// <remarks>
/// ⚖️ <strong>RDCore</strong> provides implementations of this interface <strong>licensed under GPLv3</strong>:
/// <list type="bullet">
/// <item><c>ConfigurationSymbolProvider</c> (environment host) — precompiler constants from the <c>.rdproj</c> project file, overridden by CLI arguments.</item>
/// <item><c>ProjectSymbolProvider</c> (environment host) — the module and reference structure declared by the <c>.rdproj</c>.</item>
/// <item><c>SyntaxTreeSymbolProvider</c> (language server) — declarations resolved from a parsed module's AST (both live and dead precompiler branches); their descriptors reach the environment host over <c>rdcore/host/symbols/define</c>.</item>
/// <item><c>LibrarySymbolProvider</c> — types and members reflected from a referenced library.</item>
/// </list>
/// The environment host's own runtime and standard-library symbols are provided the same way.
/// </remarks>
public interface ISymbolProvider
{
    /// <summary>
    /// Yields the symbols this provider's source defines. May be lazy.
    /// </summary>
    IEnumerable<Symbol> ProvideSymbols();
}
