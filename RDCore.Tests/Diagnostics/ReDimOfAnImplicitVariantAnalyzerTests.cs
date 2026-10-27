using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Model.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC11022</c>: a <c>ReDim</c> of a name the procedure used before it with no declaration, which MS-VBAL and vb6.exe re-dimension and MS-VBA refuses at
/// compile time, "Invalid ReDim" (Excel-measured, oracle\findings-labels R01-R10; HexIDE findings desk #9, by the author's ruling).
/// </summary>
[TestClass]
public sealed class ReDimOfAnImplicitVariantAnalyzerTests
{
    private static async Task<AnalyzerFinding[]> FindingsAsync(string options, string body)
    {
        var program = $"Attribute VB_Name = \"Program\"\r\n{options}Public Sub Main()\r\n{body.Replace(" ;; ", "\r\n")}\r\nEnd Sub\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        return [.. new ReDimOfAnImplicitVariantAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, program), model))];
    }

    [TestMethod]
    [DataRow("y = 5 ;; ReDim y(2)", DisplayName = "assigned before")]
    [DataRow("Debug.Print TypeName(y) ;; ReDim y(2)", DisplayName = "read before")]
    [DataRow("If False Then y = 5 ;; ReDim y(2)", DisplayName = "assigned before, on a line that never runs")]
    [DataRow("y = 5 ;; If True Then ;; ReDim y(2) ;; End If", DisplayName = "a ReDim in a block")]
    public async Task AReDimOfANameUsedBefore_IsAWarning(string body)
    {
        var finding = Assert.ContainsSingle(await FindingsAsync(string.Empty, body));

        Assert.AreEqual(RDCoreDiagnosticId.ReDimOfAnImplicitVariant, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        StringAssert.Contains(finding.Message, "'y'");
    }

    [TestMethod]
    public async Task AReDimOfANameUsedOnlyAfter_IsNotReported()
        => Assert.IsEmpty(await FindingsAsync(string.Empty, "ReDim y(2) ;; y(1) = 5"));

    [TestMethod]
    public async Task AReDimOfADeclaredVariant_IsNotReported()
        => Assert.IsEmpty(await FindingsAsync(string.Empty, "Dim y ;; y = 5 ;; ReDim y(2)"));

    // every ReDim of the variable is one MS-VBA refuses, the later ones too: each is reported where it is.
    [TestMethod]
    public async Task TwoReDimsOfANameUsedBefore_AreEachReported()
        => Assert.HasCount(2, await FindingsAsync(string.Empty, "y = 5 ;; ReDim y(2) ;; ReDim y(3)"));
}
