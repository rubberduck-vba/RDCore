using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.Tests.Cli;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Diagnostics;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC00207</c>: <c>IsMissing</c> of a parameter that can never be missing (one that is not a <c>Variant</c>, or an <c>Optional</c> <c>Variant</c> with a
/// default value), which is always <c>False</c> (observed in Excel); and of a variable that is not a <c>Variant</c>, or a constant, which
/// never hold the Missing value.
/// </summary>
[TestClass]
public sealed class IsMissingAlwaysFalseAnalyzerTests
{
    private static async Task<AnalyzerFinding[]> FindingsAsync(string signature, string body = "Debug.Print IsMissing(p)", string declarations = "")
    {
        var program = $"Attribute VB_Name = \"Program\"\r\n{declarations}Public Sub Probe({signature})\r\n{body}\r\nEnd Sub\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        return [.. new IsMissingAlwaysFalseAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, program), model))];
    }

    [TestMethod]
    [DataRow("Optional p As Long", "not a Variant", DisplayName = "I01 an Optional Long")]
    [DataRow("Optional p As Long = 2", "not a Variant", DisplayName = "I09 an Optional Long with a default")]
    [DataRow("Optional p As String", "not a Variant", DisplayName = "I06 an Optional String")]
    [DataRow("Optional p As Object", "not a Variant", DisplayName = "I06 an Optional Object")]
    [DataRow("p As Long", "not a Variant", DisplayName = "a required Long")]
    [DataRow("p() As Variant", "not a Variant", DisplayName = "an array")]
    [DataRow("Optional p As Variant = 5", "given a default value", DisplayName = "I02 an Optional Variant with a default")]
    [DataRow("Optional p = 5", "given a default value", DisplayName = "an Optional with a default and no As clause")]
    public async Task IsMissingOfAParameterThatCannotBeMissing_IsAWarning(string signature, string reason)
    {
        var finding = Assert.ContainsSingle(await FindingsAsync(signature));

        Assert.AreEqual(RDCoreDiagnosticId.IsMissingAlwaysFalse, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        StringAssert.Contains(finding.Message, "IsMissing(p)");
        StringAssert.Contains(finding.Message, reason);
    }

    [TestMethod]
    [DataRow("Optional p As Variant", DisplayName = "I03 an Optional Variant")]
    [DataRow("Optional p", DisplayName = "I08 an Optional with no As clause")]
    [DataRow("p As Variant", DisplayName = "I04 a required Variant")]
    [DataRow("ByVal p As Variant", DisplayName = "I08 a ByVal Variant")]
    [DataRow("ParamArray p() As Variant", DisplayName = "I05 a ParamArray")]
    public async Task IsMissingOfAParameterThatCanBeMissing_IsNotReported(string signature)
        => Assert.IsEmpty(await FindingsAsync(signature));

    [TestMethod]
    public async Task IsMissingOfSomethingElse_IsNotReported()
        // a Variant local holds the Missing value that is assigned to it (MS-VBAL §6.1.2.7.1.6), and a literal is not a name.
        => Assert.IsEmpty(await FindingsAsync("Optional n As Long", "Dim w As Variant\r\nDebug.Print IsMissing(w)\r\nDebug.Print IsMissing(5)"));

    [TestMethod]
    public async Task IsMissingOfAVariantVariable_ThatHoldsAMissingArgument_IsNotReported()
        => Assert.IsEmpty(await FindingsAsync("Optional p As Variant", "Dim w\r\nw = p\r\nDebug.Print IsMissing(w)", "Private mField As Variant\r\n"));

    [TestMethod]
    [DataRow("Dim w As Long", "w", "a variable that is not a Variant", DisplayName = "a Long local")]
    [DataRow("Dim w As Object", "w", "a variable that is not a Variant", DisplayName = "an Object local")]
    [DataRow("Dim w() As Variant", "w", "a variable that is not a Variant", DisplayName = "an array of Variant")]
    [DataRow("Const W As Long = 1", "W", "a constant", DisplayName = "a local constant")]
    [DataRow("", "mCount", "a variable that is not a Variant", DisplayName = "a Long field")]
    [DataRow("", "LIMIT", "a constant", DisplayName = "a constant of the module")]
    public async Task IsMissingOfANameThatNeverHoldsTheMissingValue_IsAWarning(string local, string name, string reason)
    {
        var finding = Assert.ContainsSingle(await FindingsAsync(
            "Optional p As Variant", $"{local}\r\nDebug.Print IsMissing({name})", "Private mCount As Long\r\nPrivate Const LIMIT As Long = 3\r\n"));

        Assert.AreEqual(RDCoreDiagnosticId.IsMissingAlwaysFalse, finding.Id);
        StringAssert.Contains(finding.Message, $"IsMissing({name})");
        StringAssert.Contains(finding.Message, reason);
        StringAssert.Contains(finding.Message, "never holds the Missing value");
    }

    [TestMethod]
    public async Task AProcedureNamedIsMissing_IsNotTheLibrarys()
    {
        var program = "Attribute VB_Name = \"Program\"\r\nPublic Function IsMissing(ByVal x As Long) As Boolean\r\nEnd Function\r\n"
            + "Public Sub Probe(Optional p As Long)\r\nDebug.Print IsMissing(p)\r\nEnd Sub\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));

        Assert.IsEmpty(new IsMissingAlwaysFalseAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, program), model)));
    }

    [TestMethod]
    [DataRow("Optional p As Long", "If Not IsMissing(p) Then Debug.Print p", "If Not False Then Debug.Print p", DisplayName = "a parameter")]
    [DataRow("Optional q As Variant", "Dim n As Long\r\nDebug.Print IsMissing(n)", "Debug.Print False", DisplayName = "a variable")]
    public async Task TheFix_ReplacesTheCallWithFalse(string signature, string body, string fixedLine)
    {
        var finding = Assert.ContainsSingle(await FindingsAsync(signature, body));

        var fix = finding.Fixes?.Edit;
        Assert.IsNotNull(fix);
        StringAssert.Contains(fix.Title, "False");
        var edit = Assert.ContainsSingle(fix.Edits);
        Assert.AreEqual(edit.Range.Start.Line, edit.Range.End.Line);
        var lines = $"Attribute VB_Name = \"Program\"\r\nPublic Sub Probe({signature})\r\n{body}\r\nEnd Sub\r\n".Split("\r\n");
        var line = lines[edit.Range.Start.Line];
        Assert.AreEqual(fixedLine, line[..edit.Range.Start.Character] + edit.NewText + line[edit.Range.End.Character..]);
    }
}
