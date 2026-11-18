using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.LanguageServer.Refactoring;

/// <summary>
/// Serves <c>textDocument/linkedEditingRange</c> (<strong>LSP 3.17</strong> §Linked Editing Range): the places a client edits as one while the user types
/// the name of a local variable, a parameter or a line label, each place its procedure writes it (<see cref="RenameWorkspace.LinkedEditing"/>).
/// </summary>
/// <remarks>
/// A name of the module or of another module is named outside the procedure, and gets no ranges: a rename changes it everywhere. The answer carries no word
/// pattern, so a client checks the edited name with its own.
/// </remarks>
internal sealed class LinkedEditingRangeHandler(IRenameService workspace) : LinkedEditingRangeHandlerBase
{
    public override async Task<LinkedEditingRanges> Handle(LinkedEditingRangeParams request, CancellationToken cancellationToken)
        => await workspace.ComposeAsync(cancellationToken) is { } composed
            ? composed.LinkedEditing(request.TextDocument.Uri.ToUri(), request.Position)
            : new LinkedEditingRanges { Ranges = new Container<LspRange>() };

    protected override LinkedEditingRangeRegistrationOptions CreateRegistrationOptions(
        LinkedEditingRangeClientCapabilities capability, ClientCapabilities clientCapabilities)
        => new();
}
