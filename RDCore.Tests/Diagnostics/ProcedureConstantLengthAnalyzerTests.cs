using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Model.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC11021</c>: a <c>Const</c> of a procedure valued with <c>Len</c>, which MS-VBA compiles there and refuses in a module (Excel-measured, oracle\s44
/// C01-C04; HexIDE findings desk #2, by the author's ruling).
/// </summary>
[TestClass]
public sealed class ProcedureConstantLengthAnalyzerTests
{
    private static async Task<AnalyzerFinding[]> FindingsAsync(string declarations, string body)
    {
        var program = $"Attribute VB_Name = \"Program\"\r\n{declarations.Replace(" ;; ", "\r\n")}\r\nPublic Sub Main()\r\n{body.Replace(" ;; ", "\r\n")}\r\nEnd Sub\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        return [.. new ProcedureConstantLengthAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, program), model))];
    }

    [TestMethod]
    [DataRow("Const K2 = Len(\"abc\")", "K2", DisplayName = "C03 of a literal")]
    [DataRow("Const K = \"abcd\" ;; Const K3 = Len(K)", "K3", DisplayName = "C04 of a constant")]
    [DataRow("Const K = \"abcd\" ;; Const K5 = VBA.Len(K) + 1", "K5", DisplayName = "through its module, in an expression")]
    public async Task AConstOfAProcedureValuedWithLen_IsAWarning(string body, string name)
    {
        var finding = Assert.ContainsSingle(await FindingsAsync(string.Empty, body + " ;; Debug.Print " + name));

        Assert.AreEqual(RDCoreDiagnosticId.ProcedureConstantLength, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        StringAssert.Contains(finding.Message, $"'{name}'");
    }

    [TestMethod]
    public async Task ABoundOfAnArray_IsNotReported()
        => Assert.IsEmpty(await FindingsAsync(string.Empty, "Const K = \"abcd\" ;; Dim d(Len(K)) As Long ;; Debug.Print UBound(d)"));

    [TestMethod]
    public async Task AConstOfAProcedureValuedWithoutLen_IsNotReported()
        => Assert.IsEmpty(await FindingsAsync(string.Empty, "Const K = 4 ;; Debug.Print K"));
}
