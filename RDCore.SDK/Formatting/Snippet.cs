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
public readonly record struct SnippetTabStop(int Index, int Start, int Length);

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

    // how deep placeholders are shown in one another before one shows nothing: ${1:a $2} ${2:b $1} is a loop.
    private const int MostNested = 16;

    /// <summary>Whether the snippet is more than one line.</summary>
    public bool IsMultiline => Text.Contains('\n') || Text.Contains('\r');

    /// <summary>
    /// The text and the tab stops of a snippet.
    /// </summary>
    /// <param name="snippet">The snippet, as <strong>LSP 3.17</strong> writes it.</param>
    /// <param name="variables">
    /// The value of a variable by its name; <see langword="null"/> for one the caller does not know. Without it, the variables an editor is said to know are
    /// empty, and the others are not known.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="snippet"/> is <see langword="null"/>.</exception>
    public static Snippet Parse(string snippet, Func<string, string?>? variables = null)
    {
        ArgumentNullException.ThrowIfNull(snippet);
        var nodes = new Reader(snippet).ReadAll();
        var placeholders = new Dictionary<int, IReadOnlyList<Node>>();
        var unknown = new List<string>();
        Collect(nodes, placeholders, unknown, variables);

        // a variable that is not known is a tab stop of its own, after the numbered ones.
        var last = Numbered(nodes).DefaultIfEmpty(0).Max();
        var unknownStops = unknown.Select((name, order) => (name, order)).ToDictionary(each => each.name, each => last + 1 + each.order, StringComparer.Ordinal);

        var text = new StringBuilder();
        var stops = new List<SnippetTabStop>();
        new Writer(text, stops, placeholders, unknownStops, variables).Write(nodes, []);
        return new Snippet(text.ToString(), [.. stops.Select((stop, order) => (stop, order)).OrderBy(each => each.stop.Start).ThenBy(each => each.order).Select(each => each.stop)]);
    }

    // the placeholder each tab stop shows - the first one given, in the order of the text, a variable's default included - and the variables not known, in order.
    private static void Collect(IReadOnlyList<Node> nodes, Dictionary<int, IReadOnlyList<Node>> placeholders, List<string> unknown, Func<string, string?>? variables)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case StopNode { Content: { } content } stop:
                    placeholders.TryAdd(stop.Index, content);
                    Collect(content, placeholders, unknown, variables);
                    break;
                case VariableNode { Default: { } fallback }:
                    Collect(fallback, placeholders, unknown, variables);
                    break;
                case VariableNode variable when ValueOf(variable.Name, variables) is null && !unknown.Contains(variable.Name):
                    unknown.Add(variable.Name);
                    break;
            }
        }
    }

    private static IEnumerable<int> Numbered(IReadOnlyList<Node> nodes)
    {
        foreach (var node in nodes)
        {
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
        public void Write(IReadOnlyList<Node> nodes, IReadOnlyList<int> showing)
        {
            foreach (var node in nodes)
            {
                switch (node)
                {
                    case TextNode plain:
                        text.Append(plain.Text);
                        break;
                    case StopNode stop:
                        var start = text.Length;
                        if (placeholders.TryGetValue(stop.Index, out var placeholder) && !showing.Contains(stop.Index) && showing.Count < MostNested)
                        {
                            Write(placeholder, [.. showing, stop.Index]);
                        }

                        stops.Add(new SnippetTabStop(stop.Index, start, text.Length - start));
                        break;
                    case VariableNode variable:
                        var value = ValueOf(variable.Name, variables);
                        if (value is { Length: > 0 })
                        {
                            text.Append(value);
                        }
                        else if (variable.Default is { } fallback)
                        {
                            Write(fallback, showing);
                        }
                        else if (value is null && unknown.TryGetValue(variable.Name, out var index))
                        {
                            stops.Add(new SnippetTabStop(index, text.Length, variable.Name.Length));
                            text.Append(variable.Name);
                        }

                        break;
                }
            }
        }
    }

    // the grammar of LSP 3.17, read with no more than it says: what does not read as one of its constructs is its text, from its '$'.
    private sealed class Reader(string snippet)
    {
        private int _position;

        public IReadOnlyList<Node> ReadAll()
        {
            var nodes = new List<Node>();
            ReadAny(nodes, nested: false);
            return Merged(nodes);
        }

        // the nodes up to the end of the snippet or - for a placeholder or a default - to its closing brace, which is taken; false when that brace is not there.
        private bool ReadAny(List<Node> nodes, bool nested)
        {
            while (_position < snippet.Length)
            {
                var character = snippet[_position];
                if (character == '\\' && _position + 1 < snippet.Length && snippet[_position + 1] is '$' or '}' or '\\')
                {
                    nodes.Add(new TextNode(snippet[_position + 1].ToString()));
                    _position += 2;
                }
                else if (character == '}' && nested)
                {
                    _position++;
                    return true;
                }
                else if (character == '$' && TryReadDollar() is { } construct)
                {
                    nodes.Add(construct);
                }
                else
                {
                    nodes.Add(new TextNode(character.ToString()));
                    _position++;
                }
            }

            return !nested;
        }

        private Node? TryReadDollar()
        {
            var start = _position;
            _position++;
            Node? read = null;
            if (At(char.IsAsciiDigit))
            {
                read = new StopNode(ReadNumber(), null);
            }
            else if (At(IsNameStart))
            {
                read = new VariableNode(ReadName(), null);
            }
            else if (Next('{'))
            {
                read = At(char.IsAsciiDigit) ? ReadBracedStop() : At(IsNameStart) ? ReadBracedVariable() : null;
            }

            if (read is null)
            {
                _position = start;
            }

            return read;
        }

        private StopNode? ReadBracedStop()
        {
            var index = ReadNumber();
            if (Next('}'))
            {
                return new StopNode(index, null);
            }

            if (Next(':'))
            {
                var content = new List<Node>();
                return ReadAny(content, nested: true) ? new StopNode(index, Merged(content)) : null;
            }

            if (Next('|'))
            {
                return ReadChoice() is { } first ? new StopNode(index, [new TextNode(first)]) : null;
            }

            // a transform of the tab stop is not made: the tab stop is the one it transforms.
            return At('/') && SkipTransform() ? new StopNode(index, null) : null;
        }

        private VariableNode? ReadBracedVariable()
        {
            var name = ReadName();
            if (Next('}'))
            {
                return new VariableNode(name, null);
            }

            if (Next(':'))
            {
                var content = new List<Node>();
                return ReadAny(content, nested: true) ? new VariableNode(name, Merged(content)) : null;
            }

            return At('/') && SkipTransform() ? new VariableNode(name, null) : null;
        }

        // '/' regex '/' format '/' options '}': read past, its closing brace with it; false when it is not one.
        private bool SkipTransform()
        {
            _position++;
            if (!SkipTo('/') || !SkipFormatTo('/'))
            {
                return false;
            }

            while (At(char.IsAsciiLetter))
            {
                _position++;
            }

            return Next('}');
        }

        // past the next unescaped character that is the one given, which is taken; false at the end of the snippet.
        private bool SkipTo(char end)
        {
            while (_position < snippet.Length)
            {
                if (snippet[_position] == '\\' && _position + 1 < snippet.Length)
                {
                    _position += 2;
                    continue;
                }

                if (snippet[_position++] == end)
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
            while (_position < snippet.Length)
            {
                var character = snippet[_position];
                if (character == '\\' && _position + 1 < snippet.Length)
                {
                    _position += 2;
                    continue;
                }

                _position++;
                if (character == '$' && At('{'))
                {
                    _position++;
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
            while (_position < snippet.Length)
            {
                var character = snippet[_position];
                if (character == '\\' && _position + 1 < snippet.Length && snippet[_position + 1] is '$' or '}' or '\\' or ',' or '|')
                {
                    options[^1].Append(snippet[_position + 1]);
                    _position += 2;
                    continue;
                }

                if (character == '|')
                {
                    _position++;
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

                _position++;
            }

            return null;
        }

        private bool At(Func<char, bool> test) => _position < snippet.Length && test(snippet[_position]);

        private bool At(char character) => _position < snippet.Length && snippet[_position] == character;

        private bool Next(char character)
        {
            if (At(character))
            {
                _position++;
                return true;
            }

            return false;
        }

        private int ReadNumber()
        {
            var start = _position;
            while (At(char.IsAsciiDigit))
            {
                _position++;
            }

            return int.TryParse(snippet.AsSpan(start, _position - start), out var number) ? number : int.MaxValue;
        }

        private string ReadName()
        {
            var start = _position;
            while (At(character => char.IsAsciiLetterOrDigit(character) || character == '_'))
            {
                _position++;
            }

            return snippet[start.._position];
        }

        private static bool IsNameStart(char character) => char.IsAsciiLetter(character) || character == '_';

        // the text nodes next to each other, as one.
        private static List<Node> Merged(List<Node> nodes)
        {
            var merged = new List<Node>();
            foreach (var node in nodes)
            {
                if (node is TextNode next && merged.Count > 0 && merged[^1] is TextNode last)
                {
                    merged[^1] = new TextNode(last.Text + next.Text);
                }
                else
                {
                    merged.Add(node);
                }
            }

            return merged;
        }
    }
}
