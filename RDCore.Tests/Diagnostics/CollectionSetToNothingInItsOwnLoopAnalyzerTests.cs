using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Diagnostics.Model;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC11035</c>: a <c>Set</c> to <c>Nothing</c> of the variable a <c>For Each</c> walks, inside that loop, which RD-VBA leaves as it is while the loop runs,
/// where MS-VBA destroys a <c>Collection</c> and the loop ends (observed in Excel): a Warning at the variable the <c>Set</c> names.
/// </summary>
[TestClass]
public sealed class CollectionSetToNothingInItsOwnLoopAnalyzerTests
{
    private static async Task<(AnalyzerFinding[] Findings, string[] Lines)> FindingsAsync(params string[] body)
    {
        var program = string.Join("\r\n", [
            "Attribute VB_Name = \"Program\"",
            "Option Explicit",
            "Public Sub Main()",
            "    Dim C As Collection, D As Collection, V As Variant",
            "    Set C = New Collection",
            "    Set D = New Collection",
            .. body,
            "End Sub",
            ""]);
        var payload = await ModuleWorkspace.SemanticsAsync([], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        return ([.. new CollectionSetToNothingInItsOwnLoopAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, program), model))
            .OrderBy(finding => finding.Range.Start)], program.Split("\r\n"));
    }

    [TestMethod]
    [DataRow("        Set C = Nothing", DisplayName = "in the loop's body")]
    [DataRow("        If V Is Nothing Then Set C = Nothing", DisplayName = "in a block of the loop")]
    [DataRow("        For Each V In D\r\n            Set C = Nothing\r\n        Next", DisplayName = "in a loop inside it")]
    public async Task ACollectionSetToNothingInItsOwnLoop_IsAWarning(string set)
    {
        var (findings, lines) = await FindingsAsync("    For Each V In C", set, "    Next");

        var finding = Assert.ContainsSingle(findings);
        Assert.AreEqual(RDCoreDiagnosticId.CollectionSetToNothingInItsOwnLoop.ToCode(), finding.Code);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        Assert.AreEqual(string.Format(RDCoreDiagnosticsResources.CollectionSetToNothingInItsOwnLoop_Message, "C"), finding.Message);
        var line = lines[finding.Range.Start.Line];
        Assert.Contains("Set C = Nothing", line);
        Assert.AreEqual(line.IndexOf("C = Nothing", StringComparison.Ordinal), finding.Range.Start.Character, "at the variable the Set names");
    }

    [TestMethod]
    [DataRow(new[] { "    For Each V In C", "        Set D = Nothing", "    Next" }, DisplayName = "another variable, in the loop")]
    [DataRow(new[] { "    For Each V In D", "        Set C = Nothing", "    Next" }, DisplayName = "in a loop over another collection")]
    [DataRow(new[] { "    For Each V In C", "    Next", "    Set C = Nothing" }, DisplayName = "after the loop")]
    [DataRow(new[] { "    For Each V In C", "        Set C = New Collection", "    Next" }, DisplayName = "set to another object")]
    public async Task AnyOtherSet_IsNotReported(string[] body)
    {
        var (findings, _) = await FindingsAsync(body);

        Assert.IsEmpty(findings);
    }
}
