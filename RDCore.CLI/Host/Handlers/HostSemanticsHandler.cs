using RDCore.SDK.Platform.Protocol;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/semantics</c>: answers with what the semantic analysis pass found out about the code the session holds.
/// </summary>
/// <remarks>
/// The pass runs when a module's code is loaded (<c>ModuleLoader</c>), so this only reads what it left: the model of a module is as of the last time it was
/// defined. The host says what is; what is worth saying is for a diagnostics extension.
/// </remarks>
internal sealed class HostSemanticsHandler(IEnvironmentSessionProvider sessionProvider)
    : RDCoreRequestHandler<HostSemanticsParams, HostSemanticsResult>
{
    protected override Task<HostSemanticsResult> HandleAsync(HostSemanticsParams request, CancellationToken token)
    {
        if (!sessionProvider.IsComposed)
        {
            // not an error: the session is composed on the LSP initialize handshake, so a request can arrive first.
            return Task.FromResult(new HostSemanticsResult { Json = PlatformJson.Serialize(new SemanticsPayload([])) });
        }

        var models = sessionProvider.Image.Semantics.All
            .Where(model => request.ModuleName.Length == 0
                || string.Equals(model.Module.Fragment.TrimStart('#'), request.ModuleName, StringComparison.OrdinalIgnoreCase))
            .Select(ModuleSemanticsDto.From);

        return Task.FromResult(new HostSemanticsResult { Json = PlatformJson.Serialize(new SemanticsPayload([.. models])) });
    }
}
