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
/// <para>
/// The statements are stored indented by the blocks they are in, when the shell indents them (<see cref="RDCore.SDK.Formatting.ProgramIndenter"/>). An edit may
/// put the lines after a block's opener or closer at another level, and the whole program is indented again - once, when it is next read as a whole (<c>LIST</c>,
/// <c>SAVE</c>, <c>RUN</c>, the document the language server holds), not at each edit: lines pasted at the prompt are indented once. A program that cannot be
/// indented is kept as it was typed.
/// </para>
/// <para>
/// The buffer is read from more threads than the shell's (what the language server is told when the program is left alone, the ghost text of the prompt): each
/// member reads and writes it under its lock.
/// </para>
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

    /// <summary>The address the module is parsed under when the shell indents its statements: a module of no workspace, which no one else holds.</summary>
    public static Uri IndentedModuleUri { get; } = new($"untitled:{ModuleName}{RDCore.SDK.Workspace.BasicProgramText.Extension}");

    private readonly SortedDictionary<int, string> _lines = [];

    private readonly object _gate = new();

    // the statements as they are to be stored, from the statements as they are: nothing, for a buffer that stores what is typed as it is.
    private readonly Func<IReadOnlyList<string>, IReadOnlyList<string>>? _indent;

    // told why the statements could not be indented, when they could not; they are kept as they were typed.
    private readonly Action<Exception>? _indentFailed;

    // a statement was stored or deleted since the buffer was indented: it is indented before it is read as a whole.
    private bool _unindented;

    // set, from any thread, when what the statements are indented by changed; the buffer is indented again on the shell's own thread (IndentIfChanged).
    private int _indentationChanged;

    /// <summary>A buffer that stores the statements as they are typed.</summary>
    public ReplProgram()
    {
    }

    /// <summary>A buffer that stores the statements indented by the blocks they are in.</summary>
    /// <param name="indent">
    /// The statements of the program as they are to be stored, from the statements as they are, in the order of their numbers: as many, in the same order
    /// (<see cref="RDCore.SDK.Formatting.ProgramIndenter.Indent"/>).
    /// </param>
    /// <param name="indentFailed">Told why the statements could not be indented, when <paramref name="indent"/> throws: they are kept as they were typed.</param>
    public ReplProgram(Func<IReadOnlyList<string>, IReadOnlyList<string>> indent, Action<Exception>? indentFailed = null)
    {
        _indent = indent;
        _indentFailed = indentFailed;
    }

    /// <summary>The number of lines currently in the buffer.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _lines.Count;
            }
        }
    }

    /// <summary>Whether the buffer holds no lines at all.</summary>
    public bool IsEmpty => Count == 0;

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
        lock (_gate)
        {
            _lines[number] = statement;
            _unindented = _indent is not null;
            Version++;
        }
    }

    /// <summary>
    /// The statement of a line as it was typed, without the indentation the program gives it: what the line is typed again as.
    /// </summary>
    /// <param name="number">The line number.</param>
    /// <returns>The statement; <see langword="null"/> when the program has no such line.</returns>
    public string? StatementOf(int number)
    {
        lock (_gate)
        {
            return _lines.TryGetValue(number, out var statement) ? statement.TrimStart() : null;
        }
    }

    /// <summary>
    /// Says what the statements are indented by changed - the indentation settings: they are indented again by <see cref="IndentIfChanged"/>. It may be
    /// called from any thread.
    /// </summary>
    public void IndentationChanged() => Interlocked.Exchange(ref _indentationChanged, 1);

    /// <summary>
    /// Indents the statements again, if what they are indented by changed since they were (<see cref="IndentationChanged"/>).
    /// </summary>
    /// <returns>Whether a statement is other than it was: the buffer is then one version later.</returns>
    public bool IndentIfChanged()
    {
        if (Interlocked.Exchange(ref _indentationChanged, 0) == 0)
        {
            return false;
        }

        lock (_gate)
        {
            _unindented = false;
            if (!Indent())
            {
                return false;
            }

            Version++;
            return true;
        }
    }

    // the statements, indented, when one was stored or deleted since they were; under the lock.
    private void EnsureIndented()
    {
        if (_unindented)
        {
            _unindented = false;
            Indent();
        }
    }

    // every statement, as it is to be stored; whether one is other than it was. A buffer that cannot be indented as the lines it has is left as it is; under the lock.
    private bool Indent()
    {
        if (_indent is null || _lines.Count == 0)
        {
            return false;
        }

        var numbers = _lines.Keys.ToArray();
        IReadOnlyList<string> indented;
        try
        {
            indented = _indent([.. _lines.Values]);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // the indenter could not read the program: it is kept as it is, and indented again at the next edit.
            _indentFailed?.Invoke(exception);
            return false;
        }

        if (indented.Count != numbers.Length)
        {
            return false;
        }

        var changed = false;
        for (var index = 0; index < numbers.Length; index++)
        {
            if (!string.Equals(_lines[numbers[index]], indented[index], StringComparison.Ordinal))
            {
                _lines[numbers[index]] = indented[index];
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Where the line numbered <paramref name="number"/> is in the program, which is the line of <see cref="ToSourceText"/> it is.
    /// </summary>
    /// <param name="number">The line number.</param>
    /// <returns>The zero-based position of the line among the lines, or <c>-1</c> if no such line is in the buffer.</returns>
    public int IndexOf(int number)
    {
        lock (_gate)
        {
            var index = 0;
            foreach (var key in _lines.Keys)
            {
                if (key == number)
                {
                    return index;
                }

                index++;
            }

            return -1;
        }
    }

    /// <summary>
    /// The number of the line at a position in the program, which is the line of <see cref="ToSourceText"/> it is: what <see cref="IndexOf"/> is the inverse of.
    /// </summary>
    /// <param name="index">The zero-based position of the line among the lines.</param>
    /// <returns>The line number, or <see langword="null"/> if the program has no line there.</returns>
    public int? NumberAt(int index)
    {
        lock (_gate)
        {
            return index >= 0 && index < _lines.Count ? _lines.Keys.ElementAt(index) : null;
        }
    }

    /// <summary>
    /// Where the line numbered <paramref name="number"/> is in the module the program is run as (<see cref="ToModuleSource"/>), which has the procedure's header
    /// above the lines: the line of the source a breakpoint on it is on.
    /// </summary>
    /// <param name="number">The line number.</param>
    /// <returns>The zero-based line of the module source, or <c>-1</c> if no such line is in the buffer.</returns>
    public int SourceLineOf(int number) => IndexOf(number) is var index && index >= 0 ? index + 1 : -1;

    /// <summary>
    /// Removes the line numbered <paramref name="number"/>.
    /// </summary>
    /// <returns><c>false</c> if no such line was in the buffer.</returns>
    public bool Delete(int number)
    {
        lock (_gate)
        {
            if (!_lines.Remove(number))
            {
                return false;
            }

            // the lines after a block's opener or closer that is gone are at another level.
            _unindented = _indent is not null;
            Version++;
            return true;
        }
    }

    /// <summary>Empties the buffer.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            if (_lines.Count == 0)
            {
                return;
            }

            _lines.Clear();
            _unindented = false;
            Version++;
        }
    }

    /// <summary>
    /// The program as the text of a <c>.rdc</c> file: one numbered line per line of the buffer, in number order.
    /// </summary>
    /// <remarks>
    /// This is the text of the document the language server is told of (<see cref="RDCore.SDK.Workspace.BasicProgramText"/>), not the module the lines are the
    /// body of.
    /// </remarks>
    public string ToSourceText()
    {
        var text = new StringBuilder();
        foreach (var (number, statement) in Lines())
        {
            text.Append(number).Append(' ').Append(statement).Append(NewLine);
        }

        return text.ToString();
    }

    /// <summary>
    /// Replaces the buffer with the program in the text of a <c>.rdc</c> file.
    /// </summary>
    /// <param name="text">The text: numbered lines, in any order; a line that is blank is skipped.</param>
    /// <returns>
    /// The 1-based lines of the text that are not numbered lines, in which case the buffer is as it was: a program that is only partly there is not
    /// one to run. Empty when the text was loaded.
    /// </returns>
    public IReadOnlyList<int> Load(string text)
    {
        var loaded = new SortedDictionary<int, string>();
        var rejected = new List<int>();
        var lineIndex = 0;
        foreach (var line in text.Split('\n'))
        {
            lineIndex++;
            var input = ReplInputParser.Parse(line, _ => false);
            switch (input.Kind)
            {
                case ReplInputKind.Empty:
                    break;
                case ReplInputKind.StoreLine:
                    loaded[input.LineNumber] = input.Text;
                    break;
                default:
                    rejected.Add(lineIndex);
                    break;
            }
        }

        if (rejected.Count > 0)
        {
            return rejected;
        }

        lock (_gate)
        {
            _lines.Clear();
            foreach (var line in loaded)
            {
                _lines.Add(line.Key, line.Value);
            }

            _unindented = _indent is not null;
            Version++;
        }

        return [];
    }

    /// <summary>
    /// The buffer's lines in number order, optionally narrowed to a range.
    /// </summary>
    /// <param name="from">The first line number to include, or <c>null</c> for the start of the buffer.</param>
    /// <param name="to">The last line number to include, or <c>null</c> for the end of the buffer.</param>
    public IEnumerable<(int Number, string Statement)> Lines(int? from = null, int? to = null)
    {
        lock (_gate)
        {
            EnsureIndented();
            return [.. _lines
                .Where(line => (from is null || line.Key >= from) && (to is null || line.Key <= to))
                .Select(line => (line.Key, line.Value))];
        }
    }

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
    /// The program line number that <paramref name="sourceLine"/> of <see cref="ToModuleSource"/>'s output
    /// came from, or <c>null</c> when that line is not one of the program's.
    /// </summary>
    /// <remarks>
    /// What a run-time error's position means to the person who typed the program. The platform locates an
    /// error in the source it was handed, which for a shell is generated - so the line it reports is a line
    /// nobody typed, and only the buffer can say which of its own lines that was.
    /// <para>
    /// The mapping is positional: <see cref="ToModuleSource"/> writes the procedure header first and then one
    /// line per numbered line, in order. An immediate-mode statement is past the end of them, since
    /// <see cref="ToImmediateModuleSource"/> appends it as a second procedure - hence <c>null</c> rather than
    /// a wrong answer.
    /// </para>
    /// </remarks>
    /// <param name="sourceLine">A zero-based line index in the generated module source.</param>
    public int? LineNumberAt(int sourceLine)
    {
        var lines = Lines().ToArray();
        var index = sourceLine - 1;
        return index >= 0 && index < lines.Length ? lines[index].Number : null;
    }

    /// <summary>
    /// The buffer as a VBA standard module with <paramref name="statement"/> appended as a second,
    /// parameterless procedure — how an immediate-mode line is compiled, so that it sees the same
    /// module scope the program itself runs in.
    /// </summary>
    /// <param name="statement">The statement typed at the prompt.</param>
    public string ToImmediateModuleSource(string statement) => ImmediateModuleSource(Lines(), statement);

    private static string ImmediateModuleSource(IEnumerable<(int Number, string Statement)> lines, string statement)
    {
        var source = new StringBuilder();
        AppendProcedure(source, EntryPointName, lines.Select(line => $"{line.Number} {line.Statement}"));
        source.Append(NewLine);
        AppendProcedure(source, ImmediateEntryPointName, [statement]);
        return source.ToString();
    }

    // the lines as they are stored, indented or not: what completing a line is made of, which is asked as the line is typed - a program is not indented for it.
    private (int Number, string Statement)[] StoredLines()
    {
        lock (_gate)
        {
            return [.. _lines.Select(line => (line.Key, line.Value))];
        }
    }

    /// <summary>
    /// The module a line that is being typed at the prompt is completed in, and where the cursor is in it: the program with the line stored at its number, for a
    /// numbered line, and the module an immediate statement is run as (<see cref="ToImmediateModuleSource"/>) otherwise - what the line will be part of once it
    /// is entered. A leading <c>?</c> is the <c>Print</c> it expands to, as it will be.
    /// </summary>
    /// <param name="typed">The line, as it is typed so far.</param>
    /// <param name="cursor">Where the cursor is in <paramref name="typed"/>.</param>
    /// <returns>The module source, and the zero-based line and character of the cursor in it; <see langword="null"/> while the cursor is in a line number.</returns>
    public (string Source, int Line, int Character)? ToCompletionSource(string typed, int cursor)
    {
        var start = typed.Length - typed.TrimStart().Length;
        var digits = start;
        while (digits < typed.Length && char.IsAsciiDigit(typed[digits]))
        {
            digits++;
        }

        if (digits > start && int.TryParse(typed[start..digits], out var number))
        {
            if (cursor <= digits)
            {
                return null;
            }

            var statementStart = digits;
            while (statementStart < typed.Length && char.IsWhiteSpace(typed[statementStart]))
            {
                statementStart++;
            }

            var (statement, character) = Expanded(typed[statementStart..], Math.Max(0, cursor - statementStart));
            var label = $"{number} ";
            var lines = new SortedDictionary<int, string>(StoredLines().ToDictionary(line => line.Number, line => line.Statement)) { [number] = statement };
            var source = new StringBuilder();
            AppendProcedure(source, EntryPointName, lines.Select(line => $"{line.Key} {line.Value}"));

            // the procedure's header is the line above the first of the program's.
            return (source.ToString(), 1 + lines.Keys.TakeWhile(key => key != number).Count(), label.Length + character);
        }

        var (immediate, at) = Expanded(typed[start..], Math.Max(0, cursor - start));

        // the program's procedure - its header, its lines, its end - and the line between the two, then the immediate procedure's header.
        var stored = StoredLines();
        return (ImmediateModuleSource(stored, immediate), stored.Length + 4, at);
    }

    // a statement with its leading `?` written as the Print it is, and where the cursor is once it is.
    private static (string Statement, int Cursor) Expanded(string statement, int cursor)
    {
        if (!statement.StartsWith('?'))
        {
            return (statement, Math.Min(cursor, statement.Length));
        }

        var rest = statement[1..];
        var skipped = 1 + rest.Length - rest.TrimStart().Length;
        var expanded = $"{ReplInputParser.PrintShorthandExpansion} {rest.TrimStart()}";
        var offset = ReplInputParser.PrintShorthandExpansion.Length + 1;
        return (expanded, cursor < skipped ? offset : Math.Min(expanded.Length, cursor - skipped + offset));
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
