using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Platform.Protocol;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// A default member called without being named, as the host states it: an object used as a value or assigned one (<c>RDC00119</c>, <c>RDC00120</c> for an
/// <c>Object</c>), an argument list given to an object (<c>RDC00121</c>, <c>RDC00122</c>), a dictionary access (<c>RDC00123</c>, <c>RDC00124</c>), and an
/// object assigned to an object without <c>Set</c> (<c>RDC00125</c>).
/// </summary>
[TestClass]
public sealed class DefaultMemberAccessAnalyzerTests
{
    private static readonly string[] Procedures =
    [
        "ReadAnObject|Dim h As New Holder|x = h",
        "ReadAMemberThatReturnsAnObject|Dim o As New Outer|x = o.Inner",
        "PassAnObject|Dim h As New Holder|Take h",
        "AssignAnObject|Dim h As New Holder|h = 5",
        "ReadAnUnboundObject|Dim v As Object|Set v = New Holder|x = v",
        "AssignAnUnboundObject|Dim v As Object|Set v = New Holder|v = 5",
        "ReadAVariant|Dim v As Variant|Set v = New Holder|x = v",
        "NameTheMember|Dim h As New Holder|x = h.Value",
        "IndexAnObject|Dim b As New Bag|x = b(1)",
        "IndexAnUnboundObject|Dim v As Object|Set v = New Bag|x = v(1)",
        "NameTheIndexedMember|Dim b As New Bag|x = b.Item(1)",
        "BangAnObject|Dim b As New Bag|x = b!key",
        "BangAnUnboundObject|Dim v As Object|Set v = New Bag|x = v!key",
        "LetAnObjectToAnObject|Dim h As New Holder|Dim h2 As New Holder|Let h = h2",
        "LetAnUnboundObjectToAnObject|Dim v As Object|Set v = New Holder|Dim h As New Holder|h = v",
        "SetAnObject|Dim h As Holder|Dim h2 As New Holder|Set h = h2",
    ];

    private static string _source = string.Empty;
    private static ModuleSemanticsDto? _model;

