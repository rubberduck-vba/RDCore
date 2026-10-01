using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using RDCore.CLI.Host;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/symbols/define</c>: reconstructs the runtime member symbols one module
/// declares from the language server's descriptors and defines them in the runtime session. Declared
/// type names are resolved against the intrinsic types and the standard library's own declared types; a
/// name that does not resolve stays <c>VBUnknownType</c> and is reported back.
/// </summary>
internal sealed class DefineSymbolsHandler(
    IEnvironmentSessionProvider sessionProvider,
    ILogger<DefineSymbolsHandler> logger) : RDCoreRequestHandler<DefineSymbolsParams, DefineSymbolsResult>
{
    protected override Task<DefineSymbolsResult> HandleAsync(DefineSymbolsParams request, CancellationToken token)
    {
        if (!sessionProvider.IsComposed)
        {
            logger.LogWarning(
                "rdcore/host/symbols/define received before the runtime session was composed; '{module}' was ignored.",
                request.ModuleName);
            return Task.FromResult(new DefineSymbolsResult());
        }

        var session = sessionProvider.Session;
        var unresolvedTypeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        VBType? ResolveType(string typeName)
        {
            if (IntrinsicVBTypes.TryResolve(typeName, out var type))
            {
                return type;
            }

            // the standard library's own declared types are in this session too — the host injects them
            // into every project whether or not a .rdproj mentions the library (RD-VBAL §6.1) — so
            // `As VbDayOfWeek` and `As ErrObject` bind here instead of being reported unresolved. A
            // workspace type may still not be defined yet, since this defines one module at a time, and
            // stays unresolved exactly as it did before.
            // resolved as seen from the module being defined, not from the global scope: a standard
            // module's members reach the project scope, and the project scope is only an ancestor of a
            // module's own. A request that names no module has no such vantage point, and resolves
            // intrinsics only.
            if (request.ModuleUri is { } moduleUri
                && session.Symbols.Resolver.ResolveType(typeName, ScopeKind.Module, moduleUri) is { IsResolved: true } resolved
                && DeclaredTypeOf(resolved.Symbol) is { } declared)
            {
                return declared;
            }

            unresolvedTypeNames.Add(typeName);
            return null;
        }

        ApplyDirectives(session, request);

        var defined = 0;
        var replaced = 0;
        var merged = 0;
        var skipped = new List<string>();

        // the language server already collapses #If-branch duplicates, but stay defensive: fuse any
        // that still arrive with the same identity (uri + concrete type) so the session never sees a
        // colliding define. Property Get/Let/Set share a uri but not a type, so they stay distinct.
        foreach (var group in SymbolDescriptorReader.Read(request, ResolveType)
            .GroupBy(symbol => (symbol.Uri.ToString(), symbol.GetType())))
        {
            var sites = group.ToList();
            merged += sites.Count - 1;
            var symbol = sites.Find(candidate => candidate is BoundSymbol { Definitions.IsDefaultOrEmpty: false }) ?? sites[0];

            if (session.Symbols.TryDefine(symbol, symbol.ScopeKind))
            {
                defined++;
            }
            else if (request.Replace && session.Symbols.TryRedefine(symbol, symbol.ScopeKind))
            {
                // the caller says this module has been re-read, so the newest definition wins: the
                // previous one may have had different locals, a different declared type, or a body
                // this one no longer has. A variable declared as it was keeps what it holds - a shell
                // re-reads the whole module for every line, and a value that did not survive that would
                // not survive to the next line.
                replaced++;
            }
            else
            {
                skipped.Add(symbol.Name);
            }
        }

        // a class module's symbol is composed from a project without being read; what makes it the class its members declare, and
        // the one that implements the interfaces its directives name, is this. A module that is not a class has nothing to compose.
        session.Symbols.TryComposeClassModule(request.ModuleName, request.ImplementedInterfaceNames);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "📥 {module}: defined {defined} symbol(s), replaced {replaced}, merged {merged}, skipped {skipped}, {unresolved} unresolved type name(s).",
                request.ModuleName, defined, replaced, merged, skipped.Count, unresolvedTypeNames.Count);
        }

        return Task.FromResult(new DefineSymbolsResult
        {
            Defined = defined,
            Replaced = replaced,
            Skipped = skipped,
            UnresolvedTypeNames = [.. unresolvedTypeNames],
            MergedDefinitions = merged,
        });
    }

    // MS-VBAL 5.2.1: the module symbol is composed from the .rdproj without parsing anything, so its
    // Option directives are not known until the language server sends them here. They are what an
    // activation of one of the module's procedures runs under - Option Compare decides how its
    // relational operators compare Strings, Option Base what `Dim a(10)` means - and both were read off
    // a frame that was never given any, so both silently took their default.
    private static void ApplyDirectives(IRuntimeSession session, DefineSymbolsParams request)
    {
        if (!session.Symbols.TryResolveValue(request.ModuleName, GlobalSymbols.UnresolvedSymbol, out var resolved)
            || resolved is not VBModuleSymbol module
            || module.Directives == request.Directives)
        {
            return;
        }

        // the same undefine/define the Replace path uses: a module symbol allocates no storage of its
        // own, and its members are keyed by their own Uris rather than held by it.
        var updated = module with { Directives = request.Directives };
        if (session.Symbols.TryUndefine(module, module.ScopeKind))
        {
            session.Symbols.TryDefine(updated, updated.ScopeKind);
        }
    }

    // an `As` clause names a symbol; this is the type that symbol declares. A user-defined type is not
    // here yet: TODO reconstruct its VBUserDefinedType, which needs the field layout the descriptors do
    // not carry.
    private static VBType? DeclaredTypeOf(Symbol? symbol) => symbol switch
    {
        VBClassModuleSymbol classModule => VBClassType.FromClassModule(classModule),
        VBEnumMemberSymbol { ResolvedType: VBEnumType enumType } => enumType,
        _ => null,
    };
}
