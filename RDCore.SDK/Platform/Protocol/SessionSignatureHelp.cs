using MediatR;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Client;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// Request for <c>rdcore/session/signatureHelp</c>: a client asks the language server what the call being written at a position of a module it supplies takes -
/// the signature help of a line typed at a prompt.
/// </summary>
/// <remarks>
/// The counterpart to <see cref="SessionCompletionParams"/> for a signature help: an editor asks LSP's own <c>textDocument/signatureHelp</c> of a document it has
/// open, and a shell has no document for the line it is typing. The answer is the one <c>textDocument/signatureHelp</c> answers with, of the module as it is
/// supplied, or <see langword="null"/> where no call is being written.
/// </remarks>
[Method(RDCorePlatformProtocol.SessionSignatureHelp, Direction.ClientToServer)]
public record class SessionSignatureHelpParams : IRequest, IRequest<SignatureHelp?>
{
    /// <summary>
    /// The complete source of the module, the line that is being typed in it.
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// The module's programmatic name: the module of the workspace by that name is the one the source stands for.
    /// </summary>
    public string ModuleName { get; init; } = string.Empty;

    /// <summary>
    /// The zero-based line of <see cref="Source"/> the cursor is on.
    /// </summary>
    public int Line { get; init; }

    /// <summary>
    /// The zero-based character of <see cref="Line"/> the cursor is at, in UTF-16 code units: what is before it is the call being written.
    /// </summary>
    public int Character { get; init; }
}
