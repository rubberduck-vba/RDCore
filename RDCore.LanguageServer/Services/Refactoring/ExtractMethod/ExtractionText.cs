using RDCore.SDK.Model.Source;

namespace RDCore.LanguageServer.Services.Refactoring.ExtractMethod;

// The text of a module, read and changed by the positions its syntax tree has: zero-based lines, and characters in them.
internal sealed class ExtractionText
{
    private readonly string _text;
    private readonly int[] _lineStarts;

    public ExtractionText(string text)
    {
        _text = text;
        NewLine = text.Contains("\r\n", StringComparison.Ordinal) || !text.Contains('\n') ? "\r\n" : "\n";
        var starts = new List<int> { 0 };
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n')
            {
                starts.Add(index + 1);
            }
        }

        _lineStarts = [.. starts];
    }

    // The line break the module is written with.
    public string NewLine { get; }

    public int OffsetOf(SourcePosition position)
        => Math.Min(_text.Length, _lineStarts[Math.Min(position.Line, _lineStarts.Length - 1)] + position.Character);

    public int LineOf(int offset)
    {
        var line = Array.BinarySearch(_lineStarts, offset);
        return line >= 0 ? line : ~line - 1;
    }

    public SourcePosition PositionOf(int offset)
    {
        var line = LineOf(offset);
        return new SourcePosition(line, offset - _lineStarts[line]);
    }

    public string Of(SourceRange range) => _text[OffsetOf(range.Start)..OffsetOf(range.End)];

    // What is written on a line before its first character that is not a space or a tab.
    public string IndentationOf(int line)
    {
        var start = _lineStarts[line];
        var end = start;
        while (end < _text.Length && _text[end] is ' ' or '\t')
        {
            end++;
        }

        return _text[start..end];
    }

    // Whether nothing but spaces and tabs is written between the start of the position's line and the position.
    public bool OnlyIndentationBefore(SourcePosition position) => IndentationOf(position.Line).Length >= position.Character;

    // Whether nothing but spaces and tabs is written after the position, up to the end of its line.
    public bool OnlySpaceAfter(SourcePosition position)
    {
        for (var index = OffsetOf(position); index < _text.Length && _text[index] is not ('\r' or '\n'); index++)
        {
            if (_text[index] is not (' ' or '\t'))
            {
                return false;
            }
        }

        return true;
    }

    // Whether the next character after the position that is not a space or a tab, on its line, is a statement separator: another statement follows on the line.
    public bool StatementFollows(SourcePosition position)
    {
        var index = OffsetOf(position);
        while (index < _text.Length && _text[index] is ' ' or '\t')
        {
            index++;
        }

        return index < _text.Length && _text[index] == ':';
    }

    // The start of the line after the position's, or the end of the text when it is the last.
    public SourcePosition StartOfNextLine(SourcePosition position)
        => position.Line + 1 < _lineStarts.Length ? new SourcePosition(position.Line + 1, 0) : new SourcePosition(position.Line, _text.Length - _lineStarts[position.Line]);

    // The text of a range with the edits made in it: each edit is within the range, and none overlaps another.
    public string Of(SourceRange range, IEnumerable<ExtractionTextEdit> edits)
    {
        var start = OffsetOf(range.Start);
        var text = Of(range);
        foreach (var edit in edits.OrderByDescending(edit => edit.Range.Start))
        {
            var from = OffsetOf(edit.Range.Start) - start;
            var to = OffsetOf(edit.Range.End) - start;
            text = string.Concat(text.AsSpan(0, from), edit.NewText, text.AsSpan(to));
        }

        return text;
    }

    // The module with the edits made, none of which overlaps another; and where in it each edit's text is written, in the order of the edits.
    public static (string Text, IReadOnlyList<(int Start, int End)> Written) Apply(string text, IReadOnlyList<ExtractionTextEdit> edits)
    {
        var module = new ExtractionText(text);
        var written = new (int Start, int End)[edits.Count];
        var result = new System.Text.StringBuilder(text.Length);
        var copied = 0;
        foreach (var index in Enumerable.Range(0, edits.Count).OrderBy(index => module.OffsetOf(edits[index].Range.Start)))
        {
            var from = module.OffsetOf(edits[index].Range.Start);
            result.Append(text, copied, from - copied);
            written[index] = (result.Length, result.Length + edits[index].NewText.Length);
            result.Append(edits[index].NewText);
            copied = module.OffsetOf(edits[index].Range.End);
        }

        result.Append(text, copied, text.Length - copied);
        return (result.ToString(), written);
    }
}
