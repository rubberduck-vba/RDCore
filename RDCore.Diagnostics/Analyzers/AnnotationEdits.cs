using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;

namespace RDCore.Diagnostics.Analyzers;

// the comment an annotation is written in, and the edits of a fix that moves or removes it.
internal static class AnnotationEdits
{
    // the one-line comment that holds the annotation, and where the annotation starts in its text; none for an annotation that runs on past its line.
    public static (CommentTriviaNode Comment, int Offset)? CommentOf(ModuleParseResult parse, AnnotationTriviaNode annotation)
    {
        var at = annotation.SourceLocation.Range;
        var comment = parse.Trivia.OfType<CommentTriviaNode>().FirstOrDefault(candidate => candidate.SourceLocation.Uri == annotation.SourceLocation.Uri
            && candidate.SourceLocation.Range.Start <= at.Start && at.End <= candidate.SourceLocation.Range.End);
        if (comment?.SourceLocation.Range is not { } range || range.Start.Line != range.End.Line || at.Start.Line != at.End.Line)
        {
            return null;
        }

        var offset = at.Start.Character - range.Start.Character;
        return offset >= 0 && offset + (at.End.Character - at.Start.Character) <= comment.Value.Length ? (comment, offset) : null;
    }

    // the annotation as it is written, from its @ to the end of its arguments.
    public static string? TextOf(ModuleParseResult parse, AnnotationTriviaNode annotation)
        => CommentOf(parse, annotation) is var (comment, offset)
            ? comment.Value.Substring(offset, annotation.SourceLocation.Range.End.Character - annotation.SourceLocation.Range.Start.Character)
            : null;

    // the comment's lines when the annotation is all the comment says, else the annotation alone.
    public static SourceRange RemovalOf(ModuleParseResult parse, AnnotationTriviaNode annotation)
    {
        var at = annotation.SourceLocation.Range;
        return CommentOf(parse, annotation) is var (comment, offset)
            && comment.Value.Remove(offset, at.End.Character - at.Start.Character).Trim() == "'"
            && LineEdits.WholeLinesOf(parse, comment) is { } lines
                ? lines
                : at;
    }
}
