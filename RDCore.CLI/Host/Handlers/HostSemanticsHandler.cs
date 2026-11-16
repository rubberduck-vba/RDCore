using RDCore.Runtime.Execution;
using RDCore.Runtime.Support.Host;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Extensibility.Diagnostics;

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
/// <para>
/// A module asked for by its address (<see cref="HostSemanticsParams.ModuleUri"/>) is answered from the session of its project: the runtime session's, or the
/// analysis session of a project the host does not run (<see cref="IProjectAnalysisSessions"/>). One asked for by its name is a module of the runtime session.
/// </para>
/// </remarks>
internal sealed class HostSemanticsHandler(IEnvironmentSessionProvider sessionProvider, IProjectAnalysisSessions analyses)
    : RDCoreRequestHandler<HostSemanticsParams, HostSemanticsResult>
{
    protected override async Task<HostSemanticsResult> HandleAsync(HostSemanticsParams request, CancellationToken token)
    {
        if (ImageOf(request) is not { } image)
        {
            // not an error: the session is composed on the LSP initialize handshake, so a request can arrive first; and a project the host was not asked to
            // analyze has no model.
            return new HostSemanticsResult { Json = PlatformJson.Serialize(new SemanticsPayload([])) };
        }

        var store = image.Semantics;
        var asked = Asked(store, request);
        if (request.Phase.HasFlag(AnalysisPhase.Runtime))
        {
            if (request.ModuleUri is null && request.ModuleName.Length == 0)
            {
                await store.WhenAllEvaluatedAsync(token);
            }
            else
            {
                foreach (var module in asked)
                {
                    await store.WhenEvaluatedAsync(module, token);
                }
            }
        }

        var models = asked
            .Select(module => store.TryGet(module, out var model) ? model : null)
            .OfType<SDK.Semantics.ModuleSemanticModel>()
            .Select(ModuleSemanticsDto.From);

        return new HostSemanticsResult { Json = PlatformJson.Serialize(new SemanticsPayload([.. models])) };
    }

    // the code of the session the module asked for is in: that of the project its address is under, which the runtime session's root may be too.
    private ProgramImage? ImageOf(HostSemanticsParams request)
    {
        if (request.ModuleUri is { } moduleUri)
        {
            var root = SymbolUri.ProjectRootOf(moduleUri);
            if (analyses.TryGet(root, out var analysis))
            {
                return analysis.Image;
            }

            return sessionProvider.IsComposed && sessionProvider.WorkspaceRoot is { } runRoot && SymbolUri.SameRoot(runRoot, root) ? sessionProvider.Image : null;
        }

        return sessionProvider.IsComposed ? sessionProvider.Image : null;
    }

    // the modules asked for: the one at the address, the ones of the name, or every one the session has a model of, when neither is given.
    private static List<Uri> Asked(SemanticModelStore store, HostSemanticsParams request)
        => [.. store.All
            .Where(model => request.ModuleUri is { } moduleUri
                ? SymbolUri.SameRoot(model.Module, moduleUri)
                    && string.Equals(SymbolUri.ModuleNameOf(model.Module), SymbolUri.ModuleNameOf(moduleUri), StringComparison.OrdinalIgnoreCase)
                : request.ModuleName.Length == 0 || string.Equals(SymbolUri.ModuleNameOf(model.Module), request.ModuleName, StringComparison.OrdinalIgnoreCase))
            .Select(model => model.Module)];
}
