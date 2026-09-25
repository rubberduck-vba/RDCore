namespace RDCore.CLI.App.Repl;

/// <summary>
/// What one line typed at the interactive prompt turned out to be.
/// </summary>
public enum ReplInputKind
{
    /// <summary>Nothing was typed; the shell just prompts again.</summary>
    Empty,

    /// <summary>A numbered line: store it in the program buffer, replacing whatever was there.</summary>
    StoreLine,

    /// <summary>A line number with nothing after it: remove that line from the program buffer.</summary>
    DeleteLine,

    /// <summary>A shell command (<c>LIST</c>, <c>RUN</c>, …) rather than VBA.</summary>
    Command,

    /// <summary>An unnumbered statement: execute it now, in immediate mode.</summary>
    Immediate,
}

/// <summary>
/// One classified line of interactive input.
/// </summary>
/// <param name="Kind">What the line turned out to be.</param>
/// <param name="LineNumber">The line number, for <see cref="ReplInputKind.StoreLine"/> and <see cref="ReplInputKind.DeleteLine"/>.</param>
/// <param name="Text">The statement text, for <see cref="ReplInputKind.StoreLine"/> and <see cref="ReplInputKind.Immediate"/>.</param>
/// <param name="CommandName">The command verb as typed, for <see cref="ReplInputKind.Command"/>.</param>
/// <param name="Arguments">Everything after the verb, trimmed, for <see cref="ReplInputKind.Command"/>.</param>
public readonly record struct ReplInput(
    ReplInputKind Kind,
    int LineNumber = 0,
    string Text = "",
    string CommandName = "",
    string Arguments = "");

/// <summary>
/// Classifies a line typed at the interactive prompt, BASIC-style.
/// </summary>
/// <remarks>
/// The order of the rules is the whole design, and it is BASIC's, not an invention:
/// <list type="number">
/// <item>A leading line number means the line is program text, never a command — so a program can
/// contain a statement that happens to start with a word the shell also uses as a verb.</item>
/// <item>A line number with nothing after it deletes that line. This is the classic convention; there
/// is no separate delete verb.</item>
/// <item>Otherwise a leading word that names a shell command is one. Commands are matched before the
/// line is treated as VBA, so <c>LIST</c> lists rather than resolving as an identifier.</item>
/// <item>Anything else is a statement to execute immediately.</item>
/// </list>
/// <para>
/// <c>?</c> expands to <c>Debug.Print</c> wherever a statement can appear — the shorthand VBA itself
/// has always had. The expansion happens here, on the way in, so the program buffer and every
/// listing hold real VBA rather than a shell-only spelling (and so the parser never has to grow a
/// rule for a character the language's own grammar does not define).
/// </para>
/// </remarks>
public static class ReplInputParser
{
    /// <summary>What <c>?</c> expands to.</summary>
    public const string PrintShorthandExpansion = "Debug.Print";

    /// <summary>
    /// Classifies <paramref name="line"/>.
    /// </summary>
    /// <param name="line">The raw line as typed.</param>
    /// <param name="isCommandName">
    /// Tells the parser whether a leading word names a shell command. The parser does not own the
    /// command vocabulary — the dispatcher does, and it grows.
    /// </param>
    public static ReplInput Parse(string? line, Func<string, bool> isCommandName)
    {
        var trimmed = line?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return new ReplInput(ReplInputKind.Empty);
        }

        if (char.IsAsciiDigit(trimmed[0]))
        {
            return ParseNumberedLine(trimmed);
        }

        var verb = trimmed[..IndexOfSeparator(trimmed)];
        if (isCommandName(verb))
        {
            return new ReplInput(ReplInputKind.Command,
                CommandName: verb,
                Arguments: trimmed[verb.Length..].Trim());
        }

        return new ReplInput(ReplInputKind.Immediate, Text: ExpandPrintShorthand(trimmed));
    }

    private static ReplInput ParseNumberedLine(string trimmed)
    {
        var digits = 0;
        while (digits < trimmed.Length && char.IsAsciiDigit(trimmed[digits]))
        {
            digits++;
        }

        if (!int.TryParse(trimmed[..digits], out var number))
        {
            // more digits than an Int32 holds: not a line number, so it can only be a statement that
            // starts with a numeric literal, which is a syntax error the parser gets to report.
            return new ReplInput(ReplInputKind.Immediate, Text: ExpandPrintShorthand(trimmed));
        }

        var statement = trimmed[digits..].Trim();
        return statement.Length == 0
            ? new ReplInput(ReplInputKind.DeleteLine, LineNumber: number)
            : new ReplInput(ReplInputKind.StoreLine, LineNumber: number, Text: ExpandPrintShorthand(statement));
    }

    /// <summary>
    /// Rewrites a leading <c>?</c> as <c>Debug.Print</c>. Only a leading one: <c>?</c> anywhere else
    /// in a line is not the shorthand.
    /// </summary>
    /// <param name="statement">The statement text as typed.</param>
    public static string ExpandPrintShorthand(string statement)
        => statement.StartsWith('?')
            ? $"{PrintShorthandExpansion} {statement[1..].TrimStart()}".TrimEnd()
            : statement;

    // a verb ends at the first whitespace; everything up to there is the candidate command name.
    private static int IndexOfSeparator(string trimmed)
    {
        var index = trimmed.AsSpan().IndexOfAny(' ', '\t');
        return index < 0 ? trimmed.Length : index;
    }
}
