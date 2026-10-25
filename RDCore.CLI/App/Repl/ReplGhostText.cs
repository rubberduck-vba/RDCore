namespace RDCore.CLI.App.Repl;

/// <summary>
/// The ghost text of the line at the shell's prompt (<see cref="App.Console.ConsolePromptEditor.Ghost"/>): the line of the program whose number is typed, which
/// is what a numbered line typed at the prompt usually is - an edit of it, rather than a line typed again from nothing.
/// </summary>
/// <remarks>
/// The number is the whole number that is typed: <c>10</c> is line 10, and not the start of line 100. What is typed after it is the start of the line's
/// statement, or the line has no ghost text: once what is typed is not the line any more, there is nothing to offer. The blanks between the number and the
/// statement are the typist's, any number of them.
/// </remarks>
/// <param name="program">The program buffer.</param>
internal sealed class ReplGhostText(ReplProgram program)
{
    /// <summary>
    /// What <paramref name="typed"/> can go on as: the rest of the line of the program whose number it starts with.
    /// </summary>
    /// <param name="typed">The line, as it is typed.</param>
    /// <returns>The rest of the line; <see langword="null"/> when there is no such line, or what is typed is not the start of it.</returns>
    public string? For(string typed)
    {
        var digits = 0;
        while (digits < typed.Length && char.IsAsciiDigit(typed[digits]))
        {
            digits++;
        }

        if (digits == 0 || !int.TryParse(typed[..digits], out var number) || program.IndexOf(number) < 0)
        {
            return null;
        }

        var statement = program.Lines(number, number).Single().Statement;
        var rest = typed[digits..];
        if (rest.Length == 0)
        {
            return " " + statement;
        }

        if (!char.IsWhiteSpace(rest[0]))
        {
            // `100` is no start of line 10.
            return null;
        }

        var written = rest.TrimStart();
        return statement.StartsWith(written, StringComparison.Ordinal) ? statement[written.Length..] : null;
    }
}
