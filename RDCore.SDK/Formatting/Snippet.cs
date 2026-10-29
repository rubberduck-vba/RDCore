using System.Text;

namespace RDCore.SDK.Formatting;

/// <summary>
/// A place of a snippet the cursor stops at (<strong>LSP 3.17</strong> §Snippet Syntax, <em>Tabstops</em> and <em>Placeholders</em>): a tab stop, and the text
/// of the snippet it is, which is its placeholder's - empty for a tab stop that has none.
/// </summary>
/// <param name="Index">
/// Its number: the cursor goes from one to the next in their order, and <c>0</c>, where the cursor is once the snippet is done, last. The places that have the
/// same number are the same tab stop, which an editor changes together.
/// </param>
/// <param name="Start">Where it is in <see cref="Snippet.Text"/>.</param>
/// <param name="Length">How long its placeholder is in <see cref="Snippet.Text"/>.</param>
/// <param name="Parent">
/// The place of the placeholder that holds it, by where that one is in <see cref="Snippet.TabStops"/>; <c>-1</c> when it is in none. A place in another is in
/// its text whatever is typed there, and goes with it when that text is typed over.
/// </param>
public readonly record struct SnippetTabStop(int Index, int Start, int Length, int Parent = -1);

/// <summary>
/// The text a snippet is inserted as, and the places in it an editor's cursor stops at (<strong>LSP 3.17</strong> §Snippet Syntax): what a completion item whose
/// <c>insertTextFormat</c> is <c>Snippet</c> inserts.
/// </summary>
/// <remarks>
/// <para>
/// A tab stop (<c>$1</c>, <c>${1}</c>) is where the cursor stops; a placeholder (<c>${1:name}</c>) is a tab stop with the text it starts with, which may hold
/// other tab stops (<c>${1:Dim ${2:x}}</c>). A choice (<c>${1|ByVal,ByRef|}</c>) is the placeholder of its first option. A tab stop whose number is used more
/// than once is one tab stop in several places: each of them shows the placeholder it is given in any of them - the first one, in the order of the text - with
/// the tab stops that placeholder holds. A transform (<c>${1/(.*)/$1/}</c>) is not made: the tab stop is the one it transforms.
/// </para>
/// <para>
/// A variable (<c>$TM_SELECTED_TEXT</c>, <c>${TM_FILENAME:default}</c>) is its value, or its default when it has none. The variables an editor is said to know
/// (<c>TM_SELECTED_TEXT</c>, <c>TM_CURRENT_LINE</c>, <c>TM_CURRENT_WORD</c>, <c>TM_LINE_INDEX</c>, <c>TM_LINE_NUMBER</c>, <c>TM_FILENAME</c>,
/// <c>TM_FILENAME_BASE</c>, <c>TM_DIRECTORY</c>, <c>TM_FILEPATH</c>) are empty unless the caller says what they are; a variable that is none of them and that the
/// caller does not know, with no default, is its name, a tab stop of its own after the numbered ones, as the protocol has it. A variable's transform is not made.
/// </para>
/// <para>
/// A <c>\</c> escapes a <c>$</c>, a <c>}</c> or a <c>\</c> - and, in a choice, a <c>,</c> or a <c>|</c>. What does not read as a tab stop, a placeholder, a
/// choice or a variable - a placeholder that is not closed, a choice with a <c>|</c> in an option - is the text it is.
/// </para>
/// </remarks>
/// <param name="Text">The text the snippet is inserted as.</param>
/// <param name="TabStops">The places of its tab stops in <see cref="Text"/>, in the order of the text.</param>
public sealed record class Snippet(string Text, IReadOnlyList<SnippetTabStop> TabStops)
{
    private static readonly HashSet<string> KnownVariables =
    [
        "TM_SELECTED_TEXT", "TM_CURRENT_LINE", "TM_CURRENT_WORD", "TM_LINE_INDEX", "TM_LINE_NUMBER", "TM_FILENAME", "TM_FILENAME_BASE", "TM_DIRECTORY", "TM_FILEPATH",
    ];

    /// <summary>
    /// The most placeholders a snippet has in one another - written so, or shown so through the tab stops they hold - before it is no snippet to insert. A tab
    /// stop in a placeholder of its own, at any depth (<c>${1:a $2} ${2:b $1}</c>), shows nothing there.
    /// </summary>
    public const int MostNested = 16;

