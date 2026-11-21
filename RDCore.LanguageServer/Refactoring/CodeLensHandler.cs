using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Services.Refactoring;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server.ProtocolExtensions;
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
/// The command of a lens is one a client runs itself. A lens that leads to a single place goes to it (<see cref="RefactoringCommands.GoToLocation"/>): the
/// declaration of the member an implementation implements, titled with it (<see cref="RefactoringTitles.Implements"/>), and the one implementation of a
/// member. Any other lens shows its places (<see cref="RefactoringCommands.ShowReferences"/>): the references, titled with their count
/// (<see cref="RefactoringTitles.References"/>), even when there is one or none; and the implementations, as <c>textDocument/implementation</c> finds them,
/// titled with their count (<see cref="RefactoringTitles.Implementations"/>).
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
                Command = GoTo(RefactoringTitles.Implements(implemented.Interface, implemented.Member), implemented.Declaration),
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
            var title = RefactoringTitles.Implementations(implementations.Count);
            return request with
            {
                Command = implementations.Count == 1 ? GoTo(title, implementations[0]) : Show(document, position, title, implementations),
            };
        }

        var references = composed.LensReferences(document.ToUri(), position);
        return request with { Command = Show(document, position, RefactoringTitles.References(references.Count), references) };
    }

    // the command that shows a list of places: the arguments of VS Code's editor.action.showReferences.
    private static Command Show(DocumentUri document, Position position, string title, IEnumerable<Location> locations)
        => new ShowReferencesCommandArguments(document.ToString(), position.ToSourcePosition(), [.. locations.Select(place => place.ToCommandLocation())])
            .ToCommand(title);

    // the command that takes the user to one place: the URI of its document and its range.
    private static Command GoTo(string title, Location location)
        => new GoToLocationCommandArguments(location.Uri.ToString(), location.Range.ToSourceRange()).ToCommand(title);

    protected override CodeLensRegistrationOptions CreateRegistrationOptions(CodeLensCapability capability, ClientCapabilities clientCapabilities)
        => new() { ResolveProvider = true };
}
