using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.ProtocolExtensions;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// What the flow of a procedure tells of its variables: a read that only the initial value reaches (<c>RDC01021</c>), an assignment that nothing reads
/// (<c>RDC01022</c>), and a parameter passed by reference that its procedure never changes (<c>RDC01023</c>).
/// </summary>
[TestClass]
public sealed class VariableFlowAnalyzerTests
{
    private static AnalyzerFinding[] Run(IModuleAnalyzer analyzer, string source, ModuleSemanticsDto? semantics, Uri uri)
    {
        var parsed = new ModuleParser().Parse(uri, source);
        Assert.IsEmpty(parsed.SyntaxErrors, string.Join("; ", parsed.SyntaxErrors.Select(error => error.Verbose)));
        return [.. analyzer.Analyze(new ModuleAnalysisContext(uri, parsed, semantics)).OrderBy(finding => finding.Range.Start)];
    }

    // the analyzer, given the program as the host models it.
    private static async Task<AnalyzerFinding[]> ProgramAsync(IModuleAnalyzer analyzer, params string[] lines)
    {
        var program = ModuleWorkspace.StandardModule("Program", lines);
        var payload = await ModuleWorkspace.SemanticsAsync([], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        return Run(analyzer, program, model, new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas")));
    }

    // the analyzer, given a class module of a project as the host models it.
    private static async Task<AnalyzerFinding[]> ClassAsync(IModuleAnalyzer analyzer, string name, IReadOnlyList<(string Name, string Source)> classes)
    {
        var payload = await ModuleWorkspace.SemanticsAsync(classes, ModuleWorkspace.StandardModule("Program", "Public Sub Main()", "End Sub"));
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == name);
        return Run(analyzer, classes.Single(module => module.Name == name).Source, model, new Uri(Path.Combine(ModuleWorkspace.Root, $"{name}.cls")));
    }

    // the line and the column each finding starts at.
    private static string At(AnalyzerFinding[] findings)
        => string.Join(" ", findings.Select(finding => $"{finding.Range.Start.Line}:{finding.Range.Start.Character}"));

    private static void AssertAre(AnalyzerFinding[] findings, RDCoreDiagnosticId id, DiagnosticSeverity severity)
        => Assert.IsTrue(findings.All(finding => finding.Severity == severity && finding.Id == id),
            string.Join("; ", findings.Select(finding => finding.Message)));

    #region RDC01021 a read that only the initial value reaches

    [TestMethod]
    public async Task AReadBeforeAnyAssignment_ReadsTheInitialValue()
    {
        var findings = await ProgramAsync(new UnassignedVariableUsageAnalyzer(),
            "Option Explicit",
            "Public Sub Main()",
            "    Dim total As Long",
            "    Debug.Print total",
            "    Dim name As String",
            "    MsgBox name",
            "    name = \"Report\"",
            "    Debug.Print name",
            "End Sub");

        Assert.AreEqual("4:16 6:11", At(findings));
        AssertAre(findings, RDCoreDiagnosticId.UnassignedVariableUsage, DiagnosticSeverity.Warning);
        StringAssert.Contains(findings[0].Message, "'total'");
    }

    [TestMethod]
    public async Task AReadThatAnAssignmentReachesOnSomePath_IsNotReported()
        => Assert.IsEmpty(await ProgramAsync(new UnassignedVariableUsageAnalyzer(),
            "Option Explicit",
            "Public Sub Main()",
            "    Dim n As Long",
            "    If Rnd() > 0.5 Then n = 1",
            "    Debug.Print n",
            "End Sub"));

    [TestMethod]
    public async Task AReadInALoopAfterTheAssignment_IsNotReported()
        => Assert.IsEmpty(await ProgramAsync(new UnassignedVariableUsageAnalyzer(),
            "Option Explicit",
            "Public Sub Main()",
            "    Dim i As Long, last As Long",
            "    For i = 1 To 3",
            "        If i > 1 Then Debug.Print last",
            "        last = i",
            "    Next",
            "End Sub"));

    [TestMethod]
    public async Task WhatMayAssignTheVariable_IsAnAssignment()
        => Assert.IsEmpty(await ProgramAsync(new UnassignedVariableUsageAnalyzer(),
            "Option Explicit",
            "Public Sub Main()",
            "    Dim filled As Long",
            "    Fill filled",
            "    Debug.Print filled",
            "    Dim counter As Long",
            "    For counter = 1 To 2: Next",
            "    Debug.Print counter",
            "End Sub",
            "Private Sub Fill(ByRef target As Long)",
            "    target = 42",
            "End Sub"));

    [TestMethod]
    public async Task AnArray_AUserDefinedType_AndAnAsNewVariable_AreNotReported()
        => Assert.IsEmpty(await ProgramAsync(new UnassignedVariableUsageAnalyzer(),
            "Option Explicit",
            "Private Type Point",
            "    X As Long",
            "End Type",
            "Public Sub Main()",
            "    Dim values(1 To 3) As Long",
            "    Debug.Print values(1)",
            "    Dim p As Point",
            "    Debug.Print p.X",
            "    Dim items As New Collection",
            "    Debug.Print items.Count",
            "End Sub"));

    [TestMethod]
    public async Task AReadInTheArgumentOfLen_AsksForTheSize()
        => Assert.IsEmpty(await ProgramAsync(new UnassignedVariableUsageAnalyzer(),
            "Option Explicit",
            "Public Sub Main()",
            "    Dim n As Long",
            "    Debug.Print Len(n), LenB(n)",
            "End Sub"));

    [TestMethod]
    public async Task AStaticVariable_KeepsItsValue()
        => Assert.IsEmpty(await ProgramAsync(new UnassignedVariableUsageAnalyzer(),
            "Option Explicit",
            "Public Sub Main()",
            "    Static calls As Long",
            "    Debug.Print calls",
            "    calls = calls + 1",
            "End Sub"));

    [TestMethod]
    public async Task AModuleVariable_IsNotReported()
        => Assert.IsEmpty(await ProgramAsync(new UnassignedVariableUsageAnalyzer(),
            "Option Explicit",
            "Private count As Long",
            "Public Sub Main()",
            "    Debug.Print count",
            "End Sub"));

    [TestMethod]
    public void WithoutTheHostsModel_NothingIsReported()
    {
        var source = ModuleWorkspace.StandardModule("Program", "Public Sub Main()", "    Dim n As Long", "    Debug.Print n", "End Sub");
        Assert.IsEmpty(Run(new UnassignedVariableUsageAnalyzer(), source, null, new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"))));
    }

    #endregion

    #region RDC01022 an assignment nothing reads

    [TestMethod]
    public async Task AValueReplacedBeforeItIsRead_IsNotUsed()
    {
        var findings = await ProgramAsync(new AssignmentNotUsedAnalyzer(),
            "Option Explicit",
            "Public Sub Main()",
            "    Dim total As Long",
            "    total = 0",
            "    total = Count()",
            "    Debug.Print total",
            "    Dim result As Long",
            "    result = Count()",
            "End Sub",
            "Private Function Count() As Long",
            "    Count = 3",
            "End Function");

        Assert.AreEqual("4:4 8:4", At(findings));
        AssertAre(findings, RDCoreDiagnosticId.AssignmentNotUsed, DiagnosticSeverity.Information);
        StringAssert.Contains(findings[0].Message, "'total'");
    }

    [TestMethod]
    public async Task AValueReadOnSomePath_IsUsed()
        => Assert.IsEmpty(await ProgramAsync(new AssignmentNotUsedAnalyzer(),
            "Option Explicit",
            "Public Sub Main()",
            "    Dim n As Long",
            "    n = 1",
            "    If Rnd() > 0.5 Then n = 2",
            "    Debug.Print n",
            "    Dim i As Long, sum As Long",
            "    For i = 1 To 3",
            "        sum = sum + i",
            "    Next",
            "    Debug.Print sum",
            "End Sub"));

    [TestMethod]
    public async Task SettingAnObjectToNothing_ReleasesIt()
        => Assert.IsEmpty(await ProgramAsync(new AssignmentNotUsedAnalyzer(),
            "Option Explicit",
            "Public Sub Main()",
            "    Dim items As Collection",
            "    Set items = New Collection",
            "    items.Add 1",
            "    Set items = Nothing",
            "End Sub"));

    [TestMethod]
    public async Task AByValParameter_IsReported_AndAByRefOne_IsTheCallers()
    {
        var findings = await ProgramAsync(new AssignmentNotUsedAnalyzer(),
            "Option Explicit",
            "Public Sub Main()",
            "    Dim n As Long",
            "    Fill n, 1",
            "    Debug.Print n",
            "End Sub",
            "Private Sub Fill(ByRef target As Long, ByVal copy As Long)",
            "    target = 1",
            "    target = 2",
            "    copy = 3",
            "End Sub");

        Assert.AreEqual("10:4", At(findings));
    }

    [TestMethod]
    public async Task AnArray_AndAModuleVariable_AreNotReported()
        => Assert.IsEmpty(await ProgramAsync(new AssignmentNotUsedAnalyzer(),
            "Option Explicit",
            "Private last As Long",
            "Public Sub Main()",
            "    Dim values(1 To 2) As Long",
            "    values(1) = 1",
            "    last = 2",
            "End Sub"));

    #endregion

    #region RDC01023 a parameter that can be passed by value

    [TestMethod]
    public async Task AByRefParameterOnlyRead_CanBeByVal()
    {
        var findings = await ProgramAsync(new ParameterCanBeByValAnalyzer(),
            "Option Explicit",
            "Public Sub Main()",
            "    Show 1, 2",
            "End Sub",
            "Private Sub Show(count As Long, ByRef total As Long)",
            "    Debug.Print count, total",
            "End Sub");

        Assert.AreEqual("5:17 5:38", At(findings));
        AssertAre(findings, RDCoreDiagnosticId.ParameterCanBeByVal, DiagnosticSeverity.Information);
        StringAssert.Contains(findings[0].Message, "'count'");
    }

    [TestMethod]
    public async Task AParameterAssigned_OrPassedOnByReference_IsNotReported()
        => Assert.IsEmpty(await ProgramAsync(new ParameterCanBeByValAnalyzer(),
            "Option Explicit",
            "Public Sub Main()",
            "    Dim a As Long, b As Long",
            "    Work a, b",
            "End Sub",
            "Private Sub Work(ByRef assigned As Long, passedOn As Long)",
            "    assigned = 1",
            "    Fill passedOn",
            "End Sub",
            "Private Sub Fill(ByRef target As Long)",
            "    target = 42",
            "End Sub"));

    [TestMethod]
    public async Task AnArray_AParamArray_AndAUserDefinedType_AreNotReported()
        => Assert.IsEmpty(await ProgramAsync(new ParameterCanBeByValAnalyzer(),
            "Option Explicit",
            "Private Type Point",
            "    X As Long",
            "End Type",
            "Public Sub Main()",
            "End Sub",
            "Private Sub Work(values() As Long, p As Point, ParamArray rest() As Variant)",
            "    Debug.Print values(1), p.X, rest(0)",
            "End Sub"));

    [TestMethod]
    public async Task AnImplementation_KeepsItsInterfacesSignature()
        => Assert.IsEmpty(await ClassAsync(new ParameterCanBeByValAnalyzer(), "Square",
        [
            ("Shape", ModuleWorkspace.ClassModule("Shape", "Option Explicit", "Public Sub Draw(ByRef size As Long)", "End Sub")),
            ("Square", ModuleWorkspace.ClassModule("Square", "Option Explicit", "Implements Shape",
                "Private Sub Shape_Draw(ByRef size As Long)", "    Debug.Print size", "End Sub")),
        ]));

    [TestMethod]
    [Ignore("the host does not state yet whether an implementation or a handler writes its parameter (DeclarationFact.IsWrittenByABoundMember)")]
    public async Task AnInterfaceMembersParameter_ThatNoImplementationWrites_CanBeByVal()
        => Assert.ContainsSingle(await ClassAsync(new ParameterCanBeByValAnalyzer(), "Shape",
        [
            ("Shape", ModuleWorkspace.ClassModule("Shape", "Option Explicit", "Public Sub Draw(ByRef size As Long)", "End Sub")),
            ("Square", ModuleWorkspace.ClassModule("Square", "Option Explicit", "Implements Shape",
                "Private Sub Shape_Draw(ByRef size As Long)", "    Debug.Print size", "End Sub")),
        ]));

    // the module with every edit of the fix made, the last one first.
    private static string WithEdits(string program, EditFix fix)
    {
        var lines = program.Split("\r\n");
        int OffsetOf(Position position) => lines.Take(position.Line).Sum(line => line.Length + 2) + position.Character;
        foreach (var edit in fix.Edits.OrderByDescending(edit => OffsetOf(edit.Range.Start)))
        {
            program = program[..OffsetOf(edit.Range.Start)] + edit.NewText + program[OffsetOf(edit.Range.End)..];
        }

        return program;
    }

    [TestMethod]
    [DataRow("ByRef count As Long", DisplayName = "explicitly ByRef")]
    [DataRow("count As Long", DisplayName = "implicitly ByRef")]
    public async Task TheFix_PassesTheParameterByValue(string declared)
    {
        string[] Lines(string parameter)
            => ["Option Explicit", "Public Sub Main()", "    Show 1", "End Sub", $"Private Sub Show({parameter})", "    Debug.Print count", "End Sub"];
        var program = ModuleWorkspace.StandardModule("Program", Lines(declared));
        var fix = Assert.ContainsSingle(await ProgramAsync(new ParameterCanBeByValAnalyzer(), Lines(declared))).Fixes?.Edits.SingleOrDefault();
        Assert.IsNotNull(fix);
        Assert.AreEqual("Pass 'count' by value", fix.Title);

        Assert.AreEqual(ModuleWorkspace.StandardModule("Program", Lines("ByVal count As Long")), WithEdits(program, fix));
        Assert.IsEmpty(await ProgramAsync(new ParameterCanBeByValAnalyzer(), Lines("ByVal count As Long")), "the parameter is passed by value");
    }

    #endregion
}
