using RDCore.SDK.Model.Source;
using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;
using RDCore.Tests.Semantics.Flow;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// The plan of extracting lines of a procedure <c>Run</c>, as <see cref="FlowFixture"/> analyzes it.
/// </summary>
internal static class ExtractionFixture
{
    /// <summary>
    /// The plan of extracting the lines of the body from <paramref name="first"/> to <paramref name="last"/> (zero-based).
    /// </summary>
    public static ExtractionPlan Plan(
        string body, int first, int last, string header = "Public Sub Run(ByVal n As Long)", string end = "End Sub", bool optionExplicit = true, string declarations = "")
        => Plan(body, new SourcePosition(first, 0), new SourcePosition(last, 1), header, end, optionExplicit, declarations);

    /// <summary>
    /// The plan of extracting the body from <paramref name="start"/> to <paramref name="end"/>, lines zero-based from the body's first.
    /// </summary>
    public static ExtractionPlan Plan(
        string body, SourcePosition start, SourcePosition end, string header = "Public Sub Run(ByVal n As Long)", string close = "End Sub", bool optionExplicit = true,
        string declarations = "")
    {
        var analyzed = FlowFixture.Analyze(body, header, close, optionExplicit, declarations);
        var requested = new SourceRange(analyzed.FirstBodyLine + start.Line, start.Character, analyzed.FirstBodyLine + end.Line, end.Character);
        Assert.IsTrue(ExtractionSelection.TrySnap(analyzed.Procedure, analyzed.Instructions, requested, out var selection, out var refusal, out _), $"refused: {refusal}");
        return ExtractionAnalysis.Plan(selection!, analyzed.Graph, analyzed.Accesses, analyzed.Model, analyzed.Scopes);
    }
}
