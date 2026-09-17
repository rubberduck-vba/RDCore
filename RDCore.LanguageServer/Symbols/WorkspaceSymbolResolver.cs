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
    /// <summary>
    /// Modules are passed as tuples, not a dictionary: <c>Uri</c> equality ignores the fragment, but a
    /// module uri differs from its siblings only in the fragment (<c>workspace#ModuleName</c>).
    /// <c>ModuleType</c> travels alongside the parse result — the parser is never told a module's kind
    /// and does not derive it (<c>RDCore.SDK.Workspace.ModuleHeader.IsClassModule</c> reads it off the
    /// raw source instead).
    /// </summary>
    /// <param name="projectName">
    /// The enclosing project's own name, if known — synthesizes a resolvable <see cref="VBProjectSymbol"/>
    /// so a qualified type reference (<c>Project.ClassName</c>, <strong>MS-VBAL 5.6.4</strong>'s type
    /// binding context) can resolve <c>Project</c> to something. <c>null</c> omits it — same as before
    /// this parameter existed, only unqualified names resolve.
    /// </param>
    public static ISymbolResolver Compose(
        Uri workspaceRoot, IEnumerable<(Uri ModuleUri, ModuleType ModuleType, ModuleParseResult Parse)> modules,
        ISymbolResolver fallback, string? projectName = null)
    {
        var symbols = new List<Symbol>();
        if (projectName is not null)
        {
            symbols.Add(new VBProjectSymbol(workspaceRoot, projectName));
        }

        foreach (var (moduleUri, moduleType, parseResult) in modules)
        {
            // the module symbol itself is the project symbol provider's job at run time; synthesize
            // it here so the scope tree has a module tier to hang the members off (and so a
            // same-module name collision reads as a duplicate declaration, not an ambiguous name).
            var moduleName = moduleUri.Fragment.TrimStart('#');
            var directives = new ModuleDirectives(Explicit: parseResult.SyntaxTree?.HasOptionExplicit() ?? false);
            VBModuleSymbol module = moduleType == ModuleType.ClassModule
                ? (VBModuleSymbol)new VBClassModuleSymbol(workspaceRoot, workspaceRoot, moduleName) { Directives = directives }
                    .With(SymbolProperties.Creatable, parseResult.SyntaxTree?.IsCreatable() ?? true)
                : new VBStandardModuleSymbol(workspaceRoot, workspaceRoot, moduleName) { Directives = directives };

            // members can't ride on the module symbol the way a Type's fields ride on it (built from
            // one AST node's own children) - a module's members are separate top-level declarations,
            // so they're only known once the member provider below has run.
            var members = new SyntaxTreeSymbolProvider(workspaceRoot, moduleUri, moduleType, parseResult, fallback).ProvideSymbols().ToList();

            symbols.Add(module with
            {
                Members = [.. members.Where(member => member.ParentUri.AbsoluteUri == module.Uri.AbsoluteUri).OfType<VBTypeMemberSymbol>()],
            });
            symbols.AddRange(members);
        }

        return new CompositeSymbolResolver(new ScopeTreeSymbolResolver(ScopeTreeBuilder.Build(symbols)), fallback);
    }
}
