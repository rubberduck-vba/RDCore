using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>RDC11045</c> and <c>RDC11046</c>: a <c>Let</c> that MS-VBAL and MS-VBA treat differently (<strong>MS-VBAL §5.4.3.8</strong>), reported as a warning
/// at the expression that names the target.
/// </summary>
[TestClass]
public sealed class LetThroughDefaultMemberAnalyzerTests
{
    private static readonly (string, string)[] Classes =
    [
        ("Leaf", ModuleWorkspace.ClassModule("Leaf",
            "Private mV As Long",
            "Public Property Get Value() As Long", "Attribute Value.VB_UserMemId = 0", "Value = mV", "End Property",
            "Public Property Let Value(ByVal n As Long)", "mV = n", "End Property")),
        ("ReadOnly", ModuleWorkspace.ClassModule("ReadOnly",
            "Public Property Get Value() As Long", "Attribute Value.VB_UserMemId = 0", "Value = 7", "End Property")),
        ("MakesLeaf", ModuleWorkspace.ClassModule("MakesLeaf",
            "Public Function Make() As Leaf", "Attribute Make.VB_UserMemId = 0", "Set Make = New Leaf", "End Function")),
        ("Holder", ModuleWorkspace.ClassModule("Holder",
            "Private mV As Variant",
            "Public Property Get Held() As Variant", "Held = mV", "End Property",
            "Public Property Get Kept() As Variant", "Kept = mV", "End Property",
            "Public Property Let Kept(ByVal v As Variant)", "mV = v", "End Property",
            "Public Function Made() As Variant", "End Function")),
    ];

    private static async Task<AnalyzerFinding[]> FindingsAsync(IModuleAnalyzer analyzer, string declaration, string statement)
    {
        var program = $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nOn Error Resume Next\r\n{declaration}\r\n{statement}\r\nEnd Sub\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync(Classes, program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        return [.. analyzer.Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, program), model))];
    }

    [TestMethod]
    [DataRow("Dim r As New ReadOnly", "r = 5", "r", DisplayName = "a Property Get default member")]
    [DataRow("Dim m As New MakesLeaf", "m = 5", "m", DisplayName = "a function default member")]
    public async Task ALetToADefaultMemberWithNoPropertyLet_IsAWarningNamingTheTarget(string declaration, string statement, string name)
    {
        var finding = Assert.ContainsSingle(await FindingsAsync(new LetToADefaultMemberWithNoPropertyLetAnalyzer(), declaration, statement));

        Assert.AreEqual(RDCoreDiagnosticId.LetToADefaultMemberWithNoPropertyLet.ToCode(), finding.Code);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        StringAssert.Contains(finding.Message, $"'{name}'");
    }

    [TestMethod]
    [DataRow("Dim l As New Leaf", "l = 5", DisplayName = "a default member with a Property Let")]
    [DataRow("Dim r As New ReadOnly", "Debug.Print r", DisplayName = "a read")]
    [DataRow("Dim o As Object", "Set o = New ReadOnly: o = 5", DisplayName = "an Object")]
    [DataRow("Dim r As New ReadOnly", "r() = 5", DisplayName = "an empty argument list")]
    [DataRow("Dim r(1) As ReadOnly", "r(0) = 5", DisplayName = "an element of an array")]
    public async Task ALetThatMSVBACompiles_IsNotReportedAsRDC11045(string declaration, string statement)
        => Assert.IsEmpty(await FindingsAsync(new LetToADefaultMemberWithNoPropertyLetAnalyzer(), declaration, statement));

    [TestMethod]
    [DataRow("h.Held = 5", "Held", "451", DisplayName = "a Property Get with no Property Let")]
    [DataRow("h.Made = 5", "Made", "450", DisplayName = "a function")]
    public async Task ALetToAVariantGetOrFunction_IsAWarningNamingTheMember(string statement, string name, string error)
    {
        var finding = Assert.ContainsSingle(await FindingsAsync(new LetThroughAVariantGetOrFunctionAnalyzer(), "Dim h As New Holder", statement));

        Assert.AreEqual(RDCoreDiagnosticId.LetThroughAVariantGetOrFunction.ToCode(), finding.Code);
        Assert.AreEqual(DiagnosticSeverity.Warning, finding.Severity);
        StringAssert.Contains(finding.Message, $"'{name}'");
        StringAssert.Contains(finding.Message, error);
    }

    [TestMethod]
    [DataRow("h.Kept = 5", DisplayName = "a Property Get with a Property Let")]
    [DataRow("Debug.Print h.Held", DisplayName = "a read")]
    public async Task ALetThatNamesAPropertyLet_IsNotReportedAsRDC11046(string statement)
        => Assert.IsEmpty(await FindingsAsync(new LetThroughAVariantGetOrFunctionAnalyzer(), "Dim h As New Holder", statement));
}
