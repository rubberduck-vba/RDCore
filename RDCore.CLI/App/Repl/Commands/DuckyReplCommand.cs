using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.External.Interaction;
using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Platform.Commands;
using RDCore.SDK.Semantics.Refactoring;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>DUCKY</c>: proposes what can be done about the program, or one of its lines, and does what is chosen - the code actions an editor offers in a light bulb.
/// </summary>
/// <remarks>
/// <para>
/// <c>DUCKY</c> asks the platform for the actions of the whole program, and <c>DUCKY 20</c> for those of line 20 (<c>textDocument/codeAction</c>): the fixes of its
/// diagnostics, and a rename of each name it declares. The actions are listed by number, and the one whose number is typed is done; <kbd>Esc</kbd> does none.
/// </para>
/// <para>
/// An action that edits the program edits the buffer, as its edit says. One that renames a name to a name the user chooses (<see cref="RefactoringCommands.Rename"/>)
/// asks for it, with the name it has; <kbd>Esc</kbd> cancels, and the platform renames the symbol (<c>textDocument/rename</c>), wherever the program refers to it
/// and nowhere else. An edit of a document the shell does not hold is not made, and neither is an edit of a program that changed since the platform answered.
/// </para>
/// <para>
/// The command is named after the duck a programmer explains the code to, which is the one who has the ideas: <c>FIX</c> would be the name of a function of the
/// language.
/// </para>
/// </remarks>
/// <param name="interaction">Where the choice and the new name are asked: the dialogs of the shell.</param>
internal sealed class DuckyReplCommand(ReplUserInteraction interaction) : IReplCommand, IReplCommandDescriptor
{
    public string Name => ReplCommandNames.Ducky;
    public IReadOnlyList<string> Aliases => [];
    public string Summary => Resources.Repl_Ducky_Summary;

    // DUCKY, and DUCKY <line>.
    public VerbDescriptor Descriptor => new()
    {
        Name = Name,
        Summary = Summary,
        Forms =
        [
            [],
            [new VerbArgument { Name = "line", Kind = VerbArgumentKind.LineNumber }],
        ],
    };

    public async Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        var program = context.Program;
        if (program.IsEmpty)
        {
            return ReplCommandResult.Continue;
        }

        var text = arguments.Trim();
        int? number = null;
        if (text.Length > 0)
        {
            if (!int.TryParse(text, out var parsed) || program.IndexOf(parsed) < 0)
            {
                context.Console.WriteMessage(MessageKind.Error, Resources.Repl_UndefinedLine, text);
                return ReplCommandResult.Continue;
            }

            number = parsed;
        }

        if (!await context.Document.EnsureOpenAsync(token) || context.Document.Uri is not { } document)
        {
            context.Console.WriteMessage(MessageKind.Warning, Resources.Repl_Ducky_NotADocument);
            return ReplCommandResult.Continue;
        }

        // the text of the document is the buffer's lines, one per line of the text, in number order.
        var lines = program.Lines().ToArray();
        var range = number is { } line
            ? new LspRange(program.IndexOf(line), 0, program.IndexOf(line), $"{line} {lines[program.IndexOf(line)].Statement}".Length)
            : new LspRange(0, 0, lines.Length, 0);

        var diagnostics = (await context.Platform.PullDiagnosticsAsync(document, token))
            .Where(diagnostic => diagnostic.Range.Start.Line <= range.End.Line && diagnostic.Range.End.Line >= range.Start.Line)
            .ToList();
        var actions = await context.Platform.GetCodeActionsAsync(document, range, diagnostics, token);
        if (actions.Count == 0)
        {
            context.Console.WriteLine(Resources.Repl_Ducky_NothingToDo);
            context.Console.WriteLine();
            return ReplCommandResult.Continue;
        }

        for (var index = 0; index < actions.Count; index++)
        {
            var at = LineOf(actions[index]) is { } actionLine && actionLine < lines.Length ? lines[actionLine].Number.ToString() : string.Empty;
            context.Console.WriteLine($"{index + 1,3}. {at,6}  {actions[index].Title}");
        }

        context.Console.WriteLine();
        if (Choose(actions.Count, token) is not { } chosen)
        {
            return ReplCommandResult.Continue;
        }

