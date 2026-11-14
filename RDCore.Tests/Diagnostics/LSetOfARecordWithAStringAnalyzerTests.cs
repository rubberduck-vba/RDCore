using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.Tests.Cli;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Diagnostics;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC11044</c>: <c>LSet</c> between two records when either one holds a variable-length <c>String</c>, which MS-VBAL §5.4.3.6 allows and MS-VBA refuses
/// (<see cref="RDCore.SDK.Semantics.Flags.FixedAssignmentSemanticFlags"/>).
/// </summary>
[TestClass]
public sealed class LSetOfARecordWithAStringAnalyzerTests
{
    private static async Task<AnalyzerFinding[]> FindAsync(params string[] body)
    {
        var uri = TestUri.TestModuleUri();
        var source = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n"
            + "Private Type Plain\r\nCode As Long\r\nTag As String * 4\r\nEnd Type\r\n"
            + "Private Type Named\r\nCode As Long\r\nName As String\r\nEnd Type\r\n"
            + $"Public Sub Main()\r\nDim p As Plain, q As Plain, n As Named, m As Named, s As String\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], source);

        return [.. new LSetOfARecordWithAStringAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), payload.Modules.Single()))];
    }

    [TestMethod]
    [DataRow("LSet p = n", DisplayName = "from a record that holds one")]
    [DataRow("LSet n = p", DisplayName = "into a record that holds one")]
    [DataRow("LSet n = m", DisplayName = "between two that hold one")]
    public async Task ARecordThatHoldsAVariableLengthString_IsAWarning(string statement)
    {
        var finding = Assert.ContainsSingle(await FindAsync(statement));

        Assert.AreEqual(RDCoreDiagnosticId.LSetOfARecordWithAString, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        Assert.AreEqual(12, finding.Range.Start.Line);
    }

    [TestMethod]
    [DataRow("LSet p = q", DisplayName = "two records with fixed-length strings only")]
    [DataRow("LSet s = \"abc\"", DisplayName = "the string form")]
    public async Task NoVariableLengthStringInARecord_IsNotReported(string statement)
        => Assert.IsEmpty(await FindAsync(statement));
}