    // the most characters a transform is read through before it is not one: a transform is a few characters of a pattern and a format.
    private const int MostTransform = 1_024;

    /// <summary>The most characters a snippet is written as; one whose placeholders, shown in one another, make more is not a snippet to insert.</summary>
    public const int MostText = 65_536;

    /// <summary>The most places of tab stops a snippet has once it is written.</summary>
    public const int MostTabStops = 4_096;

    /// <summary>Whether the snippet is more than one line.</summary>
    public bool IsMultiline => Text.Contains('\n') || Text.Contains('\r');

    [ThreadStatic]
    private static long t_steps;

    /// <summary>
    /// The work the last <see cref="TryParse"/> of this thread did: each character the reading moved past, as many times as it was read, each node it walked,
    /// and each character it wrote. What a guard of the parse's work counts, where a clock would count the machine.
    /// </summary>
    internal static long StepsOfLastParse => t_steps;

    /// <summary>
    /// The text and the tab stops of a snippet.
    /// </summary>
    /// <param name="snippet">The snippet, as <strong>LSP 3.17</strong> writes it.</param>
    /// <param name="variables">
    /// The value of a variable by its name; <see langword="null"/> for one the caller does not know. Without it, the variables an editor is said to know are
    /// empty, and the others are not known.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="snippet"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The snippet, written, is more than <see cref="MostText"/> characters or <see cref="MostTabStops"/> places of tab stops (<see cref="TryParse"/>).
    /// </exception>
    public static Snippet Parse(string snippet, Func<string, string?>? variables = null)
        => TryParse(snippet, out var parsed, variables)
            ? parsed
            : throw new ArgumentException($"The snippet is more than {MostText} characters, or {MostTabStops} places of tab stops, once it is written.", nameof(snippet));

    /// <summary>
    /// The text and the tab stops of a snippet, unless it is larger once it is written - its placeholders shown in one another - than a snippet to insert is:
    /// <see cref="MostText"/> characters, <see cref="MostTabStops"/> places of tab stops.
    /// </summary>
    /// <param name="snippet">The snippet, as <strong>LSP 3.17</strong> writes it.</param>
    /// <param name="parsed">The snippet; <see langword="null"/> when it is larger.</param>
    /// <param name="variables">The value of a variable by its name, as for <see cref="Parse"/>.</param>
    /// <returns>Whether the snippet is one to insert.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snippet"/> is <see langword="null"/>.</exception>
    public static bool TryParse(string snippet, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Snippet? parsed, Func<string, string?>? variables = null)
    {
        ArgumentNullException.ThrowIfNull(snippet);
        parsed = null;
        t_steps = 0;

        // a snippet longer than one to insert, or with more placeholders in one another than one has, is not read: nothing larger than it is walked.
        if (snippet.Length > MostText || new Reader(snippet).ReadAll() is not { } nodes)
        {
            return false;
        }

        var placeholders = new Dictionary<int, IReadOnlyList<Node>>();
        var unknown = new List<string>();
        Collect(nodes, placeholders, unknown, [], variables);

        // a variable that is not known is a tab stop of its own, after the numbered ones.
        // past the largest number a tab stop can have, a variable that is not known is its name and no tab stop.
        long last = Numbered(nodes).DefaultIfEmpty(0).Max();
        var unknownStops = unknown.Select((name, order) => (name, index: last + 1 + order)).Where(each => each.index <= int.MaxValue)
            .ToDictionary(each => each.name, each => (int)each.index, StringComparer.Ordinal);

        var text = new StringBuilder();
        var stops = new List<SnippetTabStop>();
        if (!new Writer(text, stops, placeholders, unknownStops, variables).Write(nodes, [], parent: -1))
        {
            return false;
        }

        // in the order of the text, a placeholder before the places it holds; each place's parent where that one is now.
        var ordered = stops.Select((stop, written) => (stop, written)).OrderBy(each => each.stop.Start).ThenBy(each => each.written).ToList();
        var moved = new int[stops.Count];
        for (var at = 0; at < ordered.Count; at++)
        {
            moved[ordered[at].written] = at;
        }

        parsed = new Snippet(text.ToString(), [.. ordered.Select(each => each.stop.Parent < 0 ? each.stop : each.stop with { Parent = moved[each.stop.Parent] })]);
        return true;
    }

