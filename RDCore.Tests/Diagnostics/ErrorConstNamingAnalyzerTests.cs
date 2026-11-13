using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Platform.Protocol;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <c>On Error GoTo -1</c> (<c>RDC00211</c>), an <c>On Error Resume Next</c> that nothing follows (<c>RDC00212</c>), a constant that states no type
/// (<c>RDC00117</c>), and an underscore in a member of the interface of a class (<c>RDC00408</c>).
/// </summary>
[TestClass]
public sealed class ErrorConstNamingAnalyzerTests
{
    private static string Module(string source) => $"Attribute VB_Name = \"Program\"\r\n{source.ReplaceLineEndings("\r\n")}\r\n";

    private static readonly Uri Program = new(Path.Combine(ModuleWorkspace.Root, "Program.bas"));

    private static AnalyzerFinding[] Run(IModuleAnalyzer analyzer, string source, ModuleSemanticsDto? semantics = null, Uri? uri = null)
    {
        uri ??= Program;
        var parsed = new ModuleParser().Parse(uri, source);
        Assert.IsEmpty(parsed.SyntaxErrors, string.Join("; ", parsed.SyntaxErrors.Select(error => error.Verbose)));
        return [.. analyzer.Analyze(new ModuleAnalysisContext(uri, parsed, semantics)).OrderBy(finding => finding.Range.Start)];
    }

    private static int[] Lines(AnalyzerFinding[] findings) => [.. findings.Select(finding => finding.Range.Start.Line)];

    private static void AssertAre(AnalyzerFinding[] findings, RDCoreDiagnosticId id, DiagnosticSeverity severity)
        => Assert.IsTrue(findings.All(finding => finding.Severity == severity && finding.Id == id), string.Join("; ", findings.Select(finding => finding.Message)));

    private static string Text(string source, AnalyzerFinding finding)
    {
        var line = source.ReplaceLineEndings("\n").Split('\n')[finding.Range.Start.Line];
        return line[finding.Range.Start.Character..finding.Range.End.Character];
    }

    #region RDC00211 On Error GoTo -1

    [TestMethod]
    public void OnErrorGoToMinusOne_IsReported()
    {
        var source = Module("Public Sub DoSomething()\n    On Error GoTo Handler\n    Exit Sub\nHandler:\n    On Error GoTo -1\n    On Error GoTo 0\nEnd Sub");
        var findings = Run(new OnErrorGoToMinusOneAnalyzer(), source);

        CollectionAssert.AreEqual(new[] { 5 }, Lines(findings), "neither a label nor 0");
        AssertAre(findings, RDCoreDiagnosticId.OnErrorGoToMinusOne, DiagnosticSeverity.Hint);
        Assert.AreEqual("On Error GoTo -1", Text(source, findings[0]));
    }

    #endregion

    #region RDC00212 On Error Resume Next that no On Error GoTo follows

    [TestMethod]
    public void AResumeNextThatNothingFollows_IsReported()
    {
        var source = Module("Public Sub DoSomething()\n    On Error Resume Next\n    Debug.Print 1 / 0\nEnd Sub");
        var findings = Run(new UnrestoredOnErrorResumeNextAnalyzer(), source);

        CollectionAssert.AreEqual(new[] { 2 }, Lines(findings));
        AssertAre(findings, RDCoreDiagnosticId.UnrestoredOnErrorResumeNext, DiagnosticSeverity.Warning);
        Assert.AreEqual("On Error Resume Next", Text(source, findings[0]));
    }

    [TestMethod]
    [DataRow("On Error GoTo 0", DisplayName = "GoTo 0")]
    [DataRow("On Error GoTo Handler", DisplayName = "GoTo a label")]
    public void AnOnErrorGoToAfterIt_SetsAnotherPolicy(string after)
        => Assert.IsEmpty(Run(new UnrestoredOnErrorResumeNextAnalyzer(),
            Module($"Public Sub DoSomething()\n    On Error Resume Next\n    Debug.Print 1 / 0\n    {after}\n    Exit Sub\nHandler:\nEnd Sub")));

