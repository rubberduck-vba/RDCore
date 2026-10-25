using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using System.Text;

namespace RDCore.CLI.App.Console;

/// <summary>
/// What can be written at the cursor of a line typed at a prompt: the items of a completion list.
/// </summary>
/// <param name="line">The line, as it is typed so far.</param>
/// <param name="cursor">Where the cursor is in it.</param>
/// <param name="token">Cancelled when the list is not needed any more: the word it was asked for is not being typed now.</param>
/// <returns>The items; none when nothing is to be offered there.</returns>
public delegate Task<IReadOnlyList<CompletionItem>> ConsoleCompletionSource(string line, int cursor, CancellationToken token);

/// <summary>
/// The line of a shell's prompt as an editor has it: typed and edited in place, with the lines entered before it a key away, and a completion list that drops
/// down under the word that is typed - the member list after a <c>.</c>, the names that complete a word as it is typed, or on <kbd>Ctrl</kbd>+<kbd>Space</kbd>.
/// </summary>
/// <remarks>
/// <para>
/// The keys are an editor's. A character is typed at the cursor; <kbd>Backspace</kbd> and <kbd>Delete</kbd> erase around it; the arrow keys, <kbd>Home</kbd> and
/// <kbd>End</kbd> move it, and with <kbd>Ctrl</kbd> the left and right arrows move it a word at a time. <kbd>Up</kbd> and <kbd>Down</kbd> go through the lines
/// entered before; <kbd>Esc</kbd> clears the line; <kbd>Enter</kbd> enters it.
/// </para>
/// <para>
/// The list opens on its own after a <c>.</c> or a <c>!</c>, and when a word starts to be typed; <kbd>Ctrl</kbd>+<kbd>Space</kbd> or <kbd>Ctrl</kbd>+<kbd>J</kbd>
/// opens it where the cursor is (the VBE's <em>Complete Word</em> and <em>List Members</em>). Asking for it never holds up what is typed: the keys go on being read
/// while the list is on its way, and the list that comes is narrowed to the word as it is by then. While it is open, <kbd>Up</kbd>, <kbd>Down</kbd>,
/// <kbd>PgUp</kbd> and <kbd>PgDn</kbd> move its selection, <kbd>Tab</kbd> puts the selected name in place of the word, and so does <kbd>Enter</kbd> - which enters
/// the line instead when the word is the name already. <kbd>Esc</kbd> closes it, and so does typing anything a name does not have in it.
/// </para>
/// <para>
/// The list is drawn with ANSI escape sequences, under the line, at the column of the word; an output that does not understand them gets no list, and a line
/// read as a whole (<see cref="IConsoleKeys.IsInteractive"/> is not) is read as it is.
/// </para>
/// </remarks>
/// <param name="keys">Where the keys are read.</param>
/// <param name="output">Where the line is drawn.</param>
/// <param name="width">The cells of a line of the console.</param>
/// <param name="complete">Where the completion lists come from; none are shown without it.</param>
/// <param name="border">What the list is drawn with.</param>
public sealed class ConsolePromptEditor(IConsoleKeys keys, TextWriter output, Func<int> width, ConsoleCompletionSource? complete = null, ConsoleBorder? border = null)
{
    private const string ClearToEnd = "\u001b[K";
    private const string ClearLine = "\u001b[2K";
    private const string ReverseVideo = "\u001b[7m";
    private const string NormalVideo = "\u001b[27m";
    private const string Faint = "\u001b[2m";
    private const string NormalIntensity = "\u001b[22m";

    // the fewest cells a description is wrapped to beside the column of the list; a list nearer the right edge than that has its description at the left one.
    private const int MinimumDescriptionWidth = 32;

    private readonly List<string> _history = [];
    private readonly IConsoleKeys _keys = keys;
    private readonly TextWriter _output = output;
    private readonly Func<int> _width = width;
    private readonly ConsoleCompletionSource? _complete = complete;
    private readonly ConsoleBorder _border = border ?? ConsoleBorder.Single;

    /// <summary>How many items of a completion list are in view at a time.</summary>
    public int ListHeight { get; init; } = 8;

