using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Services;

namespace RDCore.LanguageServer.Completion;

/// <summary>
/// Handles <c>rdcore/session/signatureHelp</c>: what the call being written at a position of a module the client supplies takes, with the signature help
/// <c>textDocument/signatureHelp</c> answers with.
/// </summary>
internal sealed class SessionSignatureHelpHandler(ICompletionService completion, IPlatformClientCapabilitiesService clientCapabilities)
    : RDCoreRequestHandler<SessionSignatureHelpParams, SignatureHelp?>
{
    /// <summary>JSON-RPC 2.0 "Invalid Request".</summary>
    private const int InvalidRequestCode = -32600;

    protected override Task<SignatureHelp?> HandleAsync(SessionSignatureHelpParams request, CancellationToken token)
    {
        if (!clientCapabilities.Expects(capabilities => capabilities.SessionSignatureHelp))
        {
            throw new RpcErrorException(InvalidRequestCode, error: null!,
                $"The client did not advertise the '{nameof(SessionSignatureHelp)}' platform capability.");
        }

        return completion.SignatureHelpFragmentAsync(request.ModuleName, request.Source, new Position(request.Line, request.Character), token);
    }
}
