using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Refactoring;

namespace RDCore.LanguageServer.Debugging;

/// <summary>
/// Serves <c>textDocument/inlineValue</c> (<strong>LSP 3.17</strong> §Inline Value Request): while the program is stopped, the names of variables in the
/// range a client shows, whose values it looks up in the stopped frame of the debug adapter and shows beside them
/// (<see cref="RenameWorkspace.InlineValues"/>).
/// </summary>
/// <remarks>
/// The names are those of the procedure the program is stopped in, from its first line to the line of the stop: a local variable, a parameter, or a module's
/// variable. A request whose stop is in no procedure of the document is answered with no names.
/// </remarks>
internal sealed class InlineValueHandler(IRenameService workspace) : InlineValuesHandlerBase
{
    public override async Task<Container<InlineValueBase>?> Handle(InlineValueParams request, CancellationToken cancellationToken)
        => await workspace.ComposeAsync(cancellationToken) is { } composed
            ? new Container<InlineValueBase>(composed.InlineValues(request.TextDocument.Uri.ToUri(), request.Range, request.Context.StoppedLocation))
            : new Container<InlineValueBase>();

    protected override InlineValueRegistrationOptions CreateRegistrationOptions(InlineValueClientCapabilities capability, ClientCapabilities clientCapabilities)
        => new();
}