    /// <summary>
    /// How many lines the description of the selected item takes under the list, at the most: its signature and its documentation, wrapped to the console.
    /// </summary>
    public int DescriptionHeight { get; init; } = 6;

    /// <summary>Whether the list opens on its own as a word starts to be typed; it always does after a <c>.</c> or a <c>!</c>.</summary>
    public bool CompleteWords { get; init; } = true;

    /// <summary>The lines entered so far, the last one last.</summary>
    public IReadOnlyList<string> History => _history;

    /// <summary>
    /// What is drawn under the line while it is edited, above a completion list: a line for each piece of work the shell shows the progress of, say. Given the
    /// cells of a line of the console, which each of them fits in.
    /// </summary>
    public Func<int, IReadOnlyList<string>>? Status { get; set; }

    /// <summary>
    /// What the drawing of the console is serialized on: the editor draws the line holding it, and whatever else draws at the console while a line is not being
    /// edited holds it too.
    /// </summary>
    public object Sync { get; } = new();

    /// <summary>Whether a line is being edited now: what is drawn of the shell's own is drawn under it then (<see cref="Status"/>), and not where the line is.</summary>
    public bool IsEditing => Volatile.Read(ref _editing) != 0;

    private int _editing;
    private TaskCompletionSource _invalidation = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Has the line drawn again, from any thread: what <see cref="Status"/> says has changed. Nothing is drawn while no line is being edited.
    /// </summary>
    public void Invalidate() => Interlocked.Exchange(ref _invalidation, new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();

    private Task Invalidation => Volatile.Read(ref _invalidation).Task;

    /// <summary>
    /// Reads a line.
    /// </summary>
    /// <param name="token">Stops waiting for a key.</param>
    /// <returns>The line entered, or <see langword="null"/> at the end of the input.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="token"/> was cancelled before the line was entered.</exception>
    public async Task<string?> ReadLineAsync(CancellationToken token)
    {
        if (!_keys.IsInteractive)
        {
            return _keys.ReadLine(token);
        }

        var line = new Line(this);
        Volatile.Write(ref _editing, 1);
        string entered;
        try
        {
            entered = await line.EditAsync(token);
        }
        finally
        {
            Volatile.Write(ref _editing, 0);
        }

        if (entered.Length > 0 && (_history.Count == 0 || _history[^1] != entered))
        {
            _history.Add(entered);
        }

        return entered;
    }

    private static bool IsNameCharacter(char character) => char.IsLetterOrDigit(character) || character == '_';

    private static string ToColumn(int column) => $"\u001b[{column}G";

    // one line, as it is edited: its text and its cursor, the list that is open on it, and what of both is drawn.
    private sealed class Line(ConsolePromptEditor editor)
    {
        private readonly StringBuilder _text = new();
        private int _cursor;
        private int _history;
        private string _draft = string.Empty;
        private int _start;

        private ConsoleCompletionList? _list;
        private int _anchor;
        private Task<IReadOnlyList<CompletionItem>>? _asked;
        private int _askedAnchor;
        private CancellationTokenSource? _asking;
        private int _drawnRows;
        private int _reservedRows;

        private TextWriter Output => editor._output;

        private string Text => _text.ToString();

        public async Task<string> EditAsync(CancellationToken token)
        {
            _history = editor._history.Count;
            Task<ConsoleKeyInfo>? key = null;
            var invalidated = editor.Invalidation;
            Draw();
            try
            {
                while (true)
                {
                    key ??= Task.Run(() => editor._keys.ReadKey(token), CancellationToken.None);

                    // a list that is there is shown before the next key is taken; what is drawn under the line is drawn again when it changes.
                    var done = _asked is { } asked ? await Task.WhenAny(asked, key, invalidated) : await Task.WhenAny(key, invalidated);
                    if (done == _asked)
                    {
                        Arrived(await _asked!);
                        Draw();
                        continue;
                    }

                    if (done == invalidated)
                    {
                        invalidated = editor.Invalidation;
                        Draw();
                        continue;
                    }

                    var pressed = await key;
                    key = null;
                    if (Press(pressed))
                    {
                        // the line is entered: nothing is left under it, for what is written after it is written there.
                        Close();
                        (_cursor, _start) = (_text.Length, 0);
                        Draw(entered: true);
                        Output.WriteLine();
                        return Text;
                    }

                    Draw();
                }
            }
            finally
            {
                _asking?.Cancel();
            }
        }

        // what a key does; whether the line is entered.
        private bool Press(ConsoleKeyInfo key)
        {
            var control = key.Modifiers.HasFlag(ConsoleModifiers.Control);
            if (_list is { IsEmpty: false } list)
            {
                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                        list.Move(-1);
                        return false;
                    case ConsoleKey.DownArrow:
                        list.Move(+1);
                        return false;
                    case ConsoleKey.PageUp:
                        list.Move(-list.Height);
                        return false;
                    case ConsoleKey.PageDown:
                        list.Move(+list.Height);
                        return false;
                    case ConsoleKey.Tab:
                        Commit();
                        return false;
                    case ConsoleKey.Enter when list.SelectedItem is { } item && !string.Equals(LabelOf(item), Word(), StringComparison.Ordinal):
                        Commit();
                        return false;
                    case ConsoleKey.Escape:
                        Close();
                        return false;
                }
            }

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    return true;
                case ConsoleKey.Escape:
                    _text.Clear();
                    _cursor = 0;
                    Close();
                    return false;
                case ConsoleKey.UpArrow:
                    Recall(-1);
                    return false;
                case ConsoleKey.DownArrow:
                    Recall(+1);
                    return false;
                case ConsoleKey.Spacebar or ConsoleKey.J when control:
                    Ask(WordStart());
                    return false;
                case ConsoleKey.Backspace:
                    if (_cursor > 0)
                    {
                        _text.Remove(--_cursor, 1);
                    }

                    Follow();
                    return false;
                case ConsoleKey.Delete:
                    if (_cursor < _text.Length)
                    {
                        _text.Remove(_cursor, 1);
                    }

                    Follow();
                    return false;
                case ConsoleKey.LeftArrow:
                    _cursor = control ? PreviousWord() : Math.Max(0, _cursor - 1);
                    Follow();
                    return false;
                case ConsoleKey.RightArrow:
                    _cursor = control ? NextWord() : Math.Min(_text.Length, _cursor + 1);
                    Follow();
                    return false;
                case ConsoleKey.Home:
                    _cursor = 0;
                    Follow();
                    return false;
                case ConsoleKey.End:
                    _cursor = _text.Length;
                    Follow();
                    return false;
            }

            if (key.KeyChar is var typed and not '\0' && !char.IsControl(typed))
            {
                // a word starts with a letter, or with a digit - the line number an argument can be - and the list is asked for once, as it starts.
                var startsWord = char.IsLetterOrDigit(typed) && (_cursor == 0 || !IsNameCharacter(_text[_cursor - 1]));
                _text.Insert(_cursor++, typed);
                if (typed is '.' or '!')
                {
                    Ask(_cursor);
                }
                else if (startsWord && editor.CompleteWords && _list is null && _asked is null)
                {
                    Ask(_cursor - 1);
                }
                else
                {
                    Follow();
                }
            }

            return false;
        }

