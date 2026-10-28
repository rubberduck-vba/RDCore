using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using Models = OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Services;

namespace RDCore.LanguageServer.SemanticTokens;

/// <summary>
/// Handles <c>rdcore/session/semanticTokens</c>: the tokens of code fragments the client supplies, with the legend and the encoding of
/// <c>textDocument/semanticTokens/full</c> (<see cref="SemanticTokensHandler"/>).
/// </summary>
internal sealed class SessionSemanticTokensHandler(ISemanticTokensService tokens, IPlatformClientCapabilitiesService clientCapabilities)
    : RDCoreRequestHandler<SessionSemanticTokensParams, SessionSemanticTokensResult>
{
    /// <summary>JSON-RPC 2.0 "Invalid Request".</summary>
    private const int InvalidRequestCode = -32600;

    protected override async Task<SessionSemanticTokensResult> HandleAsync(SessionSemanticTokensParams request, CancellationToken token)
    {
        if (!clientCapabilities.Expects(capabilities => capabilities.SessionSemanticTokens))
        {
            throw new RpcErrorException(InvalidRequestCode, error: null!,
                $"The client did not advertise the '{nameof(SessionSemanticTokens)}' platform capability.");
        }

        var fragments = await tokens.GetFragmentsAsync(request.ModuleName, request.Source, request.Fragments, token);
        return new SessionSemanticTokensResult { Fragments = [.. fragments.Select(Encoded)] };
    }

    // five integers a token, each line and start relative to the token before it: the encoding of textDocument/semanticTokens/full.
    internal static Models.SemanticTokens Encoded(IReadOnlyList<SemanticToken> tokens)
    {
        var data = new List<int>(tokens.Count * 5);
        (int Line, int Character) previous = (0, 0);
        foreach (var token in tokens.OrderBy(token => token.Line).ThenBy(token => token.Character))
        {
            var deltaLine = token.Line - previous.Line;
            data.AddRange([deltaLine, deltaLine == 0 ? token.Character - previous.Character : token.Character, token.Length, token.Type, token.Modifiers]);
            previous = (token.Line, token.Character);
        }

        return new Models.SemanticTokens { Data = [.. data] };
    }
}
