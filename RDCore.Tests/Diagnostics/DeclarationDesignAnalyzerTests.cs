using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Platform.Protocol;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// How a module declares what it declares: a <c>Sub</c> that gives its result back through a parameter (<c>RDC00214</c>), a variable declared <c>As New</c>
/// (<c>RDC00215</c>), and a <c>Public</c> variable of a class module (<c>RDC00409</c>).
/// </summary>
[TestClass]
public sealed class DeclarationDesignAnalyzerTests
{
    private static AnalyzerFinding[] Run(IModuleAnalyzer analyzer, string source, ModuleSemanticsDto? semantics, Uri uri)
    {
        var parsed = new ModuleParser().Parse(uri, source);
        Assert.IsEmpty(parsed.SyntaxErrors, string.Join("; ", parsed.SyntaxErrors.Select(error => error.Verbose)));
        return [.. analyzer.Analyze(new ModuleAnalysisContext(uri, parsed, semantics)).OrderBy(finding => finding.Range.Start)];
    }

    // the analyzer, given a standard module and a class module as the host models them.
    private static async Task<AnalyzerFinding[]> WorkspaceAsync(IModuleAnalyzer analyzer, string analyzed, string[] program, string[] panel)
    {
        var standard = ModuleWorkspace.StandardModule("Program", program);
        var source = ModuleWorkspace.ClassModule("Panel", panel);
        var payload = await ModuleWorkspace.SemanticsAsync([("Panel", source)], standard);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == analyzed);
        return analyzed == "Panel"
            ? Run(analyzer, source, model, new Uri(Path.Combine(ModuleWorkspace.Root, "Panel.cls")))
            : Run(analyzer, standard, model, new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas")));
    }

    private static int[] Lines(AnalyzerFinding[] findings) => [.. findings.Select(finding => finding.Range.Start.Line)];

    private static void AssertAre(AnalyzerFinding[] findings, RDCoreDiagnosticId id, DiagnosticSeverity severity)
        => Assert.IsTrue(findings.All(finding => finding.Severity == severity && finding.Id == id), string.Join("; ", findings.Select(finding => finding.Message)));

    #region RDC00214 a Sub that gives its result back through a parameter

    [TestMethod]
    public async Task ASubWhoseOnlyByRefParameterIsAssigned_CanBeAFunction()
    {
        var findings = await WorkspaceAsync(new ProcedureCanBeWrittenAsFunctionAnalyzer(), "Program",
            [
                "Option Explicit",
                "Public Sub Sum(ByVal a As Long, ByVal b As Long, total As Long)",
                "    total = a + b",
                "End Sub",
                "Public Sub ReadOnly(ByRef value As Long)",
                "    Debug.Print value",
                "End Sub",
                "Public Sub Swap(ByRef a As Long, ByRef b As Long)",
                "    Dim t As Long",
                "    t = a: a = b: b = t",
                "End Sub",
                "Public Sub Fill(ByVal n As Long, ParamArray items() As Variant)",
                "    items(0) = n",
                "End Sub",
                "Public Function Twice(ByRef value As Long) As Long",
                "    value = value * 2",
                "    Twice = value",
                "End Function",
            ],
            ["Option Explicit"]);

        CollectionAssert.AreEqual(new[] { 2 }, Lines(findings), "not a ByRef parameter only read, two ByRef parameters, a ParamArray, nor a Function");
        AssertAre(findings, RDCoreDiagnosticId.ProcedureCanBeWrittenAsFunction, DiagnosticSeverity.Information);
        StringAssert.Contains(findings[0].Message, "'Sum'");
        StringAssert.Contains(findings[0].Message, "'total'");
    }

    [TestMethod]
    public async Task APublicMemberOfAClassModule_MayBeOfAnInterface()
    {
        var findings = await WorkspaceAsync(new ProcedureCanBeWrittenAsFunctionAnalyzer(), "Panel",
            ["Option Explicit"],
            [
                "Option Explicit",
                "Public Sub Measure(ByRef width As Long)",
                "    width = 10",
                "End Sub",
                "Private Sub Resize(ByRef height As Long)",
                "    height = 20",
                "End Sub",
            ]);

        CollectionAssert.AreEqual(new[] { 9 }, Lines(findings), "only the Private procedure (a class module's source has five lines of header)");
    }

    #endregion

    #region RDC00215 a variable declared As New

    [TestMethod]
    public async Task AVariableDeclaredAsNew_CreatesItsOwnObject()
    {
        var findings = await WorkspaceAsync(new SelfAssignedDeclarationAnalyzer(), "Program",
            [
                "Option Explicit",
                "Private cache As New Collection",
                "Public Sub Main()",
                "    Dim items As New Collection",
                "    Dim other As Collection",
                "    Set other = New Collection",
                "    Debug.Print items.Count + other.Count + cache.Count",
                "End Sub",
            ],
            ["Option Explicit"]);

        CollectionAssert.AreEqual(new[] { 2, 4 }, Lines(findings), "a field and a local; not a variable assigned New");
        AssertAre(findings, RDCoreDiagnosticId.SelfAssignedDeclaration, DiagnosticSeverity.Hint);
        StringAssert.Contains(findings[1].Message, "'items'");
    }

    #endregion

    #region RDC00409 a Public variable of a class module

    [TestMethod]
    public async Task APublicVariableOfAClassModule_IsAField()
    {
        var findings = await WorkspaceAsync(new EncapsulatePublicFieldAnalyzer(), "Panel",
            ["Option Explicit"],
            [
                "Option Explicit",
                "Public Title As String",
                "Private mWidth As Long",
                "Dim mHeight As Long",
                "Private Const Margin As Long = 4",
                "Public Property Get Width() As Long",
                "    Width = mWidth + mHeight + Margin",
                "End Property",
            ]);

        CollectionAssert.AreEqual(new[] { 6 }, Lines(findings), "not a Private or Dim variable (a class module's source has five lines of header)");
        AssertAre(findings, RDCoreDiagnosticId.EncapsulatePublicField, DiagnosticSeverity.Information);
        StringAssert.Contains(findings[0].Message, "'Title'");
    }

    [TestMethod]
    public async Task APublicVariableOfAStandardModule_IsAGlobalVariable()
        => Assert.IsEmpty(await WorkspaceAsync(new EncapsulatePublicFieldAnalyzer(), "Program", ["Option Explicit", "Public Total As Long"], ["Option Explicit"]));

    #endregion
}
