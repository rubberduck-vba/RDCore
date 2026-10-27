using RDCore.CLI.Themes;
using RDCore.SDK.ConsoleIO.Model;

namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>LIST</c>: renders the program buffer back, in line-number order.
/// </summary>
/// <remarks>
/// The argument is a BASIC line range: <c>LIST</c>, <c>LIST 100</c>, <c>LIST 100-200</c>,
/// <c>LIST 100-</c> (from there on) or <c>LIST -200</c> (up to there).
/// <para>
/// The margin has two lanes. The first, at the edge, marks a breakpoint; the second, next to the line, marks the line a block starts at - an <c>If</c>, a
/// <c>Select Case</c>, a loop, a <c>With</c> - which <c>FOLD</c> folds. A folded block is its first line, faint as ghost text and cut to the console's width, and none of the lines
/// after it, to its <c>End</c>.
/// </para>
/// </remarks>
internal sealed class ListReplCommand(IAppThemeService Themes) : IReplCommand
{
    public string Name => ReplCommandNames.List;
    public IReadOnlyList<string> Aliases => [];
    public string Summary => Resources.Repl_List_Summary;

    private static readonly string enabledGlyph = "●";
    private static readonly string disabledGlyph = "○";
    private static readonly string conditionalGlyph = "◆";
    private static readonly string disabledConditionalGlyph = "◈";

    // the marks of a block, as an emoji - two cells - where the console draws one, and in ASCII where it does not.
    private static readonly (string Open, string Folded) emojiFoldGlyphs = ("🔽", "🔼");
    private static readonly (string Open, string Folded) asciiFoldGlyphs = ("-", "+");


    public async Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        if (!TryParseRange(arguments, out var from, out var to))
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_SyntaxError, arguments);
            return ReplCommandResult.Continue;
        }

        // the numbers are right-aligned to the widest one in the listing, as a BASIC listing is, so
        // the statements line up whatever the numbering.
        var lines = context.Program.Lines(from, to).ToArray();
        var width = lines.Length == 0 ? 0 : lines.Max(line => line.Number).ToString().Length;
        var highlighted = lines.Length == 0 ? null : await TokensByLineAsync(context, token);
        var debugger = context.Debugger;

        // the blocks, by the index of the line each starts at; and the lines a folded one hides, which are the ones after its first, to its last.
        var blocks = new Dictionary<int, ReplFold>();
        foreach (var fold in lines.Length == 0 ? [] : await BlockFoldsAsync(context, token))
        {
            blocks.TryAdd(fold.StartLine, fold);
        }

        var hidden = new HashSet<int>(blocks.Values
            .Where(fold => context.Program.NumberAt(fold.StartLine) is { } start && context.Folds.IsFolded(start))
            .SelectMany(fold => Enumerable.Range(fold.StartLine + 1, fold.EndLine - fold.StartLine)));
        var glyphs = context.Console.DrawsUnicode ? emojiFoldGlyphs : asciiFoldGlyphs;
        var foldLane = blocks.Count == 0 ? 0 : App.Console.ConsoleText.WidthOf(glyphs.Open);

        foreach (var (number, statement) in lines)
        {
            var index = context.Program.IndexOf(number);
            if (hidden.Contains(index))
            {
                continue;
            }

            var text = $"{number} {statement}";
            var folded = blocks.ContainsKey(index) && context.Folds.IsFolded(number);
            var foldMark = !blocks.ContainsKey(index) ? new ReplTextRun(new string(' ', foldLane), ReplTextStyle.Plain)
                : new ReplTextRun(folded ? glyphs.Folded : glyphs.Open, ReplTextStyle.Plain);

            // A listing has a margin of one character for the mark of a breakpoint, and a space between it and the line. The line is marked as a whole when it has
            // one the program waits at, or is the statement the program that was stopped waits before; the mark in the margin is outside that, on the shell's own
            // background. A disabled breakpoint is its mark in the margin and nothing more: the program goes past it.
            var breakpoint = debugger.Breakpoints.SingleOrDefault(bp => bp.LineNumber == number);
            var waitsBefore = debugger.State is ReplDebugState.Suspended && debugger.StoppedAt == number;
            var glyph = breakpoint == default ? " "
                : breakpoint.ConditionExpression != null ?
                    breakpoint.Enabled ? conditionalGlyph : disabledConditionalGlyph
                : breakpoint.Enabled ? enabledGlyph : disabledGlyph;

            var breakpointMark = breakpoint != default ? new ReplTextRun(glyph, ReplTextStyle.BreakpointGlyph) : new ReplTextRun(" ", ReplTextStyle.Plain);

            var style = waitsBefore ? ReplLineStyle.CurrentStatement
                : breakpoint != default && breakpoint.Enabled ? ReplLineStyle.Breakpoint
                : ReplLineStyle.Plain;

            var padding = new ReplTextRun(" " + new string(' ', width - number.ToString().Length), ReplTextStyle.Plain);
            ReplTextRun[] runs = folded ? [padding, new ReplTextRun(FittedToTheConsole(context.Console, text, foldLane + 1 + padding.Text.Length), ReplTextStyle.Ghost)]
                : highlighted is not null && highlighted.TryGetValue(index, out var tokens) ? [padding, .. ReplHighlighter.Runs(text, tokens)]
                : [padding, new ReplTextRun(text, ReplTextStyle.Plain)];
            context.Console.WriteListingLine([breakpointMark, foldMark], runs, style);
        }

        context.Console.WriteLine();
        return ReplCommandResult.Continue;
    }

    // the first line of a folded block, cut with an ellipsis to what is left of the console's width after the margin; as it is when the width cannot be told.
    private static string FittedToTheConsole(IReplConsole console, string text, int before)
        => console.Width > before ? App.Console.ConsoleText.Truncate(text, console.Width - before - 1) : text;

    /// <summary>
    /// The blocks of the program, as the language server answers <c>textDocument/foldingRange</c>; none when it cannot be asked.
    /// </summary>
    /// <remarks>The <c>FOLD</c> command reads them as a listing does.</remarks>
    internal static async Task<IReadOnlyList<ReplFold>> BlockFoldsAsync(ReplCommandContext context, CancellationToken token)
    {
        try
        {
            if (!await context.Document.EnsureOpenAsync(token) || context.Document.Uri is not { } document)
            {
                return [];
            }

            return await context.Platform.GetBlockFoldsAsync(document, token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return [];
        }
    }

    // the language server says what each token of the program is, by the line of the program it is on. A listing it cannot highlight is a listing all the same.
    private static async Task<Dictionary<int, List<ReplSemanticToken>>?> TokensByLineAsync(ReplCommandContext context, CancellationToken token)
    {
        try
        {
            if (!await context.Document.EnsureOpenAsync(token) || context.Document.Uri is not { } document)
            {
                return null;
            }

            var byLine = new Dictionary<int, List<ReplSemanticToken>>();
            foreach (var semanticToken in await context.Platform.GetSemanticTokensAsync(document, token))
            {
                if (!byLine.TryGetValue(semanticToken.Line, out var line))
                {
                    byLine[semanticToken.Line] = line = [];
                }

                line.Add(semanticToken);
            }

            return byLine;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
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