        // the start of the word the cursor is in, or at the end of.
        private int WordStart()
        {
            var start = _cursor;
            while (start > 0 && IsNameCharacter(_text[start - 1]))
            {
                start--;
            }

            return start;
        }

        // what is typed of the word the list is for.
        private string Word() => _text.ToString(_anchor, Math.Max(0, _cursor - _anchor));

        private int PreviousWord()
        {
            var index = _cursor;
            while (index > 0 && !IsNameCharacter(_text[index - 1]))
            {
                index--;
            }

            while (index > 0 && IsNameCharacter(_text[index - 1]))
            {
                index--;
            }

            return index;
        }

        private int NextWord()
        {
            var index = _cursor;
            while (index < _text.Length && IsNameCharacter(_text[index]))
            {
                index++;
            }

            while (index < _text.Length && !IsNameCharacter(_text[index]))
            {
                index++;
            }

            return index;
        }

        private void Recall(int by)
        {
            var history = editor._history;
            if (history.Count == 0)
            {
                return;
            }

            if (_history == history.Count)
            {
                _draft = Text;
            }

            _history = Math.Clamp(_history + by, 0, history.Count);
            _text.Clear().Append(_history == history.Count ? _draft : history[_history]);
            _cursor = _text.Length;
            Close();
        }

