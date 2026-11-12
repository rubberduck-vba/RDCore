using RDCore.External.Interaction;
using RDCore.CLI.ConsoleIO.Model;

namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>LIST</c>: renders the program buffer back, in line-number order.
/// </summary>
/// <remarks>
/// The argument is a BASIC line range: <c>LIST</c>, <c>LIST 100</c>, <c>LIST 100-200</c>,
/// <c>LIST 100-</c> (from there on) or <c>LIST -200</c> (up to there).
/// <para>
/// The margin has two lanes. The first, at the edge, marks a breakpoint, or a line that has a fix (💡: <c>DUCKY</c> proposes it) when it has no breakpoint; the
/// second, next to the line, marks the line a block starts at - an <c>If</c>, a <c>Select Case</c>, a loop, a <c>With</c> - which <c>FOLD</c> folds. A folded block is its first line, faint as ghost text and cut to the console's width, and none of the lines
/// after it, to its <c>End</c>.
/// </para>
/// <para>
/// With <c>INLAYS ON</c> a listing also shows, faint and in the line, the hints the language server gives (<c>textDocument/inlayHint</c>): the name of the
/// parameter each argument is given to, and <c>As Variant</c> after a declaration that states no type. They are asked once for the whole listing.
/// </para>
/// </remarks>
internal sealed class ListReplCommand : IReplCommand
{
    public string Name => ReplCommandNames.List;
    public IReadOnlyList<string> Aliases => [];
    public bool WorksOnTheProgram => true;
    public string Summary => Resources.Repl_List_Summary;

    // the marks of a block, plain triangles where the console draws Unicode (an emoji looks out of place next to the code), ASCII where it does not.
    private static readonly (string Open, string Folded) unicodeFoldGlyphs = ("▽", "▲");
    private static readonly (string Open, string Folded) asciiFoldGlyphs = ("-", "+");

    // the mark of a line that has a fix: a light bulb, two cells wide, where the console draws Unicode; one cell, in ASCII, where it does not.
    private const string UnicodeFixGlyph = "💡";
    private const string AsciiFixGlyph = "*";

