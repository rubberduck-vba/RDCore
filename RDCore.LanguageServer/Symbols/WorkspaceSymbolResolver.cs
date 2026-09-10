using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.LanguageServer.Symbols;

/// <summary>
/// Composes an <see cref="ISymbolResolver"/> over a whole parsed workspace. A first pass extracts
/// every module's declarations with <paramref name="fallback"/> alone (intrinsic type names only);
/// the resolver returned then binds a workspace name — a sibling module's <c>Type</c> or <c>Enum</c>,
/// a <c>Public</c> member — through a <see cref="ScopeTreeSymbolResolver"/> over the lot, and falls
/// back to <paramref name="fallback"/> for the intrinsics.
/// </summary>
internal static class WorkspaceSymbolResolver
{
    // modules are passed as pairs, not a dictionary: Uri equality ignores the fragment, but a module
    // uri differs from its siblings only in the fragment (workspace#ModuleName).
    public static ISymbolResolver Compose(
        Uri workspaceRoot, IEnumerable<(Uri ModuleUri, ModuleParseResult Parse)> modules, ISymbolResolver fallback)
    {
        var symbols = new List<Symbol>();
        foreach (var (moduleUri, parseResult) in modules)
        {
            symbols.AddRange(new SyntaxTreeSymbolProvider(workspaceRoot, moduleUri, parseResult, fallback).ProvideSymbols());
        }

        return new CompositeSymbolResolver(new ScopeTreeSymbolResolver(ScopeTreeBuilder.Build(symbols)), fallback);
    }
}
