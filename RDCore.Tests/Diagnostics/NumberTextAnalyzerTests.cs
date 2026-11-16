using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.Tests.Cli;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Diagnostics;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC11040</c> and <c>RDC11041</c>: <c>Str</c> and <c>Str$</c> given a value whose text MS-VBA writes otherwise than MS-VBAL §6.1.2.3.1.20: a number between
/// -1 and 1, with no zero before its decimal point, and an <c>Error</c> value, which raises 13. <c>RDC11042</c>: <c>Write #</c> of such a number
/// (MS-VBAL §5.4.5.9).
/// </summary>
[TestClass]
public sealed class NumberTextAnalyzerTests
{
    private static Task<AnalyzerFinding[]> FindingsAsync(string expression) => StatementFindingsAsync($"Debug.Print {expression}");

    private static async Task<AnalyzerFinding[]> StatementFindingsAsync(string statement)
    {
        var program = "Attribute VB_Name = \"Program\"\r\nPublic Sub Main(ByVal d As Double, ByVal c As Currency, ByVal v As Variant, ByVal n As Long, "
            + $"ByVal s As String)\r\n{statement}\r\nEnd Sub\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        return [.. new NumberTextAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, program), model))];
    }

    [TestMethod]
    [DataRow("Str(d)", "Str", DisplayName = "a Double")]
    [DataRow("Str$(c)", "Str$", DisplayName = "a Currency")]
    [DataRow("Str(v)", "Str", DisplayName = "a Variant")]
    [DataRow("Str(0.5)", "Str", DisplayName = "a literal between -1 and 1")]
    [DataRow("Str(-0.25)", "Str", DisplayName = "a negative literal")]
    [DataRow("Str((0.5))", "Str", DisplayName = "a literal in parentheses")]
    [DataRow("Str(Number:=d)", "Str", DisplayName = "a named argument")]
    public async Task AValueThatCanBeAFraction_IsAWarning(string expression, string name)
    {
        var finding = Assert.ContainsSingle(await FindingsAsync(expression));

        Assert.AreEqual(RDCoreDiagnosticId.StrOfAFraction, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        Assert.AreEqual(2, finding.Range.Start.Line);
        StringAssert.Contains(finding.Message, $"'{name}'");
    }

    [TestMethod]
    [DataRow("Str(n)", DisplayName = "a Long")]
    [DataRow("Str(s)", DisplayName = "a String")]
    [DataRow("Str(1.5)", DisplayName = "a literal past 1")]
    [DataRow("Str(0)", DisplayName = "zero")]
    [DataRow("Str(5)", DisplayName = "a whole number")]
    [DataRow("CStr(d)", DisplayName = "CStr")]
    public async Task AValueThatCannotBeAFraction_IsNotReported(string expression)
        => Assert.IsEmpty(await FindingsAsync(expression));

    [TestMethod]
    [DataRow("Str(CVErr(5))", "Str")]
    [DataRow("Str$(CVErr(2007))", "Str$")]
    public async Task AnErrorValue_IsAWarning_AndNotAFraction(string expression, string name)
    {
        var finding = Assert.ContainsSingle(await FindingsAsync(expression));

        Assert.AreEqual(RDCoreDiagnosticId.StrOfAnErrorValue, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        StringAssert.Contains(finding.Message, $"'{name}'");
    }

    [TestMethod]
    public async Task AWriteItemThatCanBeAFraction_IsAWarning_OnTheItem()
    {
        var findings = await StatementFindingsAsync("Write #1, n, d, s, 0.5, 2");

        Assert.HasCount(2, findings);
        Assert.IsTrue(findings.All(finding => finding.Id == RDCoreDiagnosticId.WriteOfAFraction && finding.Severity == DiagnosticSeverity.Warning));
        CollectionAssert.AreEqual(new[] { 13, 19 }, findings.Select(finding => finding.Range.Start.Character).ToArray(), "d and 0.5");
        StringAssert.Contains(findings[0].Message, "'Write #'");
    }

    [TestMethod]
    public async Task PrintOfAFraction_IsNotReported()
        => Assert.IsEmpty(await StatementFindingsAsync("Print #1, d, 0.5"));
}
