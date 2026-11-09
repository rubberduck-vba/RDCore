using RDCore.Runtime.Support.Host;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/semantics</c>: answers with what the semantic analysis pass found out about the code the session holds.
/// </summary>
/// <remarks>
/// The pass runs when a module's code is loaded (<c>ModuleLoader</c>), so this only reads what it left: the model of a module is as of the last time it was
/// defined. The host says what is; what is worth saying is for a diagnostics extension.
/// <para>
/// The static facts are there when the module is loaded and are answered at once. The facts that come of evaluating the code take longer, and are waited for only
/// by a request that asks for them (<see cref="HostSemanticsParams.Phase"/>): a request for the static phase does not wait behind them.
/// </para>
/// </remarks>
internal sealed class HostSemanticsHandler(IEnvironmentSessionProvider sessionProvider)
    : RDCoreRequestHandler<HostSemanticsParams, HostSemanticsResult>
{
    protected override async Task<HostSemanticsResult> HandleAsync(HostSemanticsParams request, CancellationToken token)
    {
        if (!sessionProvider.IsComposed)
        {
            // not an error: the session is composed on the LSP initialize handshake, so a request can arrive first.
            return new HostSemanticsResult { Json = PlatformJson.Serialize(new SemanticsPayload([])) };
        }

        var store = sessionProvider.Image.Semantics;
        if (request.Phase.HasFlag(AnalysisPhase.Runtime))
        {
            if (request.ModuleName.Length == 0)
            {
                await store.WhenAllEvaluatedAsync(token);
            }
            else
            {
                foreach (var module in Named(request.ModuleName))
                {
                    await store.WhenEvaluatedAsync(module, token);
                }
            }
        }

        var models = Named(request.ModuleName)
            .Select(module => store.TryGet(module, out var model) ? model : null)
            .OfType<SDK.Semantics.ModuleSemanticModel>()
            .Select(ModuleSemanticsDto.From);

        return new HostSemanticsResult { Json = PlatformJson.Serialize(new SemanticsPayload([.. models])) };
    }

    // the modules asked for: every one the host has a model of, when none is named.
    private IEnumerable<Uri> Named(string moduleName)
        => sessionProvider.Image.Semantics.All
            .Where(model => moduleName.Length == 0 || string.Equals(SymbolUri.ModuleNameOf(model.Module), moduleName, StringComparison.OrdinalIgnoreCase))
            .Select(model => model.Module)
            .ToList();
}
