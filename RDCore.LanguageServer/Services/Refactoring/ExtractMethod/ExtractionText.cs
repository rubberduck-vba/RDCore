using RDCore.SDK.Model.Source;
using RDCore.SDK.Semantics.Refactoring;

namespace RDCore.LanguageServer.Services.Refactoring.ExtractMethod;

// What a refactoring reads of a module's text that knows VBA's syntax: a statement separator, and a label's colon. The rest of the reading is TextLines'.
internal static class ExtractionText
{
    // Whether the next character after the position that is not a space or a tab, on its line, is a statement separator: another statement follows on the line.
    public static bool StatementFollows(this TextLines lines, SourcePosition position) => lines.After(position).TrimStart(' ', '\t').StartsWith(':');

    // Where the code after a label written up to the position starts: after its colon, and the spaces and tabs after it, on its line.
    public static SourcePosition AfterLabel(this TextLines lines, SourcePosition position)
    {
        var after = lines.After(position);
        var index = after.StartsWith(':') ? 1 : 0;
        while (index < after.Length && after[index] is ' ' or '\t')
        {
            index++;
        }

        return new SourcePosition(position.Line, position.Character + index);
    }
}
