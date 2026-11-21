using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Refactoring;

namespace RDCore.LanguageServer.Services.Refactoring.ExtractMethod;

// What a refactoring reads of a module's text that knows VBA's syntax, over the SDK's lines of the text; and the edits of an extraction made on it.
internal static class ExtractionText
{
    // Whether the next character after the position that is not a space or a tab, on its line, is a statement separator: another statement follows on the line.
    public static bool StatementFollows(this TextLines text, SourcePosition position) => text.After(position).TrimStart(' ', '\t').StartsWith(':');

    // Where the code after a label written up to the position starts: after its colon, and the spaces and tabs after it, on its line.
    public static SourcePosition AfterLabel(this TextLines text, SourcePosition position)
    {
        var after = text.After(position);
        var colon = after.StartsWith(':') ? 1 : 0;
        return new SourcePosition(position.Line, position.Character + colon + (after.Length - colon - after[colon..].TrimStart(' ', '\t').Length));
    }

    // The text of a range with the edits made in it: each edit is within the range, and none overlaps another.
    public static string Of(this TextLines text, SourceRange range, IEnumerable<ExtractionTextEdit> edits) => text.Of(range, edits.Select(Of));

    // The module with the edits made, none of which overlaps another; and where in it each edit's text is written, in the order of the edits, as offsets.
    public static (string Text, IReadOnlyList<(int Start, int End)> Written) Apply(string text, IReadOnlyList<ExtractionTextEdit> edits)
    {
        var edited = TextLines.Apply(text, [.. edits.Select(Of)]);
        var lines = TextLines.Of(edited.Text);
        return (edited.Text, [.. edited.Written.Select(range => (lines.Offset(range.Start), lines.Offset(range.End)))]);
    }

    private static RefactorTextEdit Of(ExtractionTextEdit edit) => new() { Range = edit.Range, NewText = edit.NewText };
}
