using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// A line number below <c>0</c> (<c>RDC11036</c>), and a name with a non-breaking space in it (<c>RDC11037</c>).
/// </summary>
[TestClass]
public sealed class LineNumberNameAnalyzerTests
{
    private static string Module(string source) => $"Attribute VB_Name = \"Program\"\r\n{source.ReplaceLineEndings("\r\n")}\r\n";

    private static AnalyzerFinding[] Run(IModuleAnalyzer analyzer, string source)
    {
        var uri = TestUri.TestModuleUri();
        var parsed = new ModuleParser().Parse(uri, source);
        Assert.IsEmpty(parsed.SyntaxErrors, string.Join("; ", parsed.SyntaxErrors.Select(error => error.Verbose)));
        return [.. analyzer.Analyze(new ModuleAnalysisContext(uri, parsed, null)).OrderBy(finding => finding.Range.Start)];
    }

    private static int[] Lines(AnalyzerFinding[] findings) => [.. findings.Select(finding => finding.Range.Start.Line)];

    private static void AssertAre(AnalyzerFinding[] findings, RDCoreDiagnosticId id)
        => Assert.IsTrue(findings.All(finding => finding.Severity == DiagnosticSeverity.Warning && finding.Id == id), string.Join("; ", findings.Select(finding => finding.Message)));

    private static string Text(string source, AnalyzerFinding finding)
    {
        var line = source.ReplaceLineEndings("\n").Split('\n')[finding.Range.Start.Line];
        return line[finding.Range.Start.Character..finding.Range.End.Character];
    }

    #region RDC11036 a line number below 0

    [TestMethod]
    public void ANegativeLineNumber_AndAJumpToOne_AreReported()
    {
        var source = Module("Public Sub DoSomething()\n    GoTo -5\n    On Error GoTo -2\n    Exit Sub\n-5:\n    Resume -5\n10:\nEnd Sub");
        var findings = Run(new NegativeLineNumberAnalyzer(), source);

        CollectionAssert.AreEqual(new[] { 2, 3, 5, 6 }, Lines(findings), "the jumps and the definition, not line 10");
        AssertAre(findings, RDCoreDiagnosticId.NegativeLineNumber);
        Assert.AreEqual("-5", Text(source, findings[0]));
    }

    [TestMethod]
    public void OnErrorGoToMinusOne_NamesNoLine()
        => Assert.IsEmpty(Run(new NegativeLineNumberAnalyzer(), Module("Public Sub DoSomething()\n    On Error GoTo -1\n    On Error GoTo 0\n    GoTo 10\n10:\nEnd Sub")));

    #endregion

    #region RDC11037 a non-breaking space in a name

    private const char Nbsp = ' ';

    [TestMethod]
    public void ANameWithANonBreakingSpace_IsReported()
    {
        var source = Module($"Private my{Nbsp}Value As Long\nPrivate Const MAX{Nbsp}SIZE As Long = 1\nPublic Sub Do{Nbsp}It(ByVal some{Nbsp}Arg As Long)\n    Dim ordinary As Long\nRe{Nbsp}try:\nEnd Sub");
        var findings = Run(new NonBreakingSpaceInNameAnalyzer(), source);

        CollectionAssert.AreEqual(new[] { 1, 2, 3, 3, 5 }, Lines(findings), "a variable, a constant, a procedure, a parameter and a label");
        AssertAre(findings, RDCoreDiagnosticId.NonBreakingSpaceInName);
        Assert.AreEqual($"my{Nbsp}Value", Text(source, findings[0]));
        StringAssert.Contains(findings[0].Message, "'my Value'", "the name as it reads");
    }

    [TestMethod]
    public void OrdinaryNames_AreNot()
        => Assert.IsEmpty(Run(new NonBreakingSpaceInNameAnalyzer(), Module("Private myValue As Long\nPublic Sub DoIt(ByVal someArg As Long)\nRetry:\nEnd Sub")));

    #endregion
}