    // one workspace for every test of the class: the host analyzes it once.
    [ClassInitialize]
    public static async Task AnalyzeTheWorkspaceAsync(TestContext _)
    {
        var holder = ModuleWorkspace.ClassModule("Holder", "Public Property Get Value() As Long", "Attribute Value.VB_UserMemId = 0", "Value = 1", "End Property",
            "Public Property Let Value(ByVal v As Long)", "Attribute Value.VB_UserMemId = 0", "End Property");
        var bag = ModuleWorkspace.ClassModule("Bag", "Public Function Item(ByVal key As Variant) As Long", "Attribute Item.VB_UserMemId = 0", "Item = 1", "End Function");
        var outer = ModuleWorkspace.ClassModule("Outer", "Public Property Get Inner() As Holder", "Set Inner = New Holder", "End Property");

        var lines = new List<string> { "Option Explicit" };
        foreach (var procedure in Procedures)
        {
            var parts = procedure.Split('|');
            lines.Add($"Public Sub {parts[0]}()");
            lines.Add("    Dim x As Long");
            lines.AddRange(parts.Skip(1).Select(line => "    " + line));
            lines.Add("End Sub");
        }
        lines.AddRange(["Public Sub Take(ByVal n As Long)", "End Sub"]);

        var program = ModuleWorkspace.StandardModule("Program", [.. lines]);
        var payload = await ModuleWorkspace.SemanticsAsync([("Holder", holder), ("Bag", bag), ("Outer", outer)], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        Assert.IsEmpty(model.Procedures.SelectMany(procedure => procedure.CompileErrors), string.Join("; ", model.Procedures.SelectMany(p => p.CompileErrors).Select(e => e.Description)));
        (_source, _model) = (program, model);
    }

    // each finding, as the procedure it is in and the code it marks.
    private static string[] Findings(IModuleAnalyzer analyzer, RDCoreDiagnosticId id, DiagnosticSeverity severity)
    {
        var (source, model) = (_source, _model ?? throw new InvalidOperationException("the workspace is analyzed once, before the tests"));
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        var parsed = new ModuleParser().Parse(uri, source);
        var findings = analyzer.Analyze(new ModuleAnalysisContext(uri, parsed, model)).OrderBy(finding => finding.Range.Start).ToArray();
        Assert.IsTrue(findings.All(finding => finding.Id == id && finding.Severity == severity), string.Join("; ", findings.Select(finding => finding.Message)));

        var lines = source.Split("\r\n");
        return [.. findings.Select(finding =>
        {
            var procedure = lines.Take(finding.Range.Start.Line + 1).Last(line => line.StartsWith("Public Sub ", StringComparison.Ordinal))["Public Sub ".Length..].Split('(')[0];
            var line = lines[finding.Range.Start.Line];
            var end = finding.Range.End.Line == finding.Range.Start.Line ? finding.Range.End.Character : line.Length;
            return $"{procedure}: {line[finding.Range.Start.Character..end]}";
        })];
    }

    [TestMethod]
    public void AnObjectUsedAsAValue_OrAssignedOne_CallsItsDefaultMember()
        => CollectionAssert.AreEqual(
            new[] { "ReadAnObject: h", "ReadAMemberThatReturnsAnObject: o.Inner", "PassAnObject: h", "AssignAnObject: h" },
            Findings(new ImplicitDefaultMemberAccessAnalyzer(), RDCoreDiagnosticId.ImplicitDefaultMemberAccess, DiagnosticSeverity.Information),
            "not an Object, a Variant, a member named, nor an object assigned to an object");

    [TestMethod]
    public void AnObjectUsedAsAValue_HasADefaultMemberKnownOnlyAtRunTime()
        => CollectionAssert.AreEqual(
            new[] { "ReadAnUnboundObject: v", "AssignAnUnboundObject: v" },
            Findings(new ImplicitUnboundDefaultMemberAccessAnalyzer(), RDCoreDiagnosticId.ImplicitUnboundDefaultMemberAccess, DiagnosticSeverity.Warning),
            "not a Variant, which nothing says holds an object, nor the Object assigned to an object");

    [TestMethod]
    public void AnArgumentListGivenToAnObject_GoesToItsDefaultMember()
        => CollectionAssert.AreEqual(
            new[] { "IndexAnObject: b(1)" },
            Findings(new IndexedDefaultMemberAccessAnalyzer(), RDCoreDiagnosticId.IndexedDefaultMemberAccess, DiagnosticSeverity.Information));

    [TestMethod]
    public void AnArgumentListGivenToAnObject_GoesToADefaultMemberKnownOnlyAtRunTime()
        => CollectionAssert.AreEqual(
            new[] { "IndexAnUnboundObject: v(1)" },
            Findings(new IndexedUnboundDefaultMemberAccessAnalyzer(), RDCoreDiagnosticId.IndexedUnboundDefaultMemberAccess, DiagnosticSeverity.Warning));

    [TestMethod]
    public void ABangName_GoesToTheDefaultMember()
        => CollectionAssert.AreEqual(
            new[] { "BangAnObject: b!key" },
            Findings(new BangNotationAnalyzer(), RDCoreDiagnosticId.BangNotation, DiagnosticSeverity.Information));

    [TestMethod]
    public void ABangName_GoesToADefaultMemberKnownOnlyAtRunTime()
        => CollectionAssert.AreEqual(
            new[] { "BangAnUnboundObject: v!key" },
            Findings(new UnboundBangNotationAnalyzer(), RDCoreDiagnosticId.UnboundBangNotation, DiagnosticSeverity.Warning));

    [TestMethod]
    public void AnObjectAssignedToAnObjectWithoutSet_IsSuspicious()
        => CollectionAssert.AreEqual(
            new[] { "LetAnObjectToAnObject: Let h = h2", "LetAnUnboundObjectToAnObject: h = v" },
            Findings(new SuspiciousLetAssignmentAnalyzer(), RDCoreDiagnosticId.SuspiciousLetAssignment, DiagnosticSeverity.Warning),
            "not a Set");
}