    // the placeholder each tab stop shows - the first one given, in the order of the text, a variable's default included - and the variables not known, in order.
    private static void Collect(IReadOnlyList<Node> nodes, Dictionary<int, IReadOnlyList<Node>> placeholders, List<string> unknown, HashSet<string> seen, Func<string, string?>? variables)
    {
        foreach (var node in nodes)
        {
            t_steps++;
            switch (node)
            {
                case StopNode { Content: { } content } stop:
                    // an empty placeholder is none: the text of the tab stop is the one given elsewhere, when one is (${1:} ${1:x} is "x x").
                    if (content.Count > 0)
                    {
                        placeholders.TryAdd(stop.Index, content);
                    }

                    Collect(content, placeholders, unknown, seen, variables);
                    break;
                case VariableNode { Default: { } fallback }:
                    Collect(fallback, placeholders, unknown, seen, variables);
                    break;
                case VariableNode variable when ValueOf(variable.Name, variables) is null && seen.Add(variable.Name):
                    unknown.Add(variable.Name);
                    break;
            }
        }
    }

    private static IEnumerable<int> Numbered(IReadOnlyList<Node> nodes)
    {
        foreach (var node in nodes)
        {
            t_steps++;
            switch (node)
            {
                case StopNode stop:
                    yield return stop.Index;
                    foreach (var index in stop.Content is { } content ? Numbered(content) : [])
                    {
                        yield return index;
                    }

                    break;
                case VariableNode { Default: { } fallback }:
                    foreach (var index in Numbered(fallback))
                    {
                        yield return index;
                    }

                    break;
            }
        }
    }

    // the value of a variable: what the caller says, empty for one an editor is said to know, nothing for another.
    private static string? ValueOf(string name, Func<string, string?>? variables)
        => variables?.Invoke(name) ?? (KnownVariables.Contains(name) ? string.Empty : null);

    private abstract record class Node;

    private sealed record class TextNode(string Text) : Node;

    private sealed record class StopNode(int Index, IReadOnlyList<Node>? Content) : Node;

    private sealed record class VariableNode(string Name, IReadOnlyList<Node>? Default) : Node;

    // the text of the nodes, each tab stop showing its placeholder; a tab stop in a placeholder it is showing, at any depth, shows nothing there.
    private sealed class Writer(StringBuilder text, List<SnippetTabStop> stops, Dictionary<int, IReadOnlyList<Node>> placeholders, Dictionary<string, int> unknown,
        Func<string, string?>? variables)
    {
        // false once the text or its tab stops are more than a snippet to insert has: the writing stops there.
        public bool Write(IReadOnlyList<Node> nodes, IReadOnlyList<int> showing, int parent)
        {
            foreach (var node in nodes)
            {
                t_steps++;
                switch (node)
                {
                    case TextNode plain:
                        t_steps += plain.Text.Length;
                        text.Append(plain.Text);
                        break;
                    case StopNode stop:
                        // the place is written before the places its placeholder holds, which are in it.
                        var start = text.Length;
                        var slot = stops.Count;
                        stops.Add(new SnippetTabStop(stop.Index, start, 0, parent));
                        if (placeholders.TryGetValue(stop.Index, out var placeholder) && !showing.Contains(stop.Index))
                        {
                            // placeholders shown in one another deeper than a snippet has them are no snippet to insert: no text is dropped to show them.
                            if (showing.Count >= MostNested || !Write(placeholder, [.. showing, stop.Index], slot))
                            {
                                return false;
                            }
                        }

                        stops[slot] = stops[slot] with { Length = text.Length - start };
                        break;
                    case VariableNode variable:
                        var value = ValueOf(variable.Name, variables);
                        if (value is { Length: > 0 })
                        {
                            t_steps += value.Length;
                            text.Append(value);
                        }
                        else if (variable.Default is { } fallback)
                        {
                            if (!Write(fallback, showing, parent))
                            {
                                return false;
                            }
                        }
                        else if (value is null)
                        {
                            if (unknown.TryGetValue(variable.Name, out var index))
                            {
                                stops.Add(new SnippetTabStop(index, text.Length, variable.Name.Length, parent));
                            }

                            t_steps += variable.Name.Length;
                            text.Append(variable.Name);
                        }

                        break;
                }

                if (text.Length > MostText || stops.Count > MostTabStops)
                {
                    return false;
                }
            }

            return true;
        }
    }