    [TestMethod]
    public void OnErrorGoToMinusOne_KeepsResumeNext()
        => CollectionAssert.AreEqual(new[] { 2 }, Lines(Run(new UnrestoredOnErrorResumeNextAnalyzer(),
            Module("Public Sub DoSomething()\n    On Error Resume Next\n    Debug.Print 1 / 0\n    On Error GoTo -1\n    Debug.Print 1 / 0\nEnd Sub"))),
            "-1 ends the error being handled and keeps the policy");

    [TestMethod]
    public void EachProcedure_IsReadByItself()
    {
        var findings = Run(new UnrestoredOnErrorResumeNextAnalyzer(), Module(
            "Public Sub First()\n    On Error GoTo 0\n    If True Then\n        On Error Resume Next\n    End If\nEnd Sub\nPublic Sub Second()\n    On Error Resume Next\n    On Error GoTo 0\nEnd Sub"));

        CollectionAssert.AreEqual(new[] { 4 }, Lines(findings), "a statement in a block counts, and the second procedure restores its own");
    }

    #endregion

    #region RDC00117 a constant that states no type

    [TestMethod]
    public void AConstantWithNoType_IsReported()
    {
        var source = Module("Private Const myInteger = 12345\nPrivate Const myLong As Long = 12345\nPrivate Const myShort% = 12345\nPublic Sub DoSomething()\n    Const local = \"a\"\nEnd Sub");
        var findings = Run(new ImplicitConstantTypeAnalyzer(), source);

        CollectionAssert.AreEqual(new[] { 1, 5 }, Lines(findings), "neither As nor a type character, of the module and of a procedure");
        AssertAre(findings, RDCoreDiagnosticId.ImplicitConstantType, DiagnosticSeverity.Information);
        Assert.AreEqual("myInteger", Text(source, findings[0]));
        StringAssert.Contains(findings[1].Message, "'local'");
    }

    #endregion

    #region RDC00408 an underscore in a member of the interface of a class

    private static async Task<AnalyzerFinding[]> ClassAsync(string name, params string[] lines)
    {
        var button = ModuleWorkspace.ClassModule("Button", "Public Event Click()", "Public Sub Press()", "    RaiseEvent Click", "End Sub");
        var shape = ModuleWorkspace.ClassModule("IShape", "Public Sub Draw()", "End Sub");
        var source = ModuleWorkspace.ClassModule(name, lines);
        var payload = await ModuleWorkspace.SemanticsAsync([("Button", button), ("IShape", shape), (name, source)], "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nEnd Sub\r\n");
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == name);
        return Run(new UnderscoreInPublicClassMemberAnalyzer(), source, model, new Uri(Path.Combine(ModuleWorkspace.Root, $"{name}.cls")));
    }

    [TestMethod]
    public async Task APublicMemberWithAnUnderscore_MakesTheClassImpossibleToImplement()
    {
        var findings = await ClassAsync("Widget",
            "Public Max_Size As Long",
            "Public Sub Do_Something()", "End Sub",
            "Function Get_Value() As Long", "End Function",
            "Public Sub DoSomething()", "End Sub");

        Assert.HasCount(3, findings, "a Public variable, and a Public and an implicitly public procedure");
        AssertAre(findings, RDCoreDiagnosticId.UnderscoreInPublicClassMember, DiagnosticSeverity.Warning);
        StringAssert.Contains(findings[1].Message, "'Do_Something'");
    }

    [TestMethod]
    public async Task APrivateMember_AnImplementation_AndAHandler_AreNotInTheInterface()
        => Assert.IsEmpty(await ClassAsync("Square",
            "Implements IShape", "Private WithEvents Source As Button", "Private Max_Size As Long", "Dim Min_Size As Long",
            "Private Sub Do_Something()", "    Dim local_value As Long", "End Sub",
            "Public Sub IShape_Draw()", "End Sub",
            "Public Sub Source_Click()", "End Sub"));

    [TestMethod]
    public void AStandardModule_HasNoInterface()
        => Assert.IsEmpty(Run(new UnderscoreInPublicClassMemberAnalyzer(), Module("Public Sub Do_Something()\nEnd Sub"), new ModuleSemanticsDto(Program, true, [], [], [])));

    #endregion
}
