using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC00213</c>: a line continuation between the words of a keyword of two words, read off the trivia of the parse.
/// </summary>
[TestClass]
public sealed class LineContinuationBetweenKeywordsAnalyzerTests
{
    private static AnalyzerFinding[] Run(string body)
    {
        var uri = new Uri("file:///c:/ws/Program.bas");
        var parsed = new ModuleParser().Parse(uri, $"Attribute VB_Name = \"Program\"\r\nPublic Sub Work()\r\n{body}\r\nEnd Sub\r\n");
        Assert.IsEmpty(parsed.SyntaxErrors, string.Join("; ", parsed.SyntaxErrors.Select(error => error.Verbose)));
        return [.. new LineContinuationBetweenKeywordsAnalyzer().Analyze(new ModuleAnalysisContext(uri, parsed, null)).OrderBy(finding => finding.Range.Start)];
    }

    [TestMethod]
    [DataRow("    If True Then\r\n    End _\r\n    If", "End If", 3, DisplayName = "End If")]
    [DataRow("    Exit _\r\n    Sub", "Exit Sub", 2, DisplayName = "Exit Sub")]
    [DataRow("    Do\r\n    Exit _\r\n    Do\r\n    Loop", "Exit Do", 3, DisplayName = "Exit Do")]
    public void AContinuationInsideAKeyword_IsReported(string body, string keyword, int line)
    {
        var finding = Assert.ContainsSingle(Run(body));

        Assert.AreEqual(RDCoreDiagnosticId.LineContinuationBetweenKeywords, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Information, finding.Severity);
        Assert.AreEqual(line, finding.Range.Start.Line, "the continuation, at the end of the first word's line");
        Assert.AreEqual(line + 1, finding.Range.End.Line);
        StringAssert.Contains(finding.Message, $"'{keyword}'");
    }

    [TestMethod]
    public void TwoContinuationsInsideOneKeyword_AreOneFinding()
    {
        var finding = Assert.ContainsSingle(Run("    If True Then\r\n    End _\r\n _\r\n    If"));

        Assert.AreEqual(3, finding.Range.Start.Line);
        Assert.AreEqual(5, finding.Range.End.Line, "over both continuations");
    }

    [TestMethod]
    public void TwoKeywordsSplitTheSameWay_AreTwoFindings()
    {
        var findings = Run("    If True Then\r\n    End _\r\n    If\r\n    If False Then\r\n    End _\r\n    If");

        CollectionAssert.AreEqual(new[] { 3, 6 }, findings.Select(finding => finding.Range.Start.Line).ToArray());
    }

    [TestMethod]
    public void TheEndOfTheProcedure_IsAKeywordToo()
    {
        var uri = new Uri("file:///c:/ws/Program.bas");
        var parsed = new ModuleParser().Parse(uri, "Attribute VB_Name = \"Program\"\r\nPublic Sub Work()\r\nEnd _\r\nSub\r\n");
        Assert.IsEmpty(parsed.SyntaxErrors);

        var finding = Assert.ContainsSingle(new LineContinuationBetweenKeywordsAnalyzer().Analyze(new ModuleAnalysisContext(uri, parsed, null)));
        StringAssert.Contains(finding.Message, "'End Sub'");
    }

    [TestMethod]
    [DataRow("    x = 1 + _\r\n        2", DisplayName = "a continuation between two tokens")]
    [DataRow("    x = 1 ' End _\r\n    ' If", DisplayName = "a keyword of two words in a comment")]
    [DataRow("    If True Then\r\n    End If", DisplayName = "a keyword on one line")]
    public void WhatIsNoKeywordSplitOverLines_IsNotReported(string body)
        => Assert.IsEmpty(Run(body));
}
