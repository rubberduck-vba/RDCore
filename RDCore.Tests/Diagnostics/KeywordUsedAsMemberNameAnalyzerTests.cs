using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// An Enum member or a member of a user-defined type named with a keyword (<c>RDC01018</c>), in brackets or not, unless <c>RDC11033</c> or <c>RDC11034</c>
/// reports the name.
/// </summary>
[TestClass]
public sealed class KeywordUsedAsMemberNameAnalyzerTests
{
    private static AnalyzerFinding[] Run(params string[] lines)
    {
        var uri = TestUri.TestModuleUri();
        var parsed = new ModuleParser().Parse(uri, string.Join("\n", lines));
        Assert.IsEmpty(parsed.SyntaxErrors, string.Join("; ", parsed.SyntaxErrors.Select(error => error.Verbose)));
        var findings = new KeywordUsedAsMemberNameAnalyzer().Analyze(new ModuleAnalysisContext(uri, parsed, null)).OrderBy(finding => finding.Range.Start).ToArray();
        Assert.IsTrue(findings.All(finding => finding is { Id: RDCoreDiagnosticId.KeywordUsedAsMemberName, Severity: DiagnosticSeverity.Information }));
        return findings;
    }

    private static string[] Names(AnalyzerFinding[] findings) => [.. findings.Select(finding => finding.Message.Split('\'')[1])];

    [TestMethod]
    public void AMemberOfATypeNamedWithAKeyword_IsFound_InBracketsOrNot()
        => CollectionAssert.AreEqual(
            new[] { "Next", "Loop", "Select" },
            Names(Run("Private Type Node", "    Next As Long", "    Loop As String", "    [Select] As Long", "    Total As Long", "End Type")));

    [TestMethod]
    public void AnEnumMemberNamedWithAKeywordInBrackets_IsFound()
        => CollectionAssert.AreEqual(
            new[] { "Select", "Do", "Date" },
            Names(Run("Private Enum Clause", "    [Select] = 1", "    [Do]", "    [Date]", "    Total", "End Enum")));

    [TestMethod]
    public void ANameThatADivergenceDiagnosticReports_IsLeftToIt()
        => Assert.IsEmpty(Run("Private Enum Clause", "    Date = 1", "End Enum", "Private Type Node", "    Me As Long", "End Type"));

    [TestMethod]
    public void AVariableOrAProcedure_IsNotAMember()
        => Assert.IsEmpty(Run("Private Total As Long", "Public Sub Work()", "End Sub"));

    [TestMethod]
    public void TheFix_RenamesTheMember()
    {
        var finding = Run("Private Type Node", "    Next As Long", "End Type").Single();

        Assert.IsNotNull(finding.Fixes?.Rename);
        Assert.IsEmpty(finding.Fixes.Rename.Suggestions);
    }
}