        var action = actions[chosen];
        if (action.Edit is { } edit)
        {
            await ApplyAsync(context, document, edit, action.Title, token);
        }
        else if (action.Command is { Name: RefactoringCommands.Rename } rename && PositionOf(rename) is { } position)
        {
            await RenameAsync(context, document, position, token);
        }
        else
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_Ducky_Refused, string.Format(Resources.Repl_Ducky_UnknownCommand, action.Command?.Name));
        }

        return ReplCommandResult.Continue;
    }

    // the number of the action typed, from 1; none when the user cancels. A number that is no action's is asked again.
    private int? Choose(int count, CancellationToken token)
    {
        while (true)
        {
            var answer = interaction.Prompt(new InputBoxRequest(string.Format(Resources.Repl_Ducky_Choose_Prompt, count), Resources.Repl_Ducky_Choose_Title), token)?.Trim();
            if (string.IsNullOrEmpty(answer))
            {
                return null;
            }

            if (int.TryParse(answer, out var chosen) && chosen >= 1 && chosen <= count)
            {
                return chosen - 1;
            }
        }
    }

    // asks for the new name, with the one the symbol is declared with; the platform renames it, or says why it does not.
    private async Task RenameAsync(ReplCommandContext context, Uri document, Position position, CancellationToken token)
    {
        var prepared = await context.Platform.PrepareRenameAsync(document, position, token);
        if (prepared.Value is not { } target)
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_Ducky_Refused, prepared.Refusal);
            return;
        }

        var newName = interaction.Prompt(new InputBoxRequest(string.Format(Resources.Repl_Ducky_Rename_Prompt, target.Placeholder), Resources.Repl_Ducky_Rename_Title,
            target.Placeholder), token)?.Trim();
        if (string.IsNullOrEmpty(newName) || newName == target.Placeholder)
        {
            return;
        }

        var renamed = await context.Platform.RenameAsync(document, position, newName, token);
        if (renamed.Value is not { } edit)
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_Ducky_Refused, renamed.Refusal);
            return;
        }

        await ApplyAsync(context, document, edit, RefactoringTitles.RenameTo(target.Placeholder, newName), token);
    }

    // an edit is made to the buffer whole, or not at all: it edits the program as the platform had it, and no document the shell does not hold.
    private static async Task ApplyAsync(ReplCommandContext context, Uri document, WorkspaceEdit edit, string title, CancellationToken token)
    {
        var changes = (edit.DocumentChanges ?? [])
            .Where(change => change.IsTextDocumentEdit)
            .Select(change => (Uri: change.TextDocumentEdit!.TextDocument.Uri.ToUri(), change.TextDocumentEdit.TextDocument.Version, Edits: change.TextDocumentEdit.Edits.ToList()))
            .Concat((edit.Changes ?? new Dictionary<DocumentUri, IEnumerable<TextEdit>>()).Select(change => (Uri: change.Key.ToUri(), Version: (int?)null, Edits: change.Value.ToList())))
            .ToList();

        var others = changes.Where(change => change.Uri.AbsoluteUri != document.AbsoluteUri).Select(change => Path.GetFileName(change.Uri.LocalPath)).Distinct().ToList();
        if (others.Count > 0)
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_Ducky_Refused, string.Format(Resources.Repl_Ducky_OtherDocuments, string.Join(", ", others)));
            return;
        }

        if (changes.Any(change => change.Version is { } version && version != context.Program.Version))
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_Ducky_Refused, Resources.Repl_Ducky_Stale);
            return;
        }

        var edits = changes.SelectMany(change => change.Edits).ToList();
        if (edits.Count == 0)
        {
            return;
        }

        context.Program.Load(ReplTextEdits.Apply(context.Program.ToSourceText(), edits));
        await context.Document.SynchronizeAsync(token);
        context.Console.WriteMessage(MessageKind.Success, string.Format(Resources.Repl_Ducky_Done, title, edits.Count));
    }

    // the line of the text an action is about: its diagnostic's, or the position its command names.
    private static int? LineOf(CodeAction action)
        => action.Diagnostics?.FirstOrDefault()?.Range.Start.Line ?? (action.Command is { } command ? PositionOf(command)?.Line : null);

    // the position the rename command names: its second argument.
    private static Position? PositionOf(Command command)
        => command.Arguments is { Count: >= 2 } arguments && arguments[1] is JObject position
            && position.Value<int?>("line") is { } line && position.Value<int?>("character") is { } character
            ? new Position(line, character)
            : null;
}

/// <summary>
/// Makes the edits of a text (<strong>LSP 3.17</strong> <c>TextEdit</c>): each replaces a range of the text as it was, so they are made from the last to the first.
/// </summary>
internal static class ReplTextEdits
{
    /// <summary>The text, edited.</summary>
    /// <param name="text">The text the edits are of.</param>
    /// <param name="edits">The edits; no two of them overlap.</param>
    public static string Apply(string text, IEnumerable<TextEdit> edits)
    {
        var starts = new List<int> { 0 };
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n')
            {
                starts.Add(index + 1);
            }
        }

        int OffsetOf(Position position)
            => Math.Min(text.Length, (position.Line < starts.Count ? starts[position.Line] : text.Length) + position.Character);

        var edited = text;
        foreach (var edit in edits.OrderByDescending(edit => edit.Range.Start.Line).ThenByDescending(edit => edit.Range.Start.Character))
        {
            var start = OffsetOf(edit.Range.Start);
            var end = OffsetOf(edit.Range.End);
            edited = edited[..start] + edit.NewText + edited[end..];
        }

        return edited;
    }
}
