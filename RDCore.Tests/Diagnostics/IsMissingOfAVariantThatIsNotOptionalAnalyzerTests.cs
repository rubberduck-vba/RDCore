using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.Tests.Cli;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Diagnostics;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC00218</c>: <c>IsMissing</c> of a required <c>Variant</c> parameter, or of a <c>Variant</c> variable, which holds the Missing value only when it is
/// given one from an <c>Optional</c> parameter whose argument was omitted (<strong>MS-VBAL §6.1.2.7.1.6</strong>).
/// </summary>
[TestClass]
public sealed class IsMissingOfAVariantThatIsNotOptionalAnalyzerTests
{
    private static async Task<AnalyzerFinding[]> FindingsAsync(string signature, string body, string declarations = "")
    {
        var program = $"Attribute VB_Name = \"Program\"\r\n{declarations}Public Sub Probe({signature})\r\n{body}\r\nEnd Sub\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        return [.. new IsMissingOfAVariantThatIsNotOptionalAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, program), model))];
    }

    [TestMethod]
    [DataRow("p As Variant", "Debug.Print IsMissing(p)", "p", "a required Variant parameter", DisplayName = "a required Variant parameter")]
    [DataRow("ByVal p As Variant", "Debug.Print IsMissing(p)", "p", "a required Variant parameter", DisplayName = "a required ByVal Variant parameter")]
    [DataRow("p", "Debug.Print IsMissing(p)", "p", "a required Variant parameter", DisplayName = "a required parameter with no As clause")]
    [DataRow("Optional p As Variant", "Dim w As Variant\r\nw = p\r\nDebug.Print IsMissing(w)", "w", "a Variant variable", DisplayName = "a Variant local")]
    [DataRow("Optional p As Variant", "Debug.Print IsMissing(mValue)", "mValue", "a Variant variable", DisplayName = "a Variant field")]
    public async Task IsMissingOfAVariantThatIsNotOptional_IsAWarning(string signature, string body, string name, string what)
    {
        var finding = Assert.ContainsSingle(await FindingsAsync(signature, body, "Private mValue As Variant\r\n"));

        Assert.AreEqual(RDCoreDiagnosticId.IsMissingOfAVariantThatIsNotOptional, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        StringAssert.Contains(finding.Message, $"IsMissing({name})");
        StringAssert.Contains(finding.Message, what);
    }

    [TestMethod]
    [DataRow("Optional p As Variant", "Debug.Print IsMissing(p)", DisplayName = "an Optional Variant, which the call leaves missing")]
    [DataRow("Optional p", "Debug.Print IsMissing(p)", DisplayName = "an Optional with no As clause")]
    [DataRow("ParamArray p() As Variant", "Debug.Print IsMissing(p)", DisplayName = "a ParamArray")]
    [DataRow("Optional p As Long", "Debug.Print IsMissing(p)", DisplayName = "a name that can never be missing, which is RDC00207")]
    [DataRow("Optional p As Variant", "Dim n As Long\r\nDebug.Print IsMissing(n)", DisplayName = "a variable that is not a Variant, which is RDC00207")]
    [DataRow("Optional p As Variant", "Debug.Print IsMissing(5)", DisplayName = "a literal")]
    public async Task IsMissingOfAnythingElse_IsNotReported(string signature, string body)
        => Assert.IsEmpty(await FindingsAsync(signature, body));

    [TestMethod]
    [DataRow("p As Variant", "Optional p As Variant", DisplayName = "the only parameter")]
    [DataRow("ByVal p As Variant", "Optional ByVal p As Variant", DisplayName = "Optional goes before ByVal")]
    [DataRow("p, Optional q As Long", "Optional p, Optional q As Long", DisplayName = "every later parameter is Optional")]
    [DataRow("a As Long, p", "a As Long, Optional p", DisplayName = "after a required parameter")]
    public async Task ARequiredVariantParameter_IsDeclaredOptional(string signature, string fixedSignature)
    {
        var finding = Assert.ContainsSingle(await FindingsAsync(signature, "Debug.Print IsMissing(p)"));

        var fix = finding.Fixes?.Edit;
        Assert.IsNotNull(fix);
        StringAssert.Contains(fix.Title, "'p'");
        var edit = Assert.ContainsSingle(fix.Edits);
        Assert.AreEqual(edit.Range.Start, edit.Range.End);
        var line = $"Public Sub Probe({signature})";
        Assert.AreEqual(1, edit.Range.Start.Line);
        Assert.AreEqual($"Public Sub Probe({fixedSignature})", line.Insert(edit.Range.Start.Character, edit.NewText));
    }

    [TestMethod]
    [DataRow("p, q", "Debug.Print IsMissing(p)", DisplayName = "a later parameter is required")]
    [DataRow("p, ParamArray r()", "Debug.Print IsMissing(p)", DisplayName = "the procedure has a ParamArray")]
    [DataRow("Optional p As Variant", "Dim w As Variant\r\nw = p\r\nDebug.Print IsMissing(w)", DisplayName = "a variable")]
    public async Task NoOptionalDeclarationIsOffered_WhereItIsNotLegalOrNotAParameter(string signature, string body)
    {
        var finding = Assert.ContainsSingle(await FindingsAsync(signature, body));

        Assert.IsNull(finding.Fixes?.Edit);
    }

    [TestMethod]
    [DataRow("Public", false, DisplayName = "a Public member, which a class that Implements this one declares as it is")]
    [DataRow("Private", true, DisplayName = "a Private member")]
    public async Task InAClassModule_OnlyAPrivateMemberIsDeclaredOptional(string access, bool offered)
    {
        var source = ModuleWorkspace.ClassModule("Report", $"{access} Sub Show(p As Variant)", "Debug.Print IsMissing(p)", "End Sub");
        var payload = await ModuleWorkspace.SemanticsAsync([("Report", source)], "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nEnd Sub\r\n");
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Report");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Report.cls"));

        var finding = Assert.ContainsSingle(new IsMissingOfAVariantThatIsNotOptionalAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), model)));

        Assert.AreEqual(offered, finding.Fixes?.Edit is not null);
    }
}
