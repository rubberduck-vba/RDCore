using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Platform.Commands;

namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>FOLD</c>: folds the block that starts at a line of the program in a listing, or unfolds it.
/// </summary>
/// <remarks>
/// <c>FOLD 40</c> folds the <c>If</c>, <c>Select Case</c>, loop or <c>With</c> that starts at line 40: <c>LIST</c> shows its first line, faint, and none of the
/// lines after it to its <c>End</c>. <c>FOLD 40</c> again unfolds it. What folds is what the language server says does (<c>textDocument/foldingRange</c>), the
/// blocks of the program; a line that starts none is an error.
/// </remarks>
internal sealed class FoldReplCommand : IReplCommand, IReplCommandDescriptor
{
    public string Name => ReplCommandNames.Fold;
    public IReadOnlyList<string> Aliases => [];
    public string Summary => Resources.Repl_Fold_Summary;

    public VerbDescriptor Descriptor => new()
    {
        Name = Name,
        Summary = Summary,
        Forms = [[new VerbArgument { Name = "line", Kind = VerbArgumentKind.LineNumber }]],
    };

    public async Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        if (!int.TryParse(arguments.Trim(), out var number))
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_SyntaxError, arguments);
            return ReplCommandResult.Continue;
        }

        var index = context.Program.IndexOf(number);
        if (index < 0)
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_UndefinedLine, number.ToString());
            return ReplCommandResult.Continue;
        }

        var blocks = await ListReplCommand.BlockFoldsAsync(context, token);
        if (!blocks.Any(block => block.StartLine == index))
        {
            context.Console.WriteMessage(MessageKind.Error, string.Format(Resources.Repl_Fold_None, number));
            return ReplCommandResult.Continue;
        }

        var folded = context.Folds.Toggle(number);
        context.Console.WriteLine(string.Format(folded ? Resources.Repl_Fold_Folded : Resources.Repl_Fold_Unfolded, number));
        return ReplCommandResult.Continue;
    }
}
