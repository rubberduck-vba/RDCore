using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00213</c>: a line continuation between the words of a keyword of two words, such as <c>End If</c> or <c>Exit Sub</c>.
/// </summary>
/// <remarks>
/// A line continuation is white space between two tokens (<strong>MS-VBAL §3.3</strong>), so the keyword is valid VBA. The finding is each keyword
/// broken this way, over every line continuation in it.
/// </remarks>
internal sealed class LineContinuationBetweenKeywordsAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        LineContinuationTriviaNode? first = null;
        LineContinuationTriviaNode? last = null;
        foreach (var continuation in context.ParseResult.Trivia.OfType<LineContinuationTriviaNode>()
            .Where(continuation => continuation.Keyword is not null)
            .OrderBy(continuation => continuation.SourceLocation.Range.Start))
        {
            // the continuations of one keyword follow each other: the next starts where the one before it ends.
            if (first is not null && last is not null
                && (continuation.Keyword != first.Keyword || continuation.SourceLocation.Range.Start.CompareTo(last.SourceLocation.Range.End) > 0))
            {
                yield return Finding(first, last);
                first = null;
            }

            first ??= continuation;
            last = continuation;
        }

        if (first is not null && last is not null)
        {
            yield return Finding(first, last);
        }
    }

    private static AnalyzerFinding Finding(LineContinuationTriviaNode first, LineContinuationTriviaNode last)
        => new(
            RDCoreDiagnosticId.LineContinuationBetweenKeywords.ToCode(),
            new SourceRange(first.SourceLocation.Range.Start, last.SourceLocation.Range.End),
            DiagnosticSeverity.Information,
            AnalyzerMessages.Format(RDCoreDiagnosticsResources.LineContinuationBetweenKeywords_Message, first.Keyword!));
}
