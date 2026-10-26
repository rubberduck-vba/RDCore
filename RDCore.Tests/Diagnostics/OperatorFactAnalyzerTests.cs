using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Model.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC11005</c>, <c>RDC11006</c> and <c>RDC11011</c>: analyzers that read what the host states about the operations of the code (<see cref="RDCore.SDK.Platform.Protocol.OperatorFactDto"/>).
/// </summary>
[TestClass]
public sealed class OperatorFactAnalyzerTests
{
    private static async Task<AnalyzerFinding[]> FindAsync(IModuleAnalyzer analyzer, string parameters, params string[] body)
    {
        var uri = TestUri.TestModuleUri();
        var source = $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main({parameters})\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], source);

        return [.. analyzer.Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), payload.Modules.Single()))];
    }

    [TestMethod]
    public async Task TheAdditionOfTwoStrings_IsAConcatenation_ThatTheConcatenationOperatorSays()
    {
        var finding = (await FindAsync(new AmbiguousConcatenationAnalyzer(), "ByVal s As String, ByVal t As String", "Dim r As String", "r = s + t")).Single();

        Assert.AreEqual(RDCoreDiagnosticId.AmbiguousConcatenation, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Information, finding.Severity);
        Assert.AreEqual(4, finding.Range.Start.Line);
    }

    [TestMethod]
    [DataRow("ByVal a As Long, ByVal b As Long", DisplayName = "two numbers")]
    [DataRow("ByVal a As String, ByVal b As Long", DisplayName = "a String and a number")]
    public async Task AnAdditionThatAdds_IsNotAConcatenation(string parameters)
    {
        Assert.IsEmpty(await FindAsync(new AmbiguousConcatenationAnalyzer(), parameters, "Dim r As Variant", "r = a + b"));
    }

    [TestMethod]
    public async Task TheConcatenationOperator_IsNotReported()
    {
        Assert.IsEmpty(await FindAsync(new AmbiguousConcatenationAnalyzer(), "ByVal s As String, ByVal t As String", "Dim r As String", "r = s & t"));
    }

    [TestMethod]
    public async Task ALogicalOperatorOverNumbers_WorksOnTheirBits()
    {
        var finding = (await FindAsync(new BitwiseSemanticsAnalyzer(), "ByVal a As Long, ByVal b As Long", "Dim r As Long", "r = a And b")).Single();

        Assert.AreEqual(RDCoreDiagnosticId.BitwiseOperator, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Hint, finding.Severity);
    }

    [TestMethod]
    public async Task ALogicalOperatorOverBooleans_IsLogic()
    {
        Assert.IsEmpty(await FindAsync(new BitwiseSemanticsAnalyzer(), "ByVal a As Boolean, ByVal b As Boolean", "Dim r As Boolean", "r = a And b"));
    }

    // what two Variants hold is not known: neither is whether the operator works on their bits.
    [TestMethod]
    public async Task ALogicalOperatorOverVariantsThatAreNotKnown_IsNotReported()
    {
        Assert.IsEmpty(await FindAsync(new BitwiseSemanticsAnalyzer(), "ByVal a As Variant, ByVal b As Variant", "Dim r As Variant", "r = a And b"));
    }

    [TestMethod]
    [DataRow("<")]
    [DataRow("=")]
    [DataRow(">=")]
    public async Task AVariantNumberComparedWithAVariantString_IsLessWhateverTheirValues(string op)
    {
        var finding = (await FindAsync(new VariantNumberStringComparisonAnalyzer(), "",
            "Dim a As Variant, b As Variant, r As Boolean", "a = 5", "b = \"4\"", $"r = a {op} b")).Single();

        Assert.AreEqual(RDCoreDiagnosticId.VariantNumberStringComparison, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        Assert.AreEqual(6, finding.Range.Start.Line);
    }

    [TestMethod]
    public async Task AVariantNumberComparedWithAVariantString_FromAnInputBox_IsReported()
    {
        var finding = (await FindAsync(new VariantNumberStringComparisonAnalyzer(), "",
            "Dim n As Variant, i As Variant", "n = InputBox(\"How many?\")", "i = 1", "If i <= n Then Debug.Print i")).Single();

        Assert.AreEqual(RDCoreDiagnosticId.VariantNumberStringComparison, finding.Id);
    }

    [TestMethod]
    [DataRow("Dim a As Long, b As Variant, r As Boolean", DisplayName = "a declared number")]
    [DataRow("Dim a As Variant, b As String, r As Boolean", DisplayName = "a declared String")]
    public async Task AComparisonWithADeclaredOperand_ComparesTheValues(string declarations)
    {
        Assert.IsEmpty(await FindAsync(new VariantNumberStringComparisonAnalyzer(), "", declarations, "a = 5", "b = \"4\"", "r = a < b"));
    }

    [TestMethod]
    public async Task TwoVariantNumbers_AreComparedAsNumbers()
    {
        Assert.IsEmpty(await FindAsync(new VariantNumberStringComparisonAnalyzer(), "",
            "Dim a As Variant, b As Variant, r As Boolean", "a = 5", "b = 4", "r = a < b"));
    }

    // RDC11013: MS-VBA compares a Variant number with a String as text, where MS-VBAL's table makes the String that is no number a type mismatch.
    [TestMethod]
    [DataRow("a = 10", "b = \"9\"", DisplayName = "a number")]
    [DataRow("a = #1/2/2020#", "b = \"x\"", DisplayName = "a Date")]
    [DataRow("a = True", "b = \"x\"", DisplayName = "a Boolean")]
    public async Task AVariantNumberComparedWithAString_IsComparedAsText(string first, string second)
    {
        var finding = (await FindAsync(new VariantComparedWithStringAsTextAnalyzer(), "",
            "Dim a As Variant, b As String, r As Boolean", first, second, "r = a < b")).Single();

        Assert.AreEqual(RDCoreDiagnosticId.VariantComparedWithStringAsText, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        Assert.AreEqual(6, finding.Range.Start.Line);
    }

    [TestMethod]
    [DataRow("Dim a As Variant, b As Variant, r As Boolean", "a = 5", DisplayName = "two Variants")]
    [DataRow("Dim a As Long, b As String, r As Boolean", "a = 5", DisplayName = "two declared types")]
    [DataRow("Dim a As Variant, b As String, r As Boolean", "a = \"5\"", DisplayName = "a Variant String")]
    public async Task AComparisonOfAnotherKind_IsNotComparedAsText(string declarations, string first)
    {
        Assert.IsEmpty(await FindAsync(new VariantComparedWithStringAsTextAnalyzer(), "", declarations, first, "b = \"4\"", "r = a < b"));
    }
}
