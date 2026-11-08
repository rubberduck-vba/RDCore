using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Model.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC11025</c>: an array given a second empty argument list, <c>f()()</c>, which MS-VBAL §5.6.13.1 makes invalid and MS-VBA compiles, raising 9 when it
/// runs (Excel-measured, oracle\s53 E07, E08, F07, F08).
/// </summary>
[TestClass]
public sealed class ArrayGivenASecondEmptyArgumentListAnalyzerTests
{
    private static async Task<AnalyzerFinding[]> FindingsAsync(string statement)
    {
        var program = $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nDim f(2) As Long, a() As Long, v As Variant\r\n{statement}\r\nEnd Sub\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        return [.. new ArrayGivenASecondEmptyArgumentListAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, program), model))];
    }

    [TestMethod]
    [DataRow("Debug.Print UBound(f()())", DisplayName = "a fixed-size array")]
    [DataRow("v = a()()", DisplayName = "a dynamic array")]
    public async Task AnArrayGivenASecondEmptyArgumentList_IsAWarning(string statement)
    {
        var finding = Assert.ContainsSingle(await FindingsAsync(statement));

        Assert.AreEqual(RDCoreDiagnosticId.ArrayGivenASecondEmptyArgumentList, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
    }

    [TestMethod]
    [DataRow("Debug.Print UBound(f())", DisplayName = "one empty argument list")]
    [DataRow("v = f(1)", DisplayName = "an element")]
    [DataRow("Debug.Print UBound(f)", DisplayName = "the array")]
    public async Task AnythingElse_IsNotReported(string statement)
        => Assert.IsEmpty(await FindingsAsync(statement));
}
