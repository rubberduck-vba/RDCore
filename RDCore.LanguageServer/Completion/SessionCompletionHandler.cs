using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Services;

namespace RDCore.LanguageServer.Completion;

/// <summary>
/// Handles <c>rdcore/session/completion</c>: what can be written at a position of a module the client supplies, with the list <c>textDocument/completion</c>
/// answers with.
/// </summary>
internal sealed class SessionCompletionHandler(ICompletionService completion, IPlatformClientCapabilitiesService clientCapabilities)
    : RDCoreRequestHandler<SessionCompletionParams, CompletionList>
{
    /// <summary>JSON-RPC 2.0 "Invalid Request".</summary>
    private const int InvalidRequestCode = -32600;

    protected override Task<CompletionList> HandleAsync(SessionCompletionParams request, CancellationToken token)
    {
        if (!clientCapabilities.Expects(capabilities => capabilities.SessionCompletion))
        {
            throw new RpcErrorException(InvalidRequestCode, error: null!,
                $"The client did not advertise the '{nameof(SessionCompletion)}' platform capability.");
        }

        return request.Command is { } command
            ? completion.CompleteCommandAsync(request.ModuleName, request.Source, command, token)
            : completion.CompleteFragmentAsync(request.ModuleName, request.Source, new Position(request.Line, request.Character), token);
    }
}
