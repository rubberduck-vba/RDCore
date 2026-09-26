using RDCore.SDK.Model;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Symbols;

/// <summary>
/// Builds a <see cref="ScopeTree"/> from a flat set of composed symbols. Placement is structural —
/// a symbol's concrete type, its <see cref="Symbol.ParentUri"/>, and its access modifier decide its
/// scope:
/// <list type="bullet">
/// <item>module symbols, project-level precompiler constants, global <see cref="StaticSymbol"/>s, and
///   anything the tree cannot otherwise place → the <see cref="ScopeTree.Global"/> scope;</item>
/// <item>a standard module's non-<c>Private</c> members → the project scope, where sibling modules
///   see them;</item>
/// <item>every module's members — fields, constants, procedures, properties, enums, user-defined
///   types, events, <c>Declare</c>s → that module's own scope;</item>
/// <item>a procedure's parameters and its procedure-local <c>Dim</c> / <c>Static</c> / <c>Const</c>
///   (and the dynamic array a bare <c>ReDim</c> introduces) → that procedure's scope.</item>
/// </list>
/// An enum constant is placed in the scope that declares its <c>Enum</c>, since that is where
/// <strong>MS-VBAL §5.2.3.4</strong> makes it accessible — it parents to the enum, not to a scope, so
/// its placement is resolved through the enum. A user-defined type's fields are reached through member
/// access rather than lexical scoping, and are not placed at all. Ordering referenced libraries by
/// their <c>.rdproj</c> priority within the global scope is later work.
/// </summary>
public static class ScopeTreeBuilder
{
    /// <summary>
    /// Builds the scope tree for <paramref name="symbols"/>. Any subset is valid input — a symbol
    /// whose enclosing scope is not in the set is placed in the <see cref="ScopeTree.Global"/> scope.
    /// </summary>
    public static ScopeTree Build(IEnumerable<Symbol> symbols)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        var all = symbols as IReadOnlyCollection<Symbol> ?? [.. symbols];

        // pass 1 — index the symbols that define a scope, by their own uri (Uri equality ignores the
        // fragment, and the scope path lives entirely in the fragment, so key on AbsoluteUri).
        var modules = new Dictionary<string, Symbol>(StringComparer.Ordinal);
        var standardModuleUris = new HashSet<string>(StringComparer.Ordinal);
        var procedures = new Dictionary<string, Symbol>(StringComparer.Ordinal);
        var enums = new Dictionary<string, VBEnumMemberSymbol>(StringComparer.Ordinal);
        foreach (var symbol in all)
        {
            if (symbol is VBModuleSymbol)
            {
                modules[symbol.Uri.AbsoluteUri] = symbol;
                if (symbol is VBStandardModuleSymbol)
                {
                    standardModuleUris.Add(symbol.Uri.AbsoluteUri);
                }
            }
            else if (symbol is VBEnumMemberSymbol declaredEnum)
            {
                enums[symbol.Uri.AbsoluteUri] = declaredEnum;
            }
            else if (DefinesProcedureScope(symbol))
            {
                procedures[symbol.Uri.AbsoluteUri] = symbol;
            }
        }

        // pass 2 — bucket every symbol under the scope that declares it.
        var globalDeclarations = new List<Symbol>();
        var moduleDeclarations = modules.Keys.ToDictionary(uri => uri, _ => new List<Symbol>(), StringComparer.Ordinal);
        var procedureDeclarations = procedures.Keys.ToDictionary(uri => uri, _ => new List<Symbol>(), StringComparer.Ordinal);

        foreach (var symbol in all)
        {
            if (symbol is VBModuleSymbol or PrecompilerConstantSymbol or StaticSymbol)
            {
                globalDeclarations.Add(symbol);
                continue;
            }

            var parent = symbol.ParentUri.AbsoluteUri;
            if (moduleDeclarations.TryGetValue(parent, out var inModule))
            {
                inModule.Add(symbol);
            }
            else if (procedureDeclarations.TryGetValue(parent, out var inProcedure))
            {
                inProcedure.Add(symbol);
            }
            else if (symbol is not (VBEnumConstMemberSymbol or VBUserDefinedTypeFieldSymbol))
            {
                // a udt field is reached by member access, not lexical scoping, and an enum constant
                // parents to its enum rather than to a scope — pass 2b places those. Any other
                // unplaced symbol falls back to the global scope.
                globalDeclarations.Add(symbol);
            }
        }

        // pass 2b — MS-VBAL §5.2.3.4: "the Enum type and its Enum members are accessible within the
        // enclosing project" (public) or "within the enclosing module" (private). An enum constant is
        // lexically scoped exactly as its enum is, so it goes in the scope that declares the enum. A
        // constant whose enum is not in this set has no scope to hang off, and is dropped.
        var projectEnumConstants = new List<Symbol>();
        foreach (var constant in all.OfType<VBEnumConstMemberSymbol>())
        {
            if (!enums.TryGetValue(constant.ParentUri.AbsoluteUri, out var declaringEnum))
            {
                continue;
            }

            if (moduleDeclarations.TryGetValue(declaringEnum.ParentUri.AbsoluteUri, out var inModule))
            {
                inModule.Add(constant);
                if (IsProjectVisible(declaringEnum))
                {
                    projectEnumConstants.Add(constant);
                }
            }
            else
            {
                // an enum the standard library declares parents to the global scope rather than to a
                // module of the project, and its constants resolve from there.
                globalDeclarations.Add(constant);
            }
        }

