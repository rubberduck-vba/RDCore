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
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Services.VerboseMessages;
using RDCore.Runtime.Execution;
using System.Collections.Immutable;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/symbols/define</c>: reconstructs the runtime member symbols one module
/// declares from the language server's descriptors and defines them in the runtime session. Declared
/// type names are resolved against the intrinsic types and the standard library's own declared types; a
/// name that does not resolve stays <c>VBUnknownType</c> and is reported back.
/// </summary>
internal sealed class DefineSymbolsHandler(
    IEnvironmentSessionProvider sessionProvider,
    IVerboseMessageBuilder messages,
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

        ApplyDirectives(session, request);

        var unresolvedTypeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var (defined, replaced, merged, skipped) = request.CodeOnly
            ? (0, 0, 0, [])
            : DefineSymbols(session, request, unresolvedTypeNames, replace: request.Replace);

        // A type that a declaration names is defined by the same request that declares it, and everything is read before anything is defined: so a variable declared as
        // a user-defined type of its own module was read before the type was there, and kept the name as written. The module is read again, now that it is, and
        // defined over what it was - as many times as a type can be made of types that were not there either (a type with a field of a type), and no more.
        if (!request.CodeOnly && (unresolvedTypeNames.Count > 0 || request.Symbols.Any(symbol => symbol.Kind == SymbolDescriptorKind.UserDefinedType)))
        {
            for (var pass = 0; pass < MaxTypePasses; pass++)
            {
                unresolvedTypeNames.Clear();
                _ = DefineSymbols(session, request, unresolvedTypeNames, replace: true);
            }
        }

        // a class module's symbol is composed from a project without being read; what makes it the class its members declare, and
        // the one that implements the interfaces its directives name, is this. A module that is not a class has nothing to compose.
        session.Symbols.TryComposeClassModule(request.ModuleName, request.ImplementedInterfaceNames, request.ImplementedInterfaceRanges);

        // the module's procedures get their code now that everything they are keyed by is defined.
        var loadErrors = LoadCode(session, request);

        // a module that declares a function of a native library can call it as soon as it runs, and what makes the call is got ready now - in an environment that
        // lets a program make one at all.
        if (session.Environment.AllowDllImports
            && request.Symbols.Any(symbol => symbol.Kind is SymbolDescriptorKind.ExternalProcedure or SymbolDescriptorKind.ExternalFunction))
        {
            sessionProvider.Outside.Libraries.Prepare();
        }

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
            CodeErrors = loadErrors,
            MergedDefinitions = merged,
        });
    }

    /// <summary>How many times a module is read again for the types it names: a user-defined type of user-defined types is as deep as its fields.</summary>
    private const int MaxTypePasses = 3;

    // Reads the module's symbols and defines them, the newest winning when asked to. The language server already collapses #If-branch duplicates, but stay
    // defensive: fuse any that still arrive with the same identity (uri + concrete type) so the session never sees a colliding define. Property Get/Let/Set
    // share a uri but not a type, so they stay distinct.
    private static (int Defined, int Replaced, int Merged, List<string> Skipped) DefineSymbols(
        IRuntimeSession session, DefineSymbolsParams request, HashSet<string> unresolvedTypeNames, bool replace)
    {
        var (defined, replaced, merged, skipped) = (0, 0, 0, new List<string>());

        VBType? ResolveType(string typeName)
        {
            if (IntrinsicVBTypes.TryResolve(typeName, session.Environment.Is64Bit, out var type))
            {
                return type;
            }

            // the standard library's own declared types are in this session too — the host injects them
            // into every project whether or not a .rdproj mentions the library (RD-VBAL §6.1) — so
            // `As VbDayOfWeek` and `As ErrObject` bind here instead of being reported unresolved. A
            // workspace type may still not be defined yet, since this defines one module at a time, and
            // stays unresolved until the module is read again.
            // resolved as seen from the module being defined, not from the global scope: a standard
            // module's members reach the project scope, and the project scope is only an ancestor of a
            // module's own. A request that names no module has no such vantage point, and resolves
            // intrinsics only.
            // a name qualified by a library is that library's, whatever else the references call by it.
            if (request.ModuleUri is { } moduleUri
                && ResolveNamed(session, typeName, moduleUri) is { IsResolved: true } resolved
                && DeclaredTypeOf(session, resolved.Symbol) is { } declared)
            {
                return declared;
            }

            unresolvedTypeNames.Add(typeName);
            return null;
        }

        foreach (var group in SymbolDescriptorReader.Read(request, ResolveType).GroupBy(symbol => (symbol.Uri.ToString(), symbol.GetType())))
        {
            var sites = group.ToList();
            merged += sites.Count - 1;
            var symbol = sites.Find(candidate => candidate is WorkspaceSymbol { Definitions.IsDefaultOrEmpty: false }) ?? sites[0];

            if (session.Symbols.TryDefine(symbol, symbol.ScopeKind))
            {
                defined++;
            }
            else if (replace && session.Symbols.TryRedefine(symbol, symbol.ScopeKind))
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

        return (defined, replaced, merged, skipped);
    }

    // `Library.Name` is the type of that library (MS-VBAL 5.6.12) and a bare name is resolved from the module as written; a name that merely contains a dot
    // and qualifies nothing is looked for as it is.
    private static SymbolResolutionResult ResolveNamed(IRuntimeSession session, string typeName, Uri moduleUri)
    {
        var resolver = session.Symbols.Resolver;
        if (typeName.IndexOf('.') is > 0 and var dot
            && VBProjectSymbol.ResolveQualifiedType(resolver, typeName[..dot], typeName[(dot + 1)..], moduleUri) is { IsResolved: true } qualified)
        {
            return qualified;
        }

        return resolver.ResolveType(typeName, ScopeKind.Module, moduleUri);
    }

    // a standard module is a value in the default binding context, and a class module is a type: the name of one binds only
    // in the type binding context (MS-VBAL 5.6.4), unless the class has a default instance.
    private static bool TryResolveModule(IRuntimeSession session, string moduleName, out Symbol module)
    {
        if ((session.Symbols.TryResolveValue(moduleName, GlobalSymbols.UnresolvedSymbol, out var resolved)
                || session.Symbols.TryResolveType(moduleName, GlobalSymbols.UnresolvedSymbol, out resolved))
            && resolved is VBModuleSymbol)
        {
            module = resolved;
            return true;
        }

        module = GlobalSymbols.UnresolvedSymbol;
        return false;
    }

    private ImmutableArray<string> LoadCode(IRuntimeSession session, DefineSymbolsParams request)
    {
        if (request.ParseResultJson.Length == 0
            || PlatformJson.Deserialize<ModuleParseResult>(request.ParseResultJson) is not { } parseResult
            || !TryResolveModule(session, request.ModuleName, out var module))
        {
            return [];
        }

        var errors = new ModuleLoader(session, sessionProvider.Image, messages).Load(module, parseResult);
        foreach (var error in errors)
        {
            logger.LogWarning("{module} was not loaded: {error}", request.ModuleName, error);
        }

        return errors;
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

    // an `As` clause names a symbol; this is the type that symbol declares. A user-defined type is its fields, which are symbols of their own in the session once the
    // type is defined - the descriptors of the type carry them, and they are read and defined with it.
    private static VBType? DeclaredTypeOf(IRuntimeSession session, Symbol? symbol) => symbol switch
    {
        VBClassModuleSymbol classModule => VBClassType.FromClassModule(classModule),
        VBEnumMemberSymbol { ResolvedType: VBEnumType enumType } => enumType,
        VBUserDefinedTypeMemberSymbol udt => new VBUserDefinedType(udt, udt.Members),
        _ => null,
    };
}
