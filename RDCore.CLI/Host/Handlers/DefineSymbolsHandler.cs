using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using RDCore.CLI.Host;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/symbols/define</c>: reconstructs the runtime member symbols one module
/// declares from the language server's descriptors and defines them in the runtime session. Declared
/// type names are resolved against the intrinsic types only for now (Slice 4); a name that does not
/// resolve stays <c>VBUnknownType</c> and is reported back.
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

            unresolvedTypeNames.Add(typeName);
            return null;
        }

        var defined = 0;
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
            else
            {
                skipped.Add(symbol.Name);
            }
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "📥 {module}: defined {defined} symbol(s), merged {merged}, skipped {skipped}, {unresolved} unresolved type name(s).",
                request.ModuleName, defined, merged, skipped.Count, unresolvedTypeNames.Count);
        }

        return Task.FromResult(new DefineSymbolsResult
        {
            Defined = defined,
            Skipped = skipped,
            UnresolvedTypeNames = [.. unresolvedTypeNames],
            MergedDefinitions = merged,
        });
    }
}
