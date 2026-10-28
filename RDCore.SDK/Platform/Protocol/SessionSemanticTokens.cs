using MediatR;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Client;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// Request for <c>rdcore/session/semanticTokens</c>: a client asks the language server what the tokens of code fragments it supplies are - the colours of a
/// condition, or of a line typed at a prompt.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart to <see cref="SessionCompletionParams"/> for semantic tokens: an editor asks LSP's own <c>textDocument/semanticTokens/full</c> of a document it
/// has open, and a shell has no document for the condition of a breakpoint. Each fragment is answered with the tokens of its text, with the legend and the encoding
/// of <c>textDocument/semanticTokens/full</c> - the positions are in the fragment, its first line being line 0 - so that a client colours it as it colours a
/// document.
/// </para>
/// <para>
/// The names in a fragment are told apart by what <see cref="Source"/> declares and the workspace's class modules are: a constant is read-only, and the name of a type
/// is a type's, as they are in the module's own listing. Many fragments are asked in one request, so that a listing of them costs one round trip.
/// </para>
/// </remarks>
[Method(RDCorePlatformProtocol.SessionSemanticTokens, Direction.ClientToServer)]
public record class SessionSemanticTokensParams : IRequest, IRequest<SessionSemanticTokensResult>
{
    /// <summary>
    /// The complete source of the module the fragments are code of: what its names are resolved against.
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// The module's programmatic name: the module of the workspace by that name is the one the source stands for.
    /// </summary>
    public string ModuleName { get; init; } = string.Empty;

    /// <summary>
    /// The fragments of code to tell the tokens of, each as it is written: the condition of a breakpoint is one, on one line.
    /// </summary>
    public IReadOnlyList<string> Fragments { get; init; } = [];
}

/// <summary>
/// The answer to <see cref="SessionSemanticTokensParams"/>.
/// </summary>
public record class SessionSemanticTokensResult
{
    /// <summary>
    /// The tokens of each fragment, in the order the fragments were asked: <see cref="SemanticTokens.Data"/> is encoded as <c>textDocument/semanticTokens/full</c>
    /// encodes it, relative to the fragment. A fragment with no token has an empty answer.
    /// </summary>
    public IReadOnlyList<SemanticTokens> Fragments { get; init; } = [];
}
