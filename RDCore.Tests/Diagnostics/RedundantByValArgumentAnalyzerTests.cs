using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Model.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC00310</c>: an argument written <c>ByVal</c> for a parameter that is <c>ByVal</c> already, which <strong>MS-VBA</strong> compiles and where the keyword
/// changes nothing (RD-VBAL §5.6.13.1). Whether the parameter is <c>ByVal</c> is a fact the host vouches for: the code runs through it.
/// </summary>
[TestClass]
public sealed class RedundantByValArgumentAnalyzerTests
{
    private const string Declarations = "Option Explicit\n"
        + "Private Declare PtrSafe Sub Sleep Lib \"kernel32\" (ByVal ms As Long)\n"
        + "Private Declare PtrSafe Sub Fill Lib \"kernel32\" Alias \"RtlFillMemory\" (Destination As Any, ByVal Length As LongPtr, ByVal Fill As Byte)\n"
        + "Private Function Twice(ByVal n As Long) As Long\n    Twice = n * 2\nEnd Function\n";

    private static async Task<AnalyzerFinding[]> RunAsync(string body)
    {
        var source = $"Attribute VB_Name = \"Program\"\r\n{(Declarations + $"Public Sub Main()\n    Dim x As Long\n{body}\nEnd Sub").ReplaceLineEndings("\r\n")}\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], source);
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        var parsed = new ModuleParser().Parse(uri, source);
        return [.. new RedundantByValArgumentAnalyzer().Analyze(new ModuleAnalysisContext(uri, parsed, payload.Modules.Single())).OrderBy(finding => finding.Range.Start)];
    }

    [TestMethod]
    [DataRow("    Debug.Print Twice(ByVal 2)", DisplayName = "a workspace function's ByVal parameter")]
    [DataRow("    Debug.Print Twice(n:=ByVal 2)", DisplayName = "named")]
    [DataRow("    Debug.Print Left(\"abc\", ByVal 1)", DisplayName = "a library function's ByVal parameter")]
    [DataRow("    Sleep ByVal 0", DisplayName = "a Declare's ByVal parameter, in a call statement")]
    [DataRow("    Fill x, ByVal 4, 0", DisplayName = "a Declare's ByVal parameter after an As Any one")]
    public async Task ByValForAByValParameter_IsAHint(string body)
    {
        var findings = await RunAsync(body);

        Assert.HasCount(1, findings);
        Assert.AreEqual(RDCoreDiagnosticId.RedundantByValArgument, findings[0].Id);
        Assert.AreEqual(DiagnosticSeverity.Hint, findings[0].Severity);
        Assert.AreEqual(body.IndexOf("ByVal", StringComparison.Ordinal), findings[0].Range.Start.Character, "where ByVal is written");
    }

    [TestMethod]
    [DataRow("    Fill ByVal 0&, 4, 0", DisplayName = "a Declare's As Any parameter, where ByVal passes a value for an address")]
    [DataRow("    Debug.Print VarPtr(ByVal 0)", DisplayName = "VarPtr's As Any")]
    [DataRow("    Debug.Print Twice(2)", DisplayName = "no ByVal")]
    public async Task ByValThatMeansSomething_OrNoByVal_IsNoHint(string body)
        => Assert.IsEmpty(await RunAsync(body));
}
