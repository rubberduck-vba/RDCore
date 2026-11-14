using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.Tests.Cli;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Diagnostics;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC11026</c>: a public variable of an object, named as a member of it, given to a <c>ByRef</c> parameter: MS-VBA passes a copy (oracle\s55 B), where
/// MS-VBAL §5.6.12 and §5.3.1.11 pass the variable (RD-VBAL §5.3.1.11).
/// </summary>
[TestClass]
public sealed class ObjectVariablePassedByRefAnalyzerTests
{
    private const string BoxSource = "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\nAttribute VB_Name = \"Box\"\r\nOption Explicit\r\n"
        + "Public Count As Long\r\nPublic Sub BumpMine()\r\n    Bump Me.Count\r\nEnd Sub\r\nPublic Sub BumpOwn()\r\n    Bump Count\r\nEnd Sub\r\n";

    private const string Declarations = "Option Explicit\n"
        + "Public Counter As Long\n"
        + "Public Sub Bump(ByRef n As Long)\n    n = n + 1\nEnd Sub\n"
        + "Public Sub Set8(v As Variant)\n    v = 8\nEnd Sub\n"
        + "Public Sub Show(ByVal n As Long)\nEnd Sub\n"
        + "Public Function Twice(ByRef n As Long) As Long\n    Twice = n * 2\nEnd Function\n";

    private static async Task<AnalyzerFinding[]> RunAsync(string module, string body)
    {
        var program = $"Attribute VB_Name = \"Program\"\r\n{(Declarations + $"Public Sub Main()\n    Dim b As Box\n    Set b = New Box\n    Dim o As Object\n{body}\nEnd Sub").ReplaceLineEndings("\r\n")}\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([("Box", BoxSource)], program);
        var (uri, source) = module == "Box"
            ? (new Uri(Path.Combine(ModuleWorkspace.Root, "Box.cls")), BoxSource)
            : (new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas")), program);
        var parsed = new ModuleParser().Parse(uri, source);
        var semantics = payload.Modules.Single(candidate => candidate.Module.Fragment.TrimStart('#') == module);
        return [.. new ObjectVariablePassedByRefAnalyzer().Analyze(new ModuleAnalysisContext(uri, parsed, semantics)).OrderBy(finding => finding.Range.Start)];
    }

    [TestMethod]
    [DataRow("    Bump b.Count", "b.Count", DisplayName = "a call statement")]
    [DataRow("    Debug.Print Twice(b.Count)", "b.Count", DisplayName = "a function called in an expression")]
    [DataRow("    Set8 b.Count", "b.Count", DisplayName = "an implicitly ByRef Variant parameter")]
    [DataRow("    With b\n        Bump .Count\n    End With", ".Count", DisplayName = "relative to a With block")]
    public async Task AnObjectsVariable_ForAByRefParameter_IsAWarning(string body, string argument)
    {
        var findings = await RunAsync("Program", body);

        Assert.HasCount(1, findings);
        Assert.AreEqual(RDCoreDiagnosticId.ObjectVariablePassedByRefIsACopy, findings[0].Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, findings[0].Severity);
        var line = body.Split('\n').Single(text => text.Contains(argument, StringComparison.Ordinal));
        Assert.AreEqual(line.IndexOf(argument, StringComparison.Ordinal), findings[0].Range.Start.Character, "where the argument is written");
    }

    [TestMethod]
    public async Task ItsOwnVariable_NamedThroughMe_IsAWarning()
    {
        var findings = await RunAsync("Box", "    b.BumpMine");

        Assert.HasCount(1, findings);
        Assert.AreEqual(RDCoreDiagnosticId.ObjectVariablePassedByRefIsACopy, findings[0].Id);
    }

    [TestMethod]
    [DataRow("    Bump Counter", DisplayName = "a module's variable")]
    [DataRow("    Bump Program.Counter", DisplayName = "a module's variable, named through its module")]
    [DataRow("    Bump (b.Count)", DisplayName = "in parentheses, a value")]
    [DataRow("    Show b.Count", DisplayName = "a ByVal parameter")]
    [DataRow("    Debug.Print Replace(b.Count, \"1\", \"2\")", DisplayName = "a library's procedure, whose type library declares the parameter ByRef")]
    [DataRow("    Set o = b\n    Bump o.Count", DisplayName = "bound late: what it is, is not known")]
    public async Task AnythingElse_IsNoWarning(string body)
        => Assert.IsEmpty(await RunAsync("Program", body));

    [TestMethod]
    public async Task ItsOwnVariable_NamedByItself_IsNoWarning()
    {
        var findings = await RunAsync("Box", "    b.BumpOwn");

        Assert.IsFalse(findings.Any(finding => finding.Range.Start.Line == 11), "Bump Count, in BumpOwn, passes the variable");
        Assert.HasCount(1, findings, "only Bump Me.Count, in BumpMine");
    }
}
