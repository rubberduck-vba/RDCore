using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Model.Diagnostics;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC11023</c>: a procedure of more than 5,000 statements, which MS-VBA refuses as "Procedure too large" (past 64K of compiled code: 5,000 to 5,250 of
/// <c>x = x + 1</c>, recorded in Excel, oracle\size-limits A, E, H) and RD-VBA runs.
/// </summary>
[TestClass]
public sealed class ProcedureTooLargeAnalyzerTests
{
    private static AnalyzerFinding[] Findings(params IEnumerable<string>[] body)
    {
        var program = "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n" + string.Join("\r\n", body.SelectMany(lines => lines)) + "\r\nEnd Sub\r\n";
        var uri = new Uri("file:///C:/Program.bas");
        return [.. new ProcedureTooLargeAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, program), null))];
    }

    private static IEnumerable<string> Statements(int count) => Enumerable.Repeat("    x = x + 1", count);

    [TestMethod]
    public void FiveThousandStatements_AreNotReported()
        => Assert.IsEmpty(Findings(Statements(ProcedureTooLargeAnalyzer.MaxStatements)));

    [TestMethod]
    public void TheFiveThousandAndFirst_IsAWarning_AtTheProcedure()
    {
        var finding = Assert.ContainsSingle(Findings(Statements(ProcedureTooLargeAnalyzer.MaxStatements + 1)));

        Assert.AreEqual(RDCoreDiagnosticId.ProcedureTooLarge, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        Assert.AreEqual(1, finding.Range.Start.Line);
        StringAssert.Contains(finding.Message, "'Main'");
        StringAssert.Contains(finding.Message, "Procedure too large");
    }

    [TestMethod]
    public void TheStatementsOfABlock_AreCounted()
        // an If block with 3,000 statements, and 2,000 after it: 5,001 statements.
        => Assert.ContainsSingle(Findings(["    If x = 0 Then"], Statements(3_000), ["    End If"], Statements(2_000)));

    [TestMethod]
    public void CommentsAreNoStatements()
        => Assert.IsEmpty(Findings(Enumerable.Repeat("    ' a comment", 6_000), Statements(10)));
}
