using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Collections.Immutable;

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
            var implementedInterfaceNames = parseResult.SyntaxTree?.GetImplementedInterfaceNames() ?? [];
            VBModuleSymbol module = moduleType == ModuleType.ClassModule
                ? (VBModuleSymbol)new VBClassModuleSymbol(workspaceRoot, workspaceRoot, moduleName)
                    { Directives = directives, ImplementedInterfaceNames = implementedInterfaceNames }
                    .With(SymbolProperties.Creatable, parseResult.SyntaxTree?.IsCreatable() ?? true)
                : new VBStandardModuleSymbol(workspaceRoot, workspaceRoot, moduleName) { Directives = directives };

            // members can't ride on the module symbol the way a Type's fields ride on it (built from
            // one AST node's own children) - a module's members are separate top-level declarations,
            // so they're only known once the member provider below has run.
            var members = new SyntaxTreeSymbolProvider(workspaceRoot, moduleUri, moduleType, parseResult, fallback).ProvideSymbols().ToList();
            ImmutableArray<VBTypeMemberSymbol> ownMembers =
                [.. members.Where(member => member.ParentUri.AbsoluteUri == module.Uri.AbsoluteUri).OfType<VBTypeMemberSymbol>()];

            module = module with { Members = ownMembers };
            if (module is VBClassModuleSymbol classModule)
            {
                // the default interface is a pure function of Members, fixed the moment it's known -
                // compute it here, once, so New/As-type/Me never rebuild it at resolution time (see
                // VBClassType.FromClassModule's own remarks).
                module = classModule with { DefaultInterfaceMembers = VBClassType.FromClassModule(classModule).Members };
            }

            symbols.Add(module);
            symbols.AddRange(members);
        }

        ResolveImplementedInterfaces(symbols);

        return new CompositeSymbolResolver(new ScopeTreeSymbolResolver(ScopeTreeBuilder.Build(symbols)), fallback);
    }

    /// <summary>
    /// Resolves each class module's <see cref="VBClassModuleSymbol.ImplementedInterfaceNames"/>
    /// (<strong>MS-VBAL §5.2.4.2</strong>) to the sibling class it names, mutating <paramref name="symbols"/>
    /// in place. This has to be its own pass, after every module's own symbol already exists in
    /// <paramref name="symbols"/> — unlike <see cref="VBClassModuleSymbol.DefaultInterfaceMembers"/>,
    /// which only ever needs the current module's own already-known <c>Members</c> and so can be
    /// computed inline in the per-module loop above.
    /// </summary>
    /// <remarks>
    /// A class name is a project-level identifier with no lexical scoping/shadowing concerns, so a
    /// direct case-insensitive lookup among this composition's own class modules is enough here — no
    /// need for a full <see cref="ISymbolResolver"/> round-trip. A name that doesn't resolve to a class
    /// in this composition, or that resolves back to the same class, is silently dropped rather than
    /// reported: full MS-VBAL §5.2.4.2/§5.3.1.9 validity checking (self-reference, duplicate interfaces,
    /// name collisions, member-shape matching) is not modeled yet.
    /// <para>
    /// Resolution is recursive and memoized by <c>Uri</c>, not a flat single pass over
    /// <paramref name="symbols"/>: a flat pass would only ever assign each class's <c>ImplementedInterfaces</c>
    /// from the <em>pre-resolution</em> snapshot in <c>classModulesByName</c>, so a transitive chain
    /// (<c>Widget Implements IMiddle</c>, <c>IMiddle Implements IBase</c>) would leave <c>IMiddle</c>'s
    /// own <c>ImplementedInterfaces</c> looking empty from <c>Widget</c>'s side, regardless of which
    /// order the two classes happen to appear in <paramref name="symbols"/>. Recursing into (and
    /// caching) each interface's own fully-resolved symbol before returning it is what makes
    /// <see cref="VBClassType.FromClassModule(VBClassModuleSymbol)"/>'s own recursion see the whole
    /// chain from any entry point.
    /// </para>
    /// </remarks>
    private static void ResolveImplementedInterfaces(List<Symbol> symbols)
    {
        var classModulesByName = new Dictionary<string, VBClassModuleSymbol>(StringComparer.OrdinalIgnoreCase);
        foreach (var classModule in symbols.OfType<VBClassModuleSymbol>())
        {
            classModulesByName.TryAdd(classModule.Name, classModule);
        }

        var resolved = new Dictionary<string, VBClassModuleSymbol>(StringComparer.Ordinal);
        var resolving = new HashSet<string>(StringComparer.Ordinal);

        VBClassModuleSymbol Resolve(VBClassModuleSymbol classModule)
        {
            var key = classModule.Uri.AbsoluteUri;
            if (resolved.TryGetValue(key, out var already))
            {
                return already;
            }
            if (!resolving.Add(key))
            {
                // a cycle (MS-VBAL 5.2.3.6 disallows this, not yet validated) - stop recursing here
                // rather than looping forever over a malformed workspace.
                return classModule;
            }

            var implementedInterfaces = ImmutableArray.CreateBuilder<VBClassModuleSymbol>();
            var seenUris = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in classModule.ImplementedInterfaceNames)
            {
                if (classModulesByName.TryGetValue(name, out var found)
                    && !string.Equals(found.Uri.AbsoluteUri, key, StringComparison.Ordinal)
                    && seenUris.Add(found.Uri.AbsoluteUri))
                {
                    implementedInterfaces.Add(Resolve(found));
                }
            }

            var result = classModule with { ImplementedInterfaces = implementedInterfaces.ToImmutable() };
            resolving.Remove(key);
            resolved[key] = result;
            return result;
        }

        for (var i = 0; i < symbols.Count; i++)
        {
            if (symbols[i] is VBClassModuleSymbol classModule && !classModule.ImplementedInterfaceNames.IsEmpty)
            {
                symbols[i] = Resolve(classModule);
            }
        }
    }
}