        // a procedure's parameters and its own Dim/Static/Const locals ride on the member symbol, not
        // as separate entries in `all`.
        foreach (var (uri, symbol) in procedures)
        {
            procedureDeclarations[uri].AddRange(ParametersOf(symbol));
            procedureDeclarations[uri].AddRange(LocalsOf(symbol));
        }

        // pass 3 — materialize global -> project -> modules -> procedures, wiring each parent scope.
        var global = new LexicalScope(StaticSymbol.GlobalUri, LexicalScopeKind.Global, parent: null, globalDeclarations);
        var scopeByUri = new Dictionary<string, LexicalScope>(StringComparer.Ordinal)
        {
            [StaticSymbol.GlobalUri.AbsoluteUri] = global,
        };

        // the project scope surfaces a standard module's non-Private members to its siblings. it is
        // keyed on the workspace root the modules share, and skipped when the set has no modules.
        var moduleParent = global;
        if (modules.Count > 0)
        {
            var workspaceRoot = modules.Values.First().WorkspaceRoot;
            var projectDeclarations = standardModuleUris
                .SelectMany(uri => moduleDeclarations[uri])
                // an enum constant has no access modifier of its own — its enum's is what decides, and
                // projectEnumConstants already carries the ones that reach here.
                .Where(symbol => symbol is not VBEnumConstMemberSymbol && IsProjectVisible(symbol))
                // a public Enum or user-defined type is accessible within the project wherever it is
                // declared (MS-VBAL §5.2.3.4 / §5.2.3.3) — a class module's other members would need
                // an instance, but a declared type needs none.
                .Concat(modules.Keys
                    .Where(uri => !standardModuleUris.Contains(uri))
                    .SelectMany(uri => moduleDeclarations[uri])
                    .Where(symbol => symbol is VBEnumMemberSymbol or VBUserDefinedTypeMemberSymbol)
                    .Where(IsProjectVisible))
                .Concat(projectEnumConstants)
                .ToList();
            var project = new LexicalScope(workspaceRoot, LexicalScopeKind.Project, global, projectDeclarations);
            scopeByUri[workspaceRoot.AbsoluteUri] = project;
            moduleParent = project;
        }

        var moduleScopes = new Dictionary<string, LexicalScope>(StringComparer.Ordinal);
        foreach (var (uri, symbol) in modules)
        {
            var directives = symbol is VBModuleSymbol module ? module.Directives : ModuleDirectives.None;
            var scope = new LexicalScope(symbol.Uri, LexicalScopeKind.Module, moduleParent, moduleDeclarations[uri], directives);
            moduleScopes[uri] = scope;
            scopeByUri[uri] = scope;
            MapDeclarationsToScope(scopeByUri, moduleDeclarations[uri], scope);
        }

        foreach (var (uri, symbol) in procedures)
        {
            var parent = moduleScopes.GetValueOrDefault(symbol.ParentUri.AbsoluteUri, moduleParent);
            var scope = new LexicalScope(symbol.Uri, LexicalScopeKind.Procedure, parent, procedureDeclarations[uri]);
            scopeByUri[uri] = scope; // a procedure resolves from its own scope, not its module's
            MapDeclarationsToScope(scopeByUri, procedureDeclarations[uri], scope);
        }

        MapDeclarationsToScope(scopeByUri, globalDeclarations, global);

        return new ScopeTree(global, scopeByUri);
    }

    // record each declared symbol's own uri against the scope that declares it, so a lookup can
    // start from any symbol — a field resolves from its module scope, a local from its procedure
    // scope. A uri that already maps to a scope it *defines* (a nested procedure) keeps that mapping.
    private static void MapDeclarationsToScope(
        Dictionary<string, LexicalScope> scopeByUri, IEnumerable<Symbol> declarations, LexicalScope scope)
    {
        foreach (var symbol in declarations)
        {
            scopeByUri.TryAdd(symbol.Uri.AbsoluteUri, scope);
        }
    }

    private static bool DefinesProcedureScope(Symbol symbol) => symbol switch
    {
        VBModuleFieldVariableMemberSymbol => false, // a field, despite its returning-member base
        VBConstantMemberSymbol => false,
        VBProcedureMemberSymbol => true,            // + Property Let/Set, Declare Sub
        VBFunctionMemberSymbol => true,             // + Declare Function
        VBPropertyGetMemberSymbol => true,
        VBEventMemberSymbol => true,
        _ => false,
    };

    // MS-VBAL §5.2.3 project-level visibility: an explicit Public / Global / Friend member is visible
    // to sibling modules; a Private one is not; an implicit modifier makes a procedure-like member
    // Public but a module variable or constant Private.
    private static bool IsProjectVisible(Symbol symbol)
    {
        if (symbol is not AccessibleTypedSymbol accessible)
        {
            return false;
        }

        return accessible.AccessModifier switch
        {
            AccessModifier.Private => false,
            AccessModifier.Public or AccessModifier.Global or AccessModifier.Friend => true,
            _ => symbol is not (VBModuleFieldVariableMemberSymbol or VBConstantMemberSymbol),
        };
    }

    private static ImmutableArray<VBParameterSymbol> ParametersOf(Symbol symbol) => symbol switch
    {
        VBReturningMemberSymbol member => member.Parameters,
        VBProcedureMemberSymbol member => member.Parameters,
        VBEventMemberSymbol member => member.Parameters,
        _ => [],
    };

    private static ImmutableArray<BoundTypedSymbol> LocalsOf(Symbol symbol) => symbol switch
    {
        VBReturningMemberSymbol member => member.Locals,
        VBProcedureMemberSymbol member => member.Locals,
        _ => [],
    };
}