        private void Ask(int anchor)
        {
            if (editor._complete is not { } complete)
            {
                return;
            }

            Close();
            _asking = new CancellationTokenSource();
            _askedAnchor = anchor;
            _asked = AskAsync(complete, Text, _cursor, _asking.Token);
        }

        private static async Task<IReadOnlyList<CompletionItem>> AskAsync(ConsoleCompletionSource complete, string text, int cursor, CancellationToken token)
        {
            try
            {
                return await complete(text, cursor, token);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // a list that cannot be had is no list: typing goes on.
                return [];
            }
        }

        private void Arrived(IReadOnlyList<CompletionItem> items)
        {
            _asked = null;
            if (!IsWordAt(_askedAnchor) || items.Count == 0)
            {
                return;
            }

            _anchor = _askedAnchor;
            _list = new ConsoleCompletionList(items, editor.ListHeight);
            _list.Filter(Word());
        }

        // the list goes with the word it is for while the cursor is in it, and nothing but a name is typed between its start and the cursor.
        private bool IsWordAt(int anchor)
        {
            if (_cursor < anchor || anchor > _text.Length)
            {
                return false;
            }

            for (var index = anchor; index < _cursor; index++)
            {
                if (!IsNameCharacter(_text[index]))
                {
                    return false;
                }
            }

            return true;
        }

        // the list follows what is typed: narrowed to the word, or closed once the cursor leaves it - and a list on its way is not wanted any more either.
        private void Follow()
        {
            if (_asked is not null && !IsWordAt(_askedAnchor))
            {
                Close();
            }

            if (_list is null)
            {
                return;
            }

            if (!IsWordAt(_anchor))
            {
                Close();
                return;
            }

            _list.Filter(Word());
        }

        private void Commit()
        {
            if (_list?.SelectedItem is not { } item)
            {
                return;
            }

            // the whole word is replaced, what is after the cursor of it too.
            var end = _cursor;
            while (end < _text.Length && IsNameCharacter(_text[end]))
            {
                end++;
            }

            var label = LabelOf(item);
            _text.Remove(_anchor, end - _anchor).Insert(_anchor, label);
            _cursor = _anchor + label.Length;
            Close();
        }

        private static string LabelOf(CompletionItem item) => item.InsertText ?? item.Label;

        private void Close()
        {
            _asking?.Cancel();
            _asking = null;
            _asked = null;
            _list = null;
        }

        // ---- drawing ----

        // the line, and what is under it - unless the line is entered, which leaves nothing under it.
        private void Draw(bool entered = false)
        {
            lock (editor.Sync)
            {
                var columns = Math.Max(10, editor._width());

                // the line scrolls to keep the cursor in it, when it is longer than the console is wide.
                var room = columns - 1;
                _start = Math.Clamp(_start, Math.Max(0, _cursor - room + 1), _cursor);
                var shown = Text.Substring(_start, Math.Min(room, _text.Length - _start));

                Output.Write('\r');
                Output.Write(shown);
                Output.Write(ClearToEnd);
                DrawUnder(columns, entered);
                Output.Write(ToColumn(_cursor - _start + 1));
                Output.Flush();
            }
        }