    public async Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        if (!TryParseRange(arguments, out var from, out var to))
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_SyntaxError, arguments);
            return ReplCommandResult.Continue;
        }

        var debugger = context.Debugger;
        await WriteAsync(context, from, to, debugger.State is ReplDebugState.Suspended ? debugger.StoppedAt : null, withFolds: true, token);
        return ReplCommandResult.Continue;
    }

    /// <summary>
    /// Lists the lines of the program from <paramref name="from"/> to <paramref name="to"/> as <c>LIST</c> does, with <paramref name="current"/> marked as the
    /// statement the program waits before.
    /// </summary>
    /// <remarks>A break lists the lines around the one it stopped at with it.</remarks>
    /// <param name="context">The live session.</param>
    /// <param name="from">The first line number to list, or <see langword="null"/> for the start of the program.</param>
    /// <param name="to">The last line number to list, or <see langword="null"/> for its end.</param>
    /// <param name="current">The line marked as the statement the program waits before; <see langword="null"/> for none.</param>
    /// <param name="withFolds">
    /// Whether a block that is folded (<c>FOLD</c>) is listed folded. When <see langword="false"/>, every line of the range is listed, and each block is marked as open.
    /// </param>
    /// <param name="token">A token that cancels the requests the listing makes of the language server.</param>
    internal static async Task WriteAsync(ReplCommandContext context, int? from, int? to, int? current, bool withFolds, CancellationToken token)
    {
        // the numbers are right-aligned to the widest one in the listing, as a BASIC listing is, so
        // the statements line up whatever the numbering.
        var lines = context.Program.Lines(from, to).ToArray();
        var width = lines.Length == 0 ? 0 : lines.Max(line => line.Number).ToString().Length;
        var highlighted = lines.Length == 0 ? null : await TokensByLineAsync(context, token);
        var hints = lines.Length == 0 || !context.Inlays.Enabled ? null : await InlayHintsByLineAsync(context, token);
        var debugger = context.Debugger;

        // the blocks, by the index of the line each starts at; and the lines a folded one hides, which are the ones after its first, to its last.
        var blocks = new Dictionary<int, ReplFold>();
        foreach (var fold in lines.Length == 0 ? [] : await BlockFoldsAsync(context, token))
        {
            blocks.TryAdd(fold.StartLine, fold);
        }

        bool IsFolded(int number) => withFolds && context.Folds.IsFolded(number);
        var hidden = new HashSet<int>(blocks.Values
            .Where(fold => context.Program.NumberAt(fold.StartLine) is { } start && IsFolded(start))
            .SelectMany(fold => Enumerable.Range(fold.StartLine + 1, fold.EndLine - fold.StartLine)));
        var glyphs = context.Console.DrawsUnicode ? unicodeFoldGlyphs : asciiFoldGlyphs;
        var foldLane = blocks.Count == 0 ? 0 : ConsoleText.WidthOf(glyphs.Open);

        // the lines a diagnostic with a fix is on, and the lane they are marked in: the cells of the light bulb on every line when any line has one, so that the
        // lane is as wide on a line with a breakpoint, or with none, as on a line with a bulb.
        var fixes = lines.Length == 0 ? new HashSet<int>() : await LinesWithAFixAsync(context, token);
        var fixGlyph = context.Console.DrawsUnicode ? UnicodeFixGlyph : AsciiFixGlyph;
        var breakpointLane = lines.Any(line => fixes.Contains(context.Program.IndexOf(line.Number)) && !hidden.Contains(context.Program.IndexOf(line.Number)))
            ? Math.Max(1, ConsoleText.WidthOf(fixGlyph))
            : 1;

        foreach (var (number, statement) in lines)
        {
            var index = context.Program.IndexOf(number);
            if (hidden.Contains(index))
            {
                continue;
            }

            var text = $"{number} {statement}";
            var folded = blocks.ContainsKey(index) && IsFolded(number);
            var foldMark = !blocks.ContainsKey(index) ? new ReplTextRun(new string(' ', foldLane), ReplTextStyle.Plain)
                : new ReplTextRun(folded ? glyphs.Folded : glyphs.Open, ReplTextStyle.Plain);

            // A listing has a margin of one character for the mark of a breakpoint, and a space between it and the line. The line is marked as a whole when it has
            // one the program waits at, or is the statement the program that was stopped waits before; the mark in the margin is outside that, on the shell's own
            // background. A disabled breakpoint is its mark in the margin and nothing more: the program goes past it.
            var breakpoint = debugger.Breakpoints.SingleOrDefault(bp => bp.LineNumber == number);
            var waitsBefore = current == number;

            // a folded block hides its lines and the breakpoints on them: its first line says how many lines, and shows a breakpoint it hides when it has none of
            // its own - an enabled one first. The line isn't marked as a breakpoint's: the program doesn't stop on it.
            var hiddenLines = folded ? blocks[index].EndLine - blocks[index].StartLine : 0;
            var hiddenBreakpoint = !folded || breakpoint != default ? default
                : debugger.Breakpoints
                    .Where(bp => Enumerable.Range(blocks[index].StartLine + 1, hiddenLines).Any(line => context.Program.NumberAt(line) == bp.LineNumber))
                    .OrderByDescending(bp => bp.Enabled)
                    .FirstOrDefault();
            var marked = breakpoint != default ? breakpoint : hiddenBreakpoint;
            var glyph = marked == default ? " " : ReplBreakpointMarks.GlyphOf(marked);

            // a breakpoint's mark takes the lane before the mark of a fix, and both are padded to the width of the lane.
            var hasAFix = fixes.Contains(index) || (folded && Enumerable.Range(blocks[index].StartLine + 1, hiddenLines).Any(fixes.Contains));
            var breakpointMark = marked != default ? new ReplTextRun(Padded(glyph, breakpointLane), ReplTextStyle.BreakpointGlyph)
                : hasAFix ? new ReplTextRun(Padded(fixGlyph, breakpointLane), ReplTextStyle.Plain)
                : new ReplTextRun(new string(' ', breakpointLane), ReplTextStyle.Plain);

            var style = waitsBefore ? ReplLineStyle.CurrentStatement
                : breakpoint != default && breakpoint.Enabled ? ReplLineStyle.Breakpoint
                : ReplLineStyle.Plain;

            var padding = new ReplTextRun(" " + new string(' ', width - number.ToString().Length), ReplTextStyle.Plain);
            var foldedText = $"{number} {(hiddenLines == 1 ? Resources.Repl_Fold_HiddenLine : string.Format(Resources.Repl_Fold_HiddenLines, hiddenLines))} {statement}";
            IReadOnlyList<ReplTextRun> code = highlighted is not null && highlighted.TryGetValue(index, out var tokens) ? ReplHighlighter.Runs(text, tokens)
                : [new ReplTextRun(text, ReplTextStyle.Plain)];
            if (hints is not null && hints.TryGetValue(index, out var lineHints))
            {
                code = ReplInlayRuns.With(code, lineHints);
            }

            ReplTextRun[] runs = folded ? [padding, new ReplTextRun(FittedToTheConsole(context.Console, foldedText, foldLane + breakpointLane + padding.Text.Length), ReplTextStyle.Ghost)]
                : [padding, .. code];
            context.Console.WriteListingLine([breakpointMark, foldMark], runs, style);
        }

        context.Console.WriteLine();
    }

    /// <summary>
    /// Lists lines of a module of the workspace as <c>LIST</c> lists the program: each by its line number in the module, highlighted, with the mark of its
    /// breakpoint, the mark of a block that starts on it, and <paramref name="current"/> marked as the statement the program waits before.
    /// </summary>
    /// <remarks>A break in a module of the workspace lists the lines around the one it stopped at with it.</remarks>
    /// <param name="context">The live session.</param>
    /// <param name="document">The module's document, which the language server highlights.</param>
    /// <param name="text">The lines of the module, as the document has them.</param>
    /// <param name="first">The zero-based line of the module to list first.</param>
    /// <param name="last">The zero-based line of the module to list last.</param>
    /// <param name="current">The zero-based line marked as the statement the program waits before.</param>
    /// <param name="breakpoints">The breakpoints of the module, by its line numbers (one-based, as they are listed).</param>
    /// <param name="token">A token that cancels the requests the listing makes of the language server.</param>
    internal static async Task WriteModuleAsync(
        ReplCommandContext context, Uri document, IReadOnlyList<string> text, int first, int last, int current, IReadOnlyCollection<Breakpoint> breakpoints,
        CancellationToken token)
    {
        first = Math.Max(0, first);
        last = Math.Min(text.Count - 1, last);
        if (first > last)
        {
            return;
        }

        var width = (last + 1).ToString().Length;
        var highlighted = await TokensByLineAsync(context, document, token);
        var blocks = new HashSet<int>((await BlockFoldsAsync(context, document, token)).Select(fold => fold.StartLine));
        var glyphs = context.Console.DrawsUnicode ? unicodeFoldGlyphs : asciiFoldGlyphs;
        var foldLane = Enumerable.Range(first, last - first + 1).Any(blocks.Contains) ? ConsoleText.WidthOf(glyphs.Open) : 0;

        for (var line = first; line <= last; line++)
        {
            var number = line + 1;
            var statement = text[line];
            var breakpoint = breakpoints.SingleOrDefault(bp => bp.LineNumber == number);
            var breakpointMark = breakpoint != default ? new ReplTextRun(ReplBreakpointMarks.GlyphOf(breakpoint), ReplTextStyle.BreakpointGlyph)
                : new ReplTextRun(" ", ReplTextStyle.Plain);
            var foldMark = new ReplTextRun(blocks.Contains(line) ? glyphs.Open : new string(' ', foldLane), ReplTextStyle.Plain);

            var style = line == current ? ReplLineStyle.CurrentStatement
                : breakpoint != default && breakpoint.Enabled ? ReplLineStyle.Breakpoint
                : ReplLineStyle.Plain;

            // the tokens are of the line as the module has it, and the listing puts its number before it.
            var listed = $"{number} {statement}";
            IReadOnlyList<ReplTextRun> code = highlighted is not null && highlighted.TryGetValue(line, out var tokens)
                ? ReplHighlighter.Runs(listed, tokens, offset: number.ToString().Length + 1)
                : [new ReplTextRun(listed, ReplTextStyle.Plain)];
            var padding = new ReplTextRun(" " + new string(' ', width - number.ToString().Length), ReplTextStyle.Plain);
            context.Console.WriteListingLine([breakpointMark, foldMark], [padding, .. code], style);
        }

        context.Console.WriteLine();
    }

    private static string Padded(string mark, int lane) => mark + new string(' ', Math.Max(0, lane - ConsoleText.WidthOf(mark)));

    // the lines of the program a diagnostic with a fix is on, by their position in the program; none when the server cannot be asked.
    private static async Task<IReadOnlySet<int>> LinesWithAFixAsync(ReplCommandContext context, CancellationToken token)
    {
        await context.Diagnostics.RefreshAsync(context, token);
        return context.Diagnostics.LinesWithAFix();
    }

    // the first line of a folded block, cut with an ellipsis to what is left of the console's width after the margin; as it is when the width cannot be told.
    private static string FittedToTheConsole(IReplConsole console, string text, int before)
        => console.Width > before ? ConsoleText.Truncate(text, console.Width - before - 1) : text;

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

            return await BlockFoldsAsync(context, document, token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return [];
        }
    }

    // the blocks of a document; none when the language server cannot be asked.
    private static async Task<IReadOnlyList<ReplFold>> BlockFoldsAsync(ReplCommandContext context, Uri document, CancellationToken token)
    {
        try
        {
            return await context.Platform.GetBlockFoldsAsync(document, token) ?? [];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return [];
        }
    }

    // the hints of the language server, by the line of the program they are on, asked once for the whole listing. A listing it cannot hint is a listing all the same.
    private static async Task<Dictionary<int, List<ReplInlayHint>>?> InlayHintsByLineAsync(ReplCommandContext context, CancellationToken token)
    {
        try
        {
            if (!await context.Document.EnsureOpenAsync(token) || context.Document.Uri is not { } document)
            {
                return null;
            }

            return (await context.Platform.GetInlayHintsAsync(document, token)).GroupBy(hint => hint.Line).ToDictionary(group => group.Key, group => group.ToList());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
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

            return await TokensByLineAsync(context, document, token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    // the tokens of a document, by the line they are on; nothing when the language server cannot be asked.
    private static async Task<Dictionary<int, List<ReplSemanticToken>>?> TokensByLineAsync(ReplCommandContext context, Uri document, CancellationToken token)
    {
        try
        {
            var byLine = new Dictionary<int, List<ReplSemanticToken>>();
            foreach (var semanticToken in await context.Platform.GetSemanticTokensAsync(document, token) ?? [])
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
