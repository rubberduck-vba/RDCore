using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using RDCore.LanguageServer.Symbols;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Services;

namespace RDCore.LanguageServer.Runtime;

/// <summary>
/// Handles <c>rdcore/session/discard</c>: takes a module the client supplied out of the platform's runtime session, by asking the component that
/// owns it.
/// </summary>
/// <remarks>
/// The module is addressed by name, as it was when it was run: the language server holds no copy of what the host defined, and nothing about the module
/// but its name is needed to take it out.
/// </remarks>
internal sealed class SessionDiscardHandler(
    ISymbolSyncService symbols,
    IPlatformClientCapabilitiesService clientCapabilities)
    : RDCoreRequestHandler<DiscardSessionParams, DiscardSessionResult>
{
    /// <summary>JSON-RPC 2.0 "Invalid Request".</summary>
    private const int InvalidRequestCode = -32600;

    protected override async Task<DiscardSessionResult> HandleAsync(DiscardSessionParams request, CancellationToken token)
    {
        if (!clientCapabilities.Expects(capabilities => capabilities.SessionDiscard))
        {
            throw new RpcErrorException(InvalidRequestCode, error: null!,
                $"The client did not advertise the '{nameof(SessionDiscard)}' platform capability.");
        }

        return await symbols.DiscardModuleAsync(request.ModuleName, token, request.EndProgram);
    }
}
