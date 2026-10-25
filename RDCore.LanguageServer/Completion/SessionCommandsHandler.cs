using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Services;

namespace RDCore.LanguageServer.Completion;

/// <summary>
/// Handles <c>rdcore/session/commands</c>: registers the verbs of the client's own commands, which <c>rdcore/session/completion</c> then completes as they are
/// typed. A client that completes with the language server registers with it: the capability is <see cref="SessionCompletion"/>'s.
/// </summary>
internal sealed class SessionCommandsHandler(IClientVerbRegistry verbs, IPlatformClientCapabilitiesService clientCapabilities)
    : RDCoreRequestHandler<SessionCommandsParams, SessionCommandsResult>
{
    /// <summary>JSON-RPC 2.0 "Invalid Request".</summary>
    private const int InvalidRequestCode = -32600;

    protected override Task<SessionCommandsResult> HandleAsync(SessionCommandsParams request, CancellationToken token)
    {
        if (!clientCapabilities.Expects(capabilities => capabilities.SessionCompletion))
        {
            throw new RpcErrorException(InvalidRequestCode, error: null!,
                $"The client did not advertise the '{nameof(SessionCompletion)}' platform capability.");
        }

        return Task.FromResult(new SessionCommandsResult { Registered = verbs.Register(request.Verbs) });
    }
}
