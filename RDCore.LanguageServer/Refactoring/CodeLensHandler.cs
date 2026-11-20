using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Services.Refactoring;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.Runtime.LanguageCore.Refactoring;

namespace RDCore.LanguageServer.Refactoring;

/// <summary>
/// Serves <c>textDocument/codeLens</c> and <c>codeLens/resolve</c> (<strong>LSP 3.17</strong> §Code Lens Request, §Code Lens Resolve Request): above the name of
/// each member a document declares (<see cref="SymbolLenses"/>), how many names refer to it; above a member of a class other classes implement as an
/// interface, how many procedures implement it; and above a procedure that implements a member of an interface, which member (MS-VBAL §5.2.4.2).
/// </summary>
/// <remarks>
/// <para>
/// A lens that counts is answered without its command, which <c>codeLens/resolve</c> adds: the references and the implementations of a member are found
/// in every document of the workspace, and a client resolves the lenses it shows only. The data of such a lens is what it counts, the document and the
/// position of the member's name, from which they are found when it is resolved, and not when it was answered. The lens of an implementation is answered
/// with its command.
/// </para>
/// <para>
/// The command of each lens is <see cref="RefactoringCommands.ShowReferences"/>, which a client runs itself: with the references, titled with their count
/// (<see cref="RefactoringTitles.References"/>); with the implementations, as <c>textDocument/implementation</c> finds them, titled with their count
/// (<see cref="RefactoringTitles.Implementations"/>); or with the declaration of the member an implementation implements, titled with it
/// (<see cref="RefactoringTitles.Implements"/>).
/// </para>
/// </remarks>
internal sealed class CodeLensHandler(IRenameService workspace) : CodeLensHandlerBase
{
    private const string ImplementationsKind = "implementations";

    public override async Task<CodeLensContainer?> Handle(CodeLensParams request, CancellationToken cancellationToken)
    {
        if (await workspace.ComposeAsync(cancellationToken) is not { } composed)
        {
            return new CodeLensContainer();
        }

        var document = request.TextDocument.Uri;
        return new CodeLensContainer(composed.Lenses(document.ToUri()).Select(lens => lens.Implemented is { } implemented
            ? new CodeLens
            {
                Range = lens.Range,
                Command = Show(document, lens.Range.Start, RefactoringTitles.Implements(implemented.Interface, implemented.Member),
                    [implemented.Declaration]),
            }
            : new CodeLens
            {
                Range = lens.Range,
                Data = new JObject
                {
                    ["uri"] = document.ToString(),
                    ["line"] = lens.Range.Start.Line,
                    ["character"] = lens.Range.Start.Character,
                    ["kind"] = lens.Kind == SymbolLensKind.Implementations ? ImplementationsKind : null,
                },
            }));
    }

    public override async Task<CodeLens> Handle(CodeLens request, CancellationToken cancellationToken)
    {
        if (request.Command is not null
            || request.Data is not JObject { } data
            || data.Value<string>("uri") is not { } uri
            || await workspace.ComposeAsync(cancellationToken) is not { } composed)
        {
            return request;
        }

        var document = DocumentUri.From(uri);
        var position = new Position(data.Value<int>("line"), data.Value<int>("character"));
        if (data.Value<string>("kind") == ImplementationsKind)
        {
            var implementations = composed.LensImplementations(document.ToUri(), position);
            return request with { Command = Show(document, position, RefactoringTitles.Implementations(implementations.Count), implementations) };
        }

        var references = composed.LensReferences(document.ToUri(), position);
        return request with { Command = Show(document, position, RefactoringTitles.References(references.Count), references) };
    }

    // the command that shows a list of places: the arguments of VS Code's editor.action.showReferences.
    private static Command Show(DocumentUri document, Position position, string title, IEnumerable<Location> locations) => new()
    {
        Name = RefactoringCommands.ShowReferences,
        Title = title,
        Arguments = new JArray(document.ToString(), JToken.FromObject(new { line = position.Line, character = position.Character }),
            new JArray(locations.Select(location => JToken.FromObject(new
            {
                uri = location.Uri.ToString(),
                range = new
                {
                    start = new { line = location.Range.Start.Line, character = location.Range.Start.Character },
                    end = new { line = location.Range.End.Line, character = location.Range.End.Character },
                },
            })))),
    };

    protected override CodeLensRegistrationOptions CreateRegistrationOptions(CodeLensCapability capability, ClientCapabilities clientCapabilities)
        => new() { ResolveProvider = true };
}
