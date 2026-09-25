using RDCore.SDK.ConsoleIO.Model;

namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>LIST</c>: renders the program buffer back, in line-number order.
/// </summary>
/// <remarks>
/// The argument is a BASIC line range: <c>LIST</c>, <c>LIST 100</c>, <c>LIST 100-200</c>,
/// <c>LIST 100-</c> (from there on) or <c>LIST -200</c> (up to there).
/// </remarks>
internal sealed class ListReplCommand : IReplCommand
{
    public string Name => "LIST";
    public IReadOnlyList<string> Aliases => [];
    public string Summary => Resources.Repl_List_Summary;

    public Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        if (!TryParseRange(arguments, out var from, out var to))
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_SyntaxError, arguments);
            return Task.FromResult(ReplCommandResult.Continue);
        }

        // the numbers are right-aligned to the widest one in the listing, as a BASIC listing is, so
        // the statements line up whatever the numbering.
        var lines = context.Program.Lines(from, to).ToArray();
        var width = lines.Length == 0 ? 0 : lines.Max(line => line.Number).ToString().Length;
        foreach (var (number, statement) in lines)
        {
            context.Console.WriteLine($"{number.ToString().PadLeft(width)} {statement}");
        }

        context.Console.WriteLine();
        return Task.FromResult(ReplCommandResult.Continue);
    }

    /// <summary>
    /// Parses a BASIC line range.
    /// </summary>
    /// <param name="arguments">The argument text, which may be empty.</param>
    /// <param name="from">The first line number to list, or <c>null</c> for the start of the buffer.</param>
    /// <param name="to">The last line number to list, or <c>null</c> for the end of the buffer.</param>
    /// <returns><c>false</c> if the argument is not a line range at all.</returns>
    internal static bool TryParseRange(string arguments, out int? from, out int? to)
    {
        from = null;
        to = null;

        var text = arguments.Trim();
        if (text.Length == 0)
        {
            return true;
        }

        var dash = text.IndexOf('-');
        if (dash < 0)
        {
            // a bare number lists that one line: an empty range, not an open-ended one.
            if (!int.TryParse(text, out var single))
            {
                return false;
            }

            from = single;
            to = single;
            return true;
        }

        var start = text[..dash].Trim();
        var end = text[(dash + 1)..].Trim();
        if (start.Length > 0)
        {
            if (!int.TryParse(start, out var parsedStart))
            {
                return false;
            }
            from = parsedStart;
        }

        if (end.Length > 0)
        {
            if (!int.TryParse(end, out var parsedEnd))
            {
                return false;
            }
            to = parsedEnd;
        }

        return start.Length > 0 || end.Length > 0;
    }
}