    // the grammar of LSP 3.17, read in one pass: a placeholder or a default is opened, held while what it holds is read, and closed at its brace. One that is
    // not closed when the snippet ends is its text - its opening as written, then what it holds - in what is around it. A construct that is read whole (a choice,
    // a transform) and does not read is its text from its '$', and the reading goes on from the character after it.
    private sealed class Reader(string snippet)
    {
        private sealed class Open(int start, int content, int? index, string? name)
        {
            public int Start { get; } = start;
            public int Content { get; } = content;
            public int? Index { get; } = index;
            public string? Name { get; } = name;
            public List<Node> Nodes { get; } = [];
        }

        private readonly Stack<Open> _open = new();
        private readonly List<Node> _root = [];
        private int _position;

        // where the reading is: each character it moves past is a step of the parse (StepsOfLastParse), as many times as it is read.
        private int Position
        {
            get => _position;
            set
            {
                if (value > _position)
                {
                    t_steps += value - _position;
                }

                _position = value;
            }
        }

        private List<Node> Current => _open.Count > 0 ? _open.Peek().Nodes : _root;

        // nothing when the snippet has more placeholders or defaults open in one another than a snippet has (MostNested).
        public IReadOnlyList<Node>? ReadAll()
        {
            while (Position < snippet.Length)
            {
                var character = snippet[Position];
                if (character == '\\' && Position + 1 < snippet.Length && snippet[Position + 1] is '$' or '}' or '\\')
                {
                    Current.Add(new TextNode(snippet[Position + 1].ToString()));
                    Position += 2;
                }
                else if (character == '}' && _open.Count > 0)
                {
                    Position++;
                    var closed = _open.Pop();
                    Current.Add(closed.Index is { } index ? new StopNode(index, Merged(closed.Nodes)) : new VariableNode(closed.Name!, Merged(closed.Nodes)));
                }
                else if (_open.Count > MostNested)
                {
                    return null;
                }
                else if (character != '$' || !TryReadDollar())
                {
                    Current.Add(new TextNode(character.ToString()));
                    Position++;
                }
            }

            while (_open.Count > 0)
            {
                var unclosed = _open.Pop();
                t_steps += unclosed.Nodes.Count;
                Current.Add(new TextNode(snippet[unclosed.Start..unclosed.Content]));
                Current.AddRange(unclosed.Nodes);
            }

            return Merged(_root);
        }

        // a construct at the '$': read, or opened; false, with nothing taken, when what follows the '$' is none.
        private bool TryReadDollar()
        {
            var start = Position;
            Position++;
            if (At(char.IsAsciiDigit))
            {
                if (ReadNumber() is { } index)
                {
                    Current.Add(new StopNode(index, null));
                    return true;
                }
            }
            else if (At(IsNameStart))
            {
                Current.Add(new VariableNode(ReadName(), null));
                return true;
            }
            else if (Next('{'))
            {
                if (At(char.IsAsciiDigit) ? ReadBracedStop(start) : At(IsNameStart) && ReadBracedVariable(start))
                {
                    return true;
                }
            }

            Position = start;
            return false;
        }

        private bool ReadBracedStop(int start)
        {
            if (ReadNumber() is not { } index)
            {
                return false;
            }

            if (Next('}'))
            {
                Current.Add(new StopNode(index, null));
                return true;
            }

            if (Next(':'))
            {
                _open.Push(new Open(start, Position, index, null));
                return true;
            }

            if (Next('|'))
            {
                if (ReadChoice() is { } first)
                {
                    Current.Add(new StopNode(index, [new TextNode(first)]));
                    return true;
                }

                return false;
            }

            // a transform of the tab stop is not made: the tab stop is the one it transforms.
            if (At('/') && SkipTransform())
            {
                Current.Add(new StopNode(index, null));
                return true;
            }

            return false;
        }

