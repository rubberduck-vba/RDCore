using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Model.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC00114</c>: a <c>ReDim</c> that declares the array it re-dimensions, its name matching nothing, which only the host can tell.
/// </summary>
[TestClass]
public sealed class ImplicitArrayDeclarationAnalyzerTests
{
    private static async Task<AnalyzerFinding[]> AnalyzeAsync(params string[] body)
    {
        var source = $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPrivate Field() As Long\r\nPublic Sub Main()\r\n    {string.Join("\r\n    ", body)}\r\nEnd Sub\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], source);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");

        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        return [.. new ImplicitArrayDeclarationAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), model))];
    }

    [TestMethod]
    public async Task AReDimOfANameNothingDeclares_DeclaresItsArray_AndSaysSo()
    {
        var findings = await AnalyzeAsync("ReDim Totals(1 To 12)", "Totals(1) = 1");

        var finding = findings.Single();
        Assert.AreEqual(RDCoreDiagnosticId.ImplicitArrayDeclaration, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Information, finding.Severity);
        StringAssert.Contains(finding.Message, "'Totals'");
    }

    [TestMethod]
    [DataRow("Dim Totals() As Long", "ReDim Totals(1 To 12)", DisplayName = "a Dim before it")]
    [DataRow("ReDim Field(3)", "", DisplayName = "a variable of the module")]
    public async Task AReDimOfADeclaredArray_DeclaresNothing(string first, string second)
        => Assert.IsEmpty(await AnalyzeAsync(first, second));
}
