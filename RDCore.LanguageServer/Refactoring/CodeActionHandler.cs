using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Semantics.Refactoring;

namespace RDCore.LanguageServer.Refactoring;

/// <summary>
/// Serves <c>textDocument/codeAction</c> (<strong>LSP 3.17</strong> §Code Action Request): what can be done about the diagnostics of a range of a document, and
/// what can be renamed in it.
/// </summary>
/// <remarks>
/// <para>
/// A diagnostic whose fix is a rename (<see cref="DiagnosticFixes"/>) is offered as quick fixes: a rename to each name its analyzer suggests that can be made -
/// its edits are the action's - and a rename to a name the user chooses, which is a command the client runs (<see cref="RefactoringCommands.Rename"/>). A name
/// the range declares, or the name at the position of an empty range, is offered as a refactoring that renames it, the same command.
/// </para>
/// <para>
/// Every action is complete as it is answered: nothing is left to resolve (<c>resolveProvider = false</c>).
/// </para>
/// </remarks>
internal sealed class CodeActionHandler(IRenameService rename) : CodeActionHandlerBase
{
    /// <summary>The kind of the action that renames a symbol: a refactoring (<c>refactor.rename</c>).</summary>
    public static readonly CodeActionKind RefactorRename = new("refactor.rename");

    public override async Task<CommandOrCodeActionContainer?> Handle(CodeActionParams request, CancellationToken cancellationToken)
    {
        var documentUri = request.TextDocument.Uri.ToUri();
        if (await rename.ComposeAsync(cancellationToken) is not { } workspace)
        {
            return new CommandOrCodeActionContainer();
        }

        var actions = new List<CodeAction>();
        var offered = new HashSet<Position>();
        foreach (var diagnostic in request.Context.Diagnostics)
        {
            if (DiagnosticFixes.Of(diagnostic)?.Rename is not { } fix || workspace.Prepare(documentUri, diagnostic.Range.Start).Value is not { } target)
            {
                continue;
            }

            foreach (var suggestion in fix.Suggestions)
            {
                if (workspace.Rename(documentUri, diagnostic.Range.Start, suggestion).Value is { } edit)
                {
                    actions.Add(new CodeAction
                    {
                        Title = RefactoringTitles.RenameTo(target.Placeholder, suggestion),
                        Kind = CodeActionKind.QuickFix,
                        Diagnostics = new Container<Diagnostic>(diagnostic),
                        Edit = edit,
                        IsPreferred = true,
                    });
                }
            }

            actions.Add(RenameCommand(documentUri, target.Placeholder, target.Range.Start, CodeActionKind.QuickFix, diagnostic));
            offered.Add(target.Range.Start);
        }

        foreach (var (name, position) in workspace.Renamable(documentUri, request.Range).Where(renamable => !offered.Contains(renamable.Position)))
        {
            actions.Add(RenameCommand(documentUri, name, position, RefactorRename, diagnostic: null));
        }

        // a client that asks for some kinds of actions only is answered those: a kind is asked for with the kinds under it.
        var only = request.Context.Only?.Select(kind => kind.ToString()).ToList();
        var answered = only is { Count: > 0 }
            ? actions.Where(action => only.Any(kind => action.Kind.ToString() == kind || action.Kind.ToString().StartsWith(kind + ".", StringComparison.Ordinal)))
            : actions;
        return new CommandOrCodeActionContainer(answered.Select(action => new CommandOrCodeAction(action)));
    }

    // the action that renames a symbol to a name the client asks its user for.
    private static CodeAction RenameCommand(DocumentUri document, string name, Position position, CodeActionKind kind, Diagnostic? diagnostic)
    {
        var title = RefactoringTitles.Rename(name);
        return new CodeAction
        {
            Title = title,
            Kind = kind,
            Diagnostics = diagnostic is null ? null : new Container<Diagnostic>(diagnostic),
            Command = new Command
            {
                Name = RefactoringCommands.Rename,
                Title = title,
                Arguments = new JArray(document.ToString(), JToken.FromObject(new { line = position.Line, character = position.Character })),
            },
        };
    }

    public override Task<CodeAction> Handle(CodeAction request, CancellationToken cancellationToken) => Task.FromResult(request);

    protected override CodeActionRegistrationOptions CreateRegistrationOptions(CodeActionCapability capability, ClientCapabilities clientCapabilities) => new()
    {
        CodeActionKinds = new Container<CodeActionKind>(CodeActionKind.QuickFix, RefactorRename),
        ResolveProvider = false,
    };
}
