using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.LanguageServer.Symbols;

/// <summary>
/// Composes an <see cref="ISymbolResolver"/> over a whole parsed workspace. A first pass extracts
/// every module's declarations with <paramref name="fallback"/> alone (intrinsic type names only);
/// the resolver returned then binds a workspace name — a sibling module's <c>Type</c> or <c>Enum</c>,
/// a <c>Public</c> member — through a <see cref="ScopeTreeSymbolResolver"/> over the lot, falling
/// back to <paramref name="fallback"/> for the intrinsics.
/// </summary>
internal static class WorkspaceSymbolResolver
{
    // modules are passed as tuples, not a dictionary: Uri equality ignores the fragment, but a module
    // uri differs from its siblings only in the fragment (workspace#ModuleName). ModuleType travels
    // alongside the parse result — the parser is never told a module's kind and does not derive it
    // (RDCore.SDK.Workspace.ModuleHeader.IsClassModule reads it off the raw source instead).
    public static ISymbolResolver Compose(
        Uri workspaceRoot, IEnumerable<(Uri ModuleUri, ModuleType ModuleType, ModuleParseResult Parse)> modules, ISymbolResolver fallback)
    {
        var symbols = new List<Symbol>();
        foreach (var (moduleUri, moduleType, parseResult) in modules)
        {
            // the module symbol itself is the project symbol provider's job at run time; synthesize
            // it here so the scope tree has a module tier to hang the members off (and so a
            // same-module name collision reads as a duplicate declaration, not an ambiguous name).
            var moduleName = moduleUri.Fragment.TrimStart('#');
            symbols.Add(moduleType == ModuleType.ClassModule
                ? new VBClassModuleSymbol(workspaceRoot, workspaceRoot, moduleName)
                : new VBStandardModuleSymbol(workspaceRoot, workspaceRoot, moduleName));

            symbols.AddRange(new SyntaxTreeSymbolProvider(workspaceRoot, moduleUri, moduleType, parseResult, fallback).ProvideSymbols());
        }

        return new CompositeSymbolResolver(new ScopeTreeSymbolResolver(ScopeTreeBuilder.Build(symbols)), fallback);
    }
}
