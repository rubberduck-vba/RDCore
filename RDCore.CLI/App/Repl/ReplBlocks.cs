using System.Globalization;
using RDCore.Parsing;
using RDCore.SDK.Formatting;

namespace RDCore.CLI.App.Repl;

/// <summary>
/// Block completion at the shell's prompt: while the numbered line last stored is in a block of the program that has no closer, the next line opens on the
/// number after it, selected, and the closers the program needs are drawn under the line, faint, numbered on from it (<see cref="ProgramBlocks"/>).
/// </summary>
/// <remarks>
/// <para>
/// The open blocks come from a parse of the program as it is, after each line is stored: a block whose closer the program has is not open, and a single-line
/// <c>If</c> is not a block. Each line is stored as it is entered, so what block completion offers is never more than a suggestion: the program has every line
/// that was entered, whatever key leaves block completion.
/// </para>
/// <para>
/// The suggested number is the stored line's and <see cref="Step"/> more; a number that is typed instead wins, and the next suggestion counts on from it. A
/// suggestion stays below the next line of the program: nothing is renumbered. When the next line leaves no number for the line after the stored one, block
/// completion ends there and says so; when it leaves none for a closer, the closer is not drawn, and the row says why.
/// </para>
/// <para>
/// Once the innermost block has a statement in its last branch, its closer is the line's ghost text after the number, which <kbd>Tab</kbd> takes and enters.
/// </para>
/// </remarks>
/// <param name="program">The program buffer.</param>
internal sealed class ReplBlocks(ReplProgram program)
{
    /// <summary>
    /// How far apart the numbers block completion suggests are.
    /// </summary>
    public const int Step = 10;

    private readonly ModuleParser _parser = new();
    private IReadOnlyList<ProgramBlock> _open = [];
    private int _stored;

    /// <summary>
    /// Whether block completion is on: the line last stored is in a block that has no closer.
    /// </summary>
    public bool IsOpen => _open.Count > 0;

    /// <summary>
    /// The closers of the open blocks, the innermost first.
    /// </summary>
    public IReadOnlyList<string> Closers => [.. _open.Select(block => block.Closer)];

    /// <summary>
    /// A numbered line was stored: block completion goes on while it is in a block the program has no closer for, and room is left for what comes next.
    /// </summary>
    /// <param name="number">The number of the line.</param>
    /// <returns>Why block completion ends there, when there is no room before the next line of the program; <see langword="null"/> otherwise.</returns>
    public string? Stored(int number)
    {
        var lines = program.Statements();
        var index = -1;
        for (var at = 0; at < lines.Count && index < 0; at++)
        {
            index = lines[at].Number == number ? at : -1;
        }

        _stored = number;
        _open = index < 0 ? [] : ProgramBlocks.OpenAt([.. lines.Select(line => line.Statement)], index, text => _parser.Parse(ReplProgram.IndentedModuleUri, text));
        if (_open.Count == 0)
        {
            return null;
        }

        // the line after this one, and the innermost closer: on the next line when the block has a body, on the one after it when it has none.
        var closerAt = _open[0].HasBody ? number + Step : number + Step * 2;
        if (NextLineAfter(number) is { } next && closerAt >= next)
        {
            var closer = _open[0].Closer;
            _open = [];
            return NoRoom(closer, next);
        }

        return null;
    }

    /// <summary>
    /// Block completion ends: an empty line, a command or a statement to run was entered, or a line was deleted.
    /// </summary>
    public void Leave() => _open = [];

    /// <summary>
    /// What the next line starts as: the suggested number, selected, and a space after it; <see langword="null"/> while block completion is off.
    /// </summary>
    public Snippet? Start
        => IsOpen && Snippet.TryParse($"${{1:{(_stored + Step).ToString(CultureInfo.InvariantCulture)}}} $0", out var snippet) ? snippet : null;

    /// <summary>
    /// The ghost text of a line: the rest of the innermost closer, after the number and what of the closer is typed, once the block has a body.
    /// </summary>
    /// <param name="typed">The line, as it is typed.</param>
    /// <returns>The rest of the closer; <see langword="null"/> when the line is not the start of it.</returns>
    public string? GhostFor(string typed)
    {
        if (!IsOpen || !_open[0].HasBody || !TrySplit(typed, out _, out var statement, out var spaced))
        {
            return null;
        }

        var closer = _open[0].Closer;
        if (!spaced)
        {
            return " " + closer;
        }

        return closer.StartsWith(statement, StringComparison.OrdinalIgnoreCase) && statement.Length < closer.Length ? closer[statement.Length..] : null;
    }

    /// <summary>
    /// Whether a line is the innermost closer, after its number: <kbd>Tab</kbd> enters the line it makes whole (<see cref="App.Console.ConsolePromptEditor.EntersWithGhost"/>).
    /// </summary>
    /// <param name="line">The line, with the ghost text taken.</param>
    public bool Closes(string line)
        => IsOpen && TrySplit(line, out _, out var statement, out var spaced) && spaced
            && string.Equals(statement.TrimEnd(), _open[0].Closer, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The rows drawn under a line: the closers the program needs after it, each numbered on from it, the innermost first. The innermost is left out when the
    /// line is, or can be completed as, that closer. A closer the next line of the program leaves no number for is not drawn, nor any after it: a row says why.
    /// </summary>
    /// <param name="typed">The line, as it is typed.</param>
    /// <returns>The rows; none while block completion is off, or when the line has no number.</returns>
    public IReadOnlyList<string> RowsFor(string typed)
    {
        if (!IsOpen || !TrySplit(typed, out var number, out var statement, out _))
        {
            return [];
        }

        var closers = Closers;
        var innermost = _open[0];
        if (innermost.HasBody && innermost.Closer.StartsWith(statement.TrimEnd(), StringComparison.OrdinalIgnoreCase))
        {
            closers = [.. closers.Skip(1)];
        }

        var rows = new List<string>();
        var next = NextLineAfter(number);
        var at = number;
        foreach (var closer in closers)
        {
            at += Step;
            if (next is { } taken && at >= taken)
            {
                rows.Add(NoRoom(closer, taken));
                break;
            }

            rows.Add($"{at.ToString(CultureInfo.InvariantCulture)} {closer}");
        }

        return rows;
    }

    private static string NoRoom(string closer, int next)
        => string.Format(CultureInfo.CurrentCulture, Resources.Repl_BlockNoRoom, closer, next.ToString(CultureInfo.InvariantCulture));

    private int? NextLineAfter(int number)
    {
        foreach (var (line, _) in program.Statements())
        {
            if (line > number)
            {
                return line;
            }
        }

        return null;
    }

    // a line's number, and the statement after it and its blanks; whether there are blanks after the number.
    private static bool TrySplit(string line, out int number, out string statement, out bool spaced)
    {
        var digits = 0;
        while (digits < line.Length && char.IsAsciiDigit(line[digits]))
        {
            digits++;
        }

        statement = string.Empty;
        spaced = false;
        if (digits == 0 || !int.TryParse(line.AsSpan(0, digits), NumberStyles.None, CultureInfo.InvariantCulture, out number))
        {
            number = 0;
            return false;
        }

        var rest = line[digits..];
        if (rest.Length > 0 && !char.IsWhiteSpace(rest[0]))
        {
            return false;
        }

        spaced = rest.Length > 0;
        statement = rest.TrimStart();
        return true;
    }
}