        private bool ReadBracedVariable(int start)
        {
            var name = ReadName();
            if (Next('}'))
            {
                Current.Add(new VariableNode(name, null));
                return true;
            }

            if (Next(':'))
            {
                _open.Push(new Open(start, Position, null, name));
                return true;
            }

            if (At('/') && SkipTransform())
            {
                Current.Add(new VariableNode(name, null));
                return true;
            }

            return false;
        }

        // '/' regex '/' format '/' options '}': read past, its closing brace with it; false when it is not one, or not one in MostTransform characters - each
        // transform is read through no further, and the snippet in no more time than its length.
        private bool SkipTransform()
        {
            Position++;
            _transformEnd = Math.Min(snippet.Length, Position + MostTransform);
            if (!SkipTo('/') || !SkipFormatTo('/'))
            {
                return false;
            }

            while (At(char.IsAsciiLetter))
            {
                Position++;
            }

            return Next('}');
        }

        // past the next unescaped character that is the one given, which is taken; false at the end of the snippet.
        private int _transformEnd;

        private bool SkipTo(char end)
        {
            while (Position < _transformEnd)
            {
                if (snippet[Position] == '\\' && Position + 1 < snippet.Length)
                {
                    Position += 2;
                    continue;
                }

                if (snippet[Position++] == end)
                {
                    return true;
                }
            }

            return false;
        }

        // a format - text, $1, ${1}, ${1:/upcase}, ${1:+if}, ${1:?if:else}, ${1:-else}, its braces nested - to the next unescaped character given outside them.
        private bool SkipFormatTo(char end)
        {
            var depth = 0;
            while (Position < _transformEnd)
            {
                var character = snippet[Position];
                if (character == '\\' && Position + 1 < snippet.Length)
                {
                    Position += 2;
                    continue;
                }

                Position++;
                if (character == '$' && At('{'))
                {
                    Position++;
                    depth++;
                }
                else if (character == '}' && depth > 0)
                {
                    depth--;
                }
                else if (character == end && depth == 0)
                {
                    return true;
                }
            }

            return false;
        }

        // the options of a choice - text, a ',' between two - to its closing "|}"; the first, or nothing when it is not one.
        private string? ReadChoice()
        {
            var options = new List<StringBuilder> { new() };
            while (Position < snippet.Length)
            {
                var character = snippet[Position];
                if (character == '\\' && Position + 1 < snippet.Length && snippet[Position + 1] is '$' or '}' or '\\' or ',' or '|')
                {
                    options[^1].Append(snippet[Position + 1]);
                    Position += 2;
                    continue;
                }

                if (character == '|')
                {
                    Position++;
                    return Next('}') ? options[0].ToString() : null;
                }

                if (character == ',')
                {
                    options.Add(new StringBuilder());
                }
                else
                {
                    options[^1].Append(character);
                }

                Position++;
            }

            return null;
        }

        private bool At(Func<char, bool> test) => Position < snippet.Length && test(snippet[Position]);

        private bool At(char character) => Position < snippet.Length && snippet[Position] == character;

        private bool Next(char character)
        {
            if (At(character))
            {
                Position++;
                return true;
            }

            return false;
        }

        // the digits at the position, as a number; nothing for one larger than a tab stop's number can be, which is text.
        private int? ReadNumber()
        {
            var start = Position;
            while (At(char.IsAsciiDigit))
            {
                Position++;
            }

            return int.TryParse(snippet.AsSpan(start, Position - start), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number)
                ? number
                : null;
        }

        private string ReadName()
        {
            var start = Position;
            while (At(character => char.IsAsciiLetterOrDigit(character) || character == '_'))
            {
                Position++;
            }

            return snippet[start..Position];
        }

        private static bool IsNameStart(char character) => char.IsAsciiLetter(character) || character == '_';

        // the text nodes next to each other, as one.
        private static List<Node> Merged(List<Node> nodes)
        {
            var merged = new List<Node>();
            var run = new StringBuilder();
            foreach (var node in nodes)
            {
                t_steps++;
                if (node is TextNode text)
                {
                    run.Append(text.Text);
                    continue;
                }

                if (run.Length > 0)
                {
                    merged.Add(new TextNode(run.ToString()));
                    run.Clear();
                }

                merged.Add(node);
            }

            if (run.Length > 0)
            {
                merged.Add(new TextNode(run.ToString()));
            }

            return merged;
        }
    }
}
