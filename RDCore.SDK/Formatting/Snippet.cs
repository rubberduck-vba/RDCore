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
/// than once is one tab stop in several places, its text that of the first of them that has a placeholder.
/// </para>
/// <para>
/// A variable (<c>$TM_SELECTED_TEXT</c>, <c>${TM_FILENAME:default}</c>) is its default, or nothing: the variables are the editor's, and none is known here. A
/// <c>\</c> escapes a <c>$</c>, a <c>}</c> or a <c>\</c> - and, in a choice, a <c>,</c> or a <c>|</c>. What cannot be read as a tab stop, a placeholder, a choice or
/// a variable is the text it is.
/// </para>
/// </remarks>
/// <param name="Text">The text the snippet is inserted as.</param>
/// <param name="TabStops">The places of its tab stops in <see cref="Text"/>, in the order of the text.</param>
public sealed record class Snippet(string Text, IReadOnlyList<SnippetTabStop> TabStops)
{
    /// <summary>Whether the snippet is more than one line.</summary>
    public bool IsMultiline => Text.Contains('\n') || Text.Contains('\r');

    /// <summary>
    /// The text and the tab stops of a snippet.
    /// </summary>
    /// <param name="snippet">The snippet, as <strong>LSP 3.17</strong> writes it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="snippet"/> is <see langword="null"/>.</exception>
    public static Snippet Parse(string snippet)
    {
        ArgumentNullException.ThrowIfNull(snippet);
        var reader = new Reader(snippet);
        var text = new StringBuilder();
        var stops = new List<SnippetTabStop>();
        reader.Read(text, stops, nested: false);

        // a tab stop that is in several places has the text of the first of them that has a placeholder, in all of them.
        var placeholders = new Dictionary<int, string>();
        foreach (var stop in stops.Where(stop => stop.Length > 0))
        {
            placeholders.TryAdd(stop.Index, text.ToString(stop.Start, stop.Length));
        }

        if (!stops.Any(stop => stop.Length == 0 && placeholders.ContainsKey(stop.Index)))
        {
            return new Snippet(text.ToString(), [.. stops.OrderBy(stop => stop.Start)]);
        }

        // the places without a placeholder take the text of the one that has: the text is written again, each place where it now is.
        var filled = new StringBuilder();
        var moved = new List<SnippetTabStop>();
        var written = 0;
        foreach (var stop in stops.OrderBy(stop => stop.Start).ThenByDescending(stop => stop.Length))
        {
            if (stop.Start < written)
            {
                // inside a placeholder already written: it is where that placeholder's text has it.
                moved.Add(stop with { Start = filled.Length - (written - stop.Start) });
                continue;
            }

            filled.Append(text, written, stop.Start - written);
            var value = stop.Length == 0 && placeholders.TryGetValue(stop.Index, out var placeholder) ? placeholder : text.ToString(stop.Start, stop.Length);
            moved.Add(new SnippetTabStop(stop.Index, filled.Length, value.Length));
            filled.Append(value);
            written = stop.Start + stop.Length;
        }

        filled.Append(text, written, text.Length - written);
        return new Snippet(filled.ToString(), [.. moved.OrderBy(stop => stop.Start)]);
    }

    private sealed class Reader(string snippet)
    {
        private int _position;

        // the text up to the end of the snippet, or - for what a placeholder holds - to its closing brace, which is taken.
        public void Read(StringBuilder text, List<SnippetTabStop> stops, bool nested)
        {
            while (_position < snippet.Length)
            {
                var character = snippet[_position];
                if (character == '\\' && _position + 1 < snippet.Length && snippet[_position + 1] is '$' or '}' or '\\')
                {
                    text.Append(snippet[_position + 1]);
                    _position += 2;
                }
                else if (character == '}' && nested)
                {
                    _position++;
                    return;
                }
                else if (character == '$' && TryReadDollar(text, stops))
                {
                    continue;
                }
                else
                {
                    text.Append(character);
                    _position++;
                }
            }
        }

        // a tab stop, a placeholder, a choice or a variable at the '$'; nothing taken when what follows it is none of them.
        private bool TryReadDollar(StringBuilder text, List<SnippetTabStop> stops)
        {
            var start = _position;
            _position++;
            if (_position < snippet.Length && char.IsAsciiDigit(snippet[_position]))
            {
                stops.Add(new SnippetTabStop(ReadNumber(), text.Length, 0));
                return true;
            }

            if (_position < snippet.Length && IsNameStart(snippet[_position]))
            {
                // a variable is the editor's, and none is known: it is nothing.
                ReadName();
                return true;
            }

            if (_position < snippet.Length && snippet[_position] == '{')
            {
                _position++;
                if (_position < snippet.Length && char.IsAsciiDigit(snippet[_position]))
                {
                    var index = ReadNumber();
                    var at = text.Length;
                    if (Next('}'))
                    {
                        stops.Add(new SnippetTabStop(index, at, 0));
                        return true;
                    }

                    if (Next(':'))
                    {
                        Read(text, stops, nested: true);
                        stops.Add(new SnippetTabStop(index, at, text.Length - at));
                        return true;
                    }

                    if (Next('|') && ReadChoice() is { } first)
                    {
                        text.Append(first);
                        stops.Add(new SnippetTabStop(index, at, first.Length));
                        return true;
                    }
                }
                else if (_position < snippet.Length && IsNameStart(snippet[_position]))
                {
                    ReadName();
                    if (Next('}'))
                    {
                        return true;
                    }

                    if (Next(':'))
                    {
                        // the default of a variable is its text, tab stops and all.
                        Read(text, stops, nested: true);
                        return true;
                    }
                }
            }

            // not one of them: the '$' is the text it is.
            _position = start;
            return false;
        }

        // the options of a choice, to its closing "|}"; the first of them, or nothing when the choice is not closed.
        private string? ReadChoice()
        {
            var first = new StringBuilder();
            var inFirst = true;
            while (_position < snippet.Length)
            {
                var character = snippet[_position];
                if (character == '\\' && _position + 1 < snippet.Length && snippet[_position + 1] is '$' or '}' or '\\' or ',' or '|')
                {
                    if (inFirst)
                    {
                        first.Append(snippet[_position + 1]);
                    }

                    _position += 2;
                    continue;
                }

                if (character == '|' && _position + 1 < snippet.Length && snippet[_position + 1] == '}')
                {
                    _position += 2;
                    return first.ToString();
                }

                if (character == ',')
                {
                    inFirst = false;
                }
                else if (inFirst)
                {
                    first.Append(character);
                }

                _position++;
            }

            return null;
        }

        private bool Next(char character)
        {
            if (_position < snippet.Length && snippet[_position] == character)
            {
                _position++;
                return true;
            }

            return false;
        }

        private int ReadNumber()
        {
            var start = _position;
            while (_position < snippet.Length && char.IsAsciiDigit(snippet[_position]))
            {
                _position++;
            }

            return int.TryParse(snippet.AsSpan(start, _position - start), out var number) ? number : int.MaxValue;
        }

        private void ReadName()
        {
            while (_position < snippet.Length && (char.IsAsciiLetterOrDigit(snippet[_position]) || snippet[_position] == '_'))
            {
                _position++;
            }
        }

        private static bool IsNameStart(char character) => char.IsAsciiLetter(character) || character == '_';
    }
}