        // under the line: what the editor is given to show there (Status), and the list under it, at the column of its word - its border, the items in view, the
        // selected one in reverse video, and what the selected one is.
        private void DrawUnder(int columns, bool entered)
        {
            var rows = new List<string>();
            if (!entered && editor.Status?.Invoke(columns) is { } status)
            {
                rows.AddRange(status.Select(row => ToColumn(1) + row));
            }

            if (_list is { IsEmpty: false } list)
            {
                var border = editor._border;
                var labels = list.Items.Select(item => $"{GlyphOf(item.Kind)} {item.Label}").ToList();
                var inner = Math.Clamp(labels.Max(label => label.Length) + 2, 16, Math.Max(16, Math.Min(48, columns - 2)));
                var column = Math.Clamp(_anchor - _start, 0, Math.Max(0, columns - inner - 2));
                var margin = ToColumn(column + 1);

                var more = list.Top > 0 ? " ▲ " : string.Empty;
                rows.Add($"{margin}{border.TopLeft}{new string(border.Horizontal, inner - more.Length)}{more}{border.TopRight}");
                for (var index = list.Top; index < Math.Min(list.Items.Count, list.Top + list.Height); index++)
                {
                    var cell = ConsoleText.PadToWidth(" " + ConsoleText.Truncate(labels[index], inner - 1), inner);
                    rows.Add(index == list.Selected
                        ? $"{margin}{border.Vertical}{ReverseVideo}{cell}{NormalVideo}{border.Vertical}"
                        : $"{margin}{border.Vertical}{cell}{border.Vertical}");
                }

                var position = $" {list.Selected + 1}/{list.Items.Count} ";
                var below = list.Top + list.Height < list.Items.Count ? "▼" : border.Horizontal.ToString();
                var fill = Math.Max(0, inner - position.Length - 1);
                rows.Add($"{margin}{border.BottomLeft}{new string(border.Horizontal, fill)}{position}{below}{border.BottomRight}");

                rows.AddRange(DescriptionOf(list.SelectedItem, column, columns));
            }

            // the rows a list needs are made once, under the line - the console scrolls up when the line is at its bottom - and the cursor goes back up to it.
            if (rows.Count > _reservedRows)
            {
                Output.Write(new string('\n', rows.Count));
                Output.Write($"\u001b[{rows.Count}A");
                _reservedRows = rows.Count;
            }

            var drawn = Math.Max(rows.Count, _drawnRows);
            for (var index = 0; index < drawn; index++)
            {
                Output.Write("\u001b[1B\r");
                Output.Write(ClearLine);
                if (index < rows.Count)
                {
                    Output.Write(rows[index]);
                }
            }

            if (drawn > 0)
            {
                Output.Write($"\u001b[{drawn}A");
            }

            _drawnRows = rows.Count;
        }

        // what the selected item is, under the list: its signature, then what its documentation says of it, faint - each wrapped to the console, and no more lines
        // of them than the editor gives a description. A list that drops down near the right edge has its description start at the left one, where it has room.
        private IEnumerable<string> DescriptionOf(CompletionItem? item, int column, int columns)
        {
            if (item is null)
            {
                return [];
            }

            var start = columns - column - 1 >= MinimumDescriptionWidth ? column : 0;
            var width = Math.Max(1, columns - start - 1);
            var margin = ToColumn(start + 1);

            var documentation = item.Documentation is { } content ? content.HasMarkupContent ? content.MarkupContent?.Value : content.String : null;
            IEnumerable<string> lines =
            [
                .. item.Detail is { Length: > 0 } detail ? ConsoleText.Wrap(detail, width).Select(line => margin + line) : [],
                .. documentation is { Length: > 0 } text ? ConsoleText.Wrap(text, width).Select(line => $"{margin}{Faint}{line}{NormalIntensity}") : [],
            ];

            return lines.Take(editor.DescriptionHeight);
        }

        // what kind of name an item is, in one character the width of any other: the VBE has an icon for it.
        private static char GlyphOf(CompletionItemKind? kind) => kind switch
        {
            CompletionItemKind.Function => 'ƒ',
            CompletionItemKind.Method => 's',
            CompletionItemKind.Property => 'p',
            CompletionItemKind.Field => 'f',
            CompletionItemKind.Variable => 'v',
            CompletionItemKind.Constant => 'c',
            CompletionItemKind.Enum => 'E',
            CompletionItemKind.EnumMember => 'e',
            CompletionItemKind.Struct => 'T',
            CompletionItemKind.Class => 'C',
            CompletionItemKind.Interface => 'I',
            CompletionItemKind.Module => 'M',
            CompletionItemKind.Folder => 'P',
            CompletionItemKind.Event => '!',
            CompletionItemKind.Keyword => 'k',
            CompletionItemKind.Value => '#',
            CompletionItemKind.Color => '*',
            _ => '·',
        };
    }
}
