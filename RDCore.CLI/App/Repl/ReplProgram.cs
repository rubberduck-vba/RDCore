using System.Text;

namespace RDCore.CLI.App.Repl;

/// <summary>
/// The interactive shell's program buffer: the numbered lines typed at the prompt, in line-number
/// order, and the real VBA module source they render to.
/// </summary>
/// <remarks>
/// The numbering is not an invention of the shell. A line-number label (<c>100 X = 1</c>) is real
/// MS-VBAL syntax — <strong>MS-VBAL 5.4.1.1</strong>'s <c>statement-label-definition</c> — so a
/// numbered line typed at the prompt is one more ordinary VBA statement, stored under a label the
/// parser, the lowering pass and <c>GoTo</c> all already understand. That is also why the buffer is
/// sorted by number rather than kept in entry order: the number is the line's identity, exactly as it
/// is in the BASIC this shell is modelled on. Re-typing a number replaces that line; typing a number
/// with nothing after it deletes it.
/// </remarks>
public sealed class ReplProgram
{
    /// <summary>The name of the module the buffer renders to, and of the file backing it.</summary>
    public const string ModuleName = "Program";

    /// <summary>The name of the procedure the numbered lines make up — what <c>RUN</c> invokes.</summary>
    public const string EntryPointName = "Main";

    /// <summary>The name of the procedure an immediate-mode line is wrapped in.</summary>
    public const string ImmediateEntryPointName = "Immediate";

    // VBA source is CRLF, and the parser's own line/column positions are reported against it.
    private const string NewLine = "\r\n";

    private readonly SortedDictionary<int, string> _lines = [];

    /// <summary>The number of lines currently in the buffer.</summary>
    public int Count => _lines.Count;

    /// <summary>Whether the buffer holds no lines at all.</summary>
    public bool IsEmpty => _lines.Count == 0;

    /// <summary>
    /// Bumped by every edit. This is the buffer's LSP document version — the language server drops a
    /// parse or a diagnostic report that raced a later edit by comparing it.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// Inserts <paramref name="statement"/> at <paramref name="number"/>, replacing whatever line was
    /// there.
    /// </summary>
    /// <param name="number">The line number, which is also the line's label in the rendered source.</param>
    /// <param name="statement">The statement text, without the number.</param>
    public void Store(int number, string statement)
    {
        _lines[number] = statement;
        Version++;
    }

    /// <summary>
    /// Removes the line numbered <paramref name="number"/>.
    /// </summary>
    /// <returns><c>false</c> if no such line was in the buffer.</returns>
    public bool Delete(int number)
    {
        if (!_lines.Remove(number))
        {
            return false;
        }

        Version++;
        return true;
    }

    /// <summary>Empties the buffer.</summary>
    public void Clear()
    {
        if (_lines.Count == 0)
        {
            return;
        }

        _lines.Clear();
        Version++;
    }

    /// <summary>
    /// The buffer's lines in number order, optionally narrowed to a range.
    /// </summary>
    /// <param name="from">The first line number to include, or <c>null</c> for the start of the buffer.</param>
    /// <param name="to">The last line number to include, or <c>null</c> for the end of the buffer.</param>
    public IEnumerable<(int Number, string Statement)> Lines(int? from = null, int? to = null)
        => _lines
            .Where(line => (from is null || line.Key >= from) && (to is null || line.Key <= to))
            .Select(line => (line.Key, line.Value));

    /// <summary>
    /// The buffer as a VBA standard module: every numbered line is a labelled statement in the
    /// <see cref="EntryPointName"/> procedure, which is what <c>RUN</c> invokes.
    /// </summary>
    public string ToModuleSource()
    {
        var source = new StringBuilder();
        AppendProcedure(source, EntryPointName, Lines().Select(line => $"{line.Number} {line.Statement}"));
        return source.ToString();
    }

    /// <summary>
    /// The buffer as a VBA standard module with <paramref name="statement"/> appended as a second,
    /// parameterless procedure — how an immediate-mode line is compiled, so that it sees the same
    /// module scope the program itself runs in.
    /// </summary>
    /// <param name="statement">The statement typed at the prompt.</param>
    public string ToImmediateModuleSource(string statement)
    {
        var source = new StringBuilder();
        AppendProcedure(source, EntryPointName, Lines().Select(line => $"{line.Number} {line.Statement}"));
        source.Append(NewLine);
        AppendProcedure(source, ImmediateEntryPointName, [statement]);
        return source.ToString();
    }

    private static void AppendProcedure(StringBuilder source, string name, IEnumerable<string> statements)
    {
        source.Append("Public Sub ").Append(name).Append("()").Append(NewLine);
        foreach (var statement in statements)
        {
            source.Append(statement).Append(NewLine);
        }
        source.Append("End Sub").Append(NewLine);
    }
}
