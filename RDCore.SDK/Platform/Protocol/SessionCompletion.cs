using MediatR;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Client;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// Request for <c>rdcore/session/completion</c>: a client asks the language server what can be written at a position of a module it supplies - the completion
/// list of a line typed at a prompt.
/// </summary>
/// <remarks>
/// The counterpart to <see cref="AnalyzeSessionParams"/> for completion: an editor asks LSP's own <c>textDocument/completion</c> of a document it has open, and a
/// shell has no document for the line it is typing - the line is not in the program until it is entered, and an immediate statement never is. The list is the one
/// <c>textDocument/completion</c> answers with, of the module as it is supplied: nothing of it is sent to the environment host.
/// </remarks>
[Method(RDCorePlatformProtocol.SessionCompletion, Direction.ClientToServer)]
public record class SessionCompletionParams : IRequest, IRequest<CompletionList>
{
    /// <summary>
    /// The complete source of the module, the line that is being typed in it.
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// The module's programmatic name: the module of the workspace by that name is the one the source stands for, and the workspace's other modules are what its
    /// names are bound among.
    /// </summary>
    public string ModuleName { get; init; } = string.Empty;

    /// <summary>
    /// The zero-based line of <see cref="Source"/> the cursor is on.
    /// </summary>
    public int Line { get; init; }

    /// <summary>
    /// The zero-based character of <see cref="Line"/> the cursor is at, in UTF-16 code units: what is before it is what the list completes.
    /// </summary>
    public int Character { get; init; }
}
