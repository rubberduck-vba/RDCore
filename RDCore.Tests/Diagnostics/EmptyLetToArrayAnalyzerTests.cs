using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Model.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC11016</c>: the <c>Empty</c> literal Let-assigned to an array, which compiles and raises 13 whenever it runs (Excel, <c>oracle\s46</c> F17a-b).
/// </summary>
[TestClass]
public sealed class EmptyLetToArrayAnalyzerTests
{
    private static async Task<AnalyzerFinding[]> FindingsAsync(string declarations, string body)
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" + declarations.Replace(" ;; ", "\r\n")
            + "\r\nPublic Sub Main()\r\nOn Error Resume Next\r\n" + body.Replace(" ;; ", "\r\n") + "\r\nEnd Sub\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        return [.. new EmptyLetToArrayAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, program), model))];
    }

    [TestMethod]
    [DataRow("Function FE() As Integer() ;; FE = Empty ;; End Function", "Dim r ;; r = FE()", DisplayName = "F17a the result of a function")]
    [DataRow("", "Dim a() As Integer ;; a = Empty", DisplayName = "F17b a variable")]
    [DataRow("", "Dim a() As Integer ;; a = Null", DisplayName = "s48 J01 Null")]
    public async Task EmptyLetToAnArray_IsReported(string declarations, string body)
    {
        var finding = Assert.ContainsSingle(await FindingsAsync(declarations, body));

        Assert.AreEqual(RDCoreDiagnosticId.EmptyLetToArray, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        StringAssert.StartsWith(finding.Message, body.Contains("Null") ? "Null" : "Empty");
    }

    [TestMethod]
    [DataRow("", "Dim v As Variant ;; v = Empty", DisplayName = "a Variant")]
    [DataRow("", "Dim n As Long ;; n = Empty", DisplayName = "a Long")]
    [DataRow("", "Dim a() As Integer, e As Variant ;; a = e", DisplayName = "a Variant variable, whose value is the run time's")]
    public async Task EmptyLetToAnythingElse_IsNotReported(string declarations, string body)
    {
        Assert.IsEmpty(await FindingsAsync(declarations, body));
    }
}
