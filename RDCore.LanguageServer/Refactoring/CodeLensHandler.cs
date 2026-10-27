using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Semantics.Refactoring;

namespace RDCore.LanguageServer.Refactoring;

/// <summary>
/// Serves <c>textDocument/codeLens</c> and <c>codeLens/resolve</c> (<strong>LSP 3.17</strong> §Code Lens Request, §Code Lens Resolve Request): above the name of
/// each member a document declares (<see cref="SymbolLenses"/>), how many names refer to it.
/// </summary>
/// <remarks>
/// <para>
/// A lens is answered without its command, which <c>codeLens/resolve</c> adds: the references of a member are found in every document of the workspace, and a
/// client resolves the lenses it shows only. The data of a lens is the document and the position of the member's name, from which its references are found
/// when it is resolved, and not when it was answered.
/// </para>
/// <para>
/// The command of a resolved lens is <see cref="RefactoringCommands.ShowReferences"/>, which a client runs itself, its title the count
/// (<see cref="RefactoringTitles.References"/>).
/// </para>
/// </remarks>
internal sealed class CodeLensHandler(IRenameService workspace) : CodeLensHandlerBase
{
    public override async Task<CodeLensContainer?> Handle(CodeLensParams request, CancellationToken cancellationToken)
    {
        if (await workspace.ComposeAsync(cancellationToken) is not { } composed)
        {
            return new CodeLensContainer();
        }

        var document = request.TextDocument.Uri;
        return new CodeLensContainer(composed.Lenses(document.ToUri()).Select(lens => new CodeLens
        {
            Range = lens.Range,
            Data = new JObject
            {
                ["uri"] = document.ToString(),
                ["line"] = lens.Range.Start.Line,
                ["character"] = lens.Range.Start.Character,
            },
        }));
    }

    public override async Task<CodeLens> Handle(CodeLens request, CancellationToken cancellationToken)
    {
        if (request.Data is not JObject { } data
            || data.Value<string>("uri") is not { } uri
            || await workspace.ComposeAsync(cancellationToken) is not { } composed)
        {
            return request;
        }

        var document = DocumentUri.From(uri);
        var position = new Position(data.Value<int>("line"), data.Value<int>("character"));
        var references = composed.LensReferences(document.ToUri(), position);
        var title = RefactoringTitles.References(references.Count);
        return request with
        {
            Command = new Command
            {
                Name = RefactoringCommands.ShowReferences,
                Title = title,
                Arguments = new JArray(document.ToString(), JToken.FromObject(new { line = position.Line, character = position.Character }),
                    new JArray(references.Select(reference => JToken.FromObject(new
                    {
                        uri = reference.Uri.ToString(),
                        range = new
                        {
                            start = new { line = reference.Range.Start.Line, character = reference.Range.Start.Character },
                            end = new { line = reference.Range.End.Line, character = reference.Range.End.Character },
                        },
                    })))),
            },
        };
    }

    protected override CodeLensRegistrationOptions CreateRegistrationOptions(CodeLensCapability capability, ClientCapabilities clientCapabilities)
        => new() { ResolveProvider = true };
}
