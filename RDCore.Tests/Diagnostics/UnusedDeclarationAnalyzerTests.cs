using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// What a module declares and does not use, as the host counts the references: a variable never read (<c>RDC01011</c>) or never assigned (<c>RDC01012</c>), a
/// parameter never used (<c>RDC01013</c>) or passed by value and assigned (<c>RDC01014</c>), a constant never used (<c>RDC01015</c>); and, from the syntax
/// tree, a line label nothing jumps to
/// (<c>RDC01016</c>) and a property that cannot be read (<c>RDC01017</c>).
/// </summary>
[TestClass]
public sealed class UnusedDeclarationAnalyzerTests
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

    // the analyzer, given a class module as the host models it.
    private static async Task<AnalyzerFinding[]> ClassAsync(IModuleAnalyzer analyzer, string name, params string[] lines)
    {
        var button = ModuleWorkspace.ClassModule("Button", "Public Event Click(ByVal times As Long)", "Public Sub Press()", "    RaiseEvent Click(1)", "End Sub");
        var source = ModuleWorkspace.ClassModule(name, lines);
        var payload = await ModuleWorkspace.SemanticsAsync([("Button", button), (name, source)], ModuleWorkspace.StandardModule("Program", "Public Sub Main()", "End Sub"));
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == name);
        return Run(analyzer, source, model, new Uri(Path.Combine(ModuleWorkspace.Root, $"{name}.cls")));
    }

    private static int[] Lines(AnalyzerFinding[] findings) => [.. findings.Select(finding => finding.Range.Start.Line)];

    private static void AssertAre(AnalyzerFinding[] findings, RDCoreDiagnosticId id, DiagnosticSeverity severity)
        => Assert.IsTrue(findings.All(finding => finding.Severity == severity && finding.Id == id), string.Join("; ", findings.Select(finding => finding.Message)));

    private static AnalyzerFinding[] WithoutSemantics(IModuleAnalyzer analyzer, params string[] lines)
        => Run(analyzer, ModuleWorkspace.StandardModule("Program", lines), null, new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas")));

    #region RDC01011 a variable never read

    [TestMethod]
    public async Task AVariableNeverRead_IsNotUsed()
    {
        var findings = await ProgramAsync(new VariableNotUsedAnalyzer(),
            "Option Explicit",
            "Private field As Long",
            "Public Everyone As Long",
            "Public Sub Main()",
            "    Dim unused As Long",
            "    Dim assigned As Long",
            "    assigned = 1",
            "    Dim used As Long",
            "    used = 2",
            "    Debug.Print used",
            "    Dim i As Long",
            "    For i = 1 To 2",
            "    Next",
            "End Sub");

        CollectionAssert.AreEqual(new[] { 2, 5, 6 }, Lines(findings), "a Private variable of the module and two locals; not a Public one, not a loop's counter");
        AssertAre(findings, RDCoreDiagnosticId.VariableNotUsed, DiagnosticSeverity.Information);
        StringAssert.Contains(findings[2].Message, "assigned", "the variable that is only assigned says so");
    }

    [TestMethod]
    public void WithNoSemantics_NoVariableIsCounted()
        => Assert.IsEmpty(WithoutSemantics(new VariableNotUsedAnalyzer(), "Public Sub Main()", "    Dim unused As Long", "End Sub"));

    #endregion

    #region RDC01012 a variable never assigned

    [TestMethod]
    public async Task AVariableReadAndNeverAssigned_HasItsDefaultValue()
    {
        var findings = await ProgramAsync(new VariableNotAssignedAnalyzer(),
            "Option Explicit",
            "Private field As Long",
            "Public Sub Main()",
            "    Dim never As Long",
            "    Debug.Print never + field",
            "    Dim passed As Long",
            "    Bump passed",
            "    Debug.Print passed",
            "    Dim assigned As String",
            "    assigned = \"a\"",
            "    Debug.Print assigned",
            "End Sub",
            "Private Sub Bump(ByRef x As Long)",
            "    x = x + 1",
            "End Sub");

        CollectionAssert.AreEqual(new[] { 2, 4 }, Lines(findings), "not one passed to a ByRef parameter, not one assigned");
        AssertAre(findings, RDCoreDiagnosticId.VariableNotAssigned, DiagnosticSeverity.Warning);
    }

    [TestMethod]
    public async Task AnArray_AnObject_AndAReDim_AreGivenWhatTheyHoldOtherwise()
        => Assert.IsEmpty(await ProgramAsync(new VariableNotAssignedAnalyzer(),
            "Option Explicit",
            "Public Sub Main()",
            "    Dim values(2) As Long",
            "    Debug.Print values(0)",
            "    Dim items As Collection",
            "    Debug.Print items Is Nothing",
            "    Dim resized As Variant",
            "    ReDim resized(2)",
            "    Debug.Print resized(0)",
            "End Sub"));

    #endregion

    #region RDC01013 a parameter never used

    [TestMethod]
    public async Task AParameterNothingRefersTo_IsNotUsed()
    {
        var findings = await ProgramAsync(new ParameterNotUsedAnalyzer(),
            "Option Explicit",
            "Public Sub Work(ByVal used As Long, ByVal unused As Long)",
            "    Debug.Print used",
            "End Sub");

        CollectionAssert.AreEqual(new[] { 2 }, Lines(findings));
        AssertAre(findings, RDCoreDiagnosticId.ParameterNotUsed, DiagnosticSeverity.Information);
        StringAssert.Contains(findings[0].Message, "'unused'");
    }

    [TestMethod]
    public async Task ASignatureTheProcedureDoesNotChoose_IsNotReported()
    {
        var findings = await ClassAsync(new ParameterNotUsedAnalyzer(), "Panel",
            "Option Explicit",
            "Private WithEvents Source As Button",
            "Private Sub Source_Click(ByVal times As Long)",
            "End Sub",
            "Public Sub Show(ByVal mode As Long)",
            "End Sub",
            "Private Sub Hide(ByVal mode As Long)",
            "End Sub");

        Assert.HasCount(1, findings, "only the Private procedure: not a handler, not a Public member that may be in an interface");
        StringAssert.Contains(findings[0].Message, "'mode'");
    }

    #endregion

    #region RDC01014 a ByVal parameter assigned

    [TestMethod]
    public async Task AByValParameterAssigned_DoesNotChangeTheArgument()
    {
        var findings = await ProgramAsync(new AssignedByValParameterAnalyzer(),
            "Option Explicit",
            "Public Sub Work(ByVal copy As Long, ByRef target As Long, implicit As Long)",
            "    copy = 1",
            "    target = 2",
            "    implicit = 3",
            "End Sub");

        CollectionAssert.AreEqual(new[] { 2 }, Lines(findings));
        AssertAre(findings, RDCoreDiagnosticId.AssignedByValParameter, DiagnosticSeverity.Information);
        StringAssert.Contains(findings[0].Message, "'copy'");
    }

    #endregion

    #region RDC01015 a constant never used

    [TestMethod]
    public async Task AConstantNothingRefersTo_IsNotUsed()
    {
        var findings = await ProgramAsync(new ConstantNotUsedAnalyzer(),
            "Option Explicit",
            "Private Const Hidden As Long = 1",
            "Public Sub Main()",
            "    Const Unused As Long = 1",
            "    Const Read As Long = 2",
            "    Debug.Print Read",
            "    Const Passed As Long = 3",
            "    Take Passed",
            "    Const Base As Long = 4",
            "    Const Derived As Long = Base + 1",
            "    Dim items(Derived) As Long",
            "    Debug.Print items(0)",
            "End Sub",
            "Private Sub Take(ByVal value As Long)",
            "    Debug.Print value",
            "End Sub");

        CollectionAssert.AreEqual(new[] { 4 }, Lines(findings), "not one read, passed, or read by another constant or a bound; not a constant of the module, which is not counted");
        AssertAre(findings, RDCoreDiagnosticId.ConstantNotUsed, DiagnosticSeverity.Information);
        StringAssert.Contains(findings[0].Message, "'Unused'");
    }

    [TestMethod]
    public void WithNoSemantics_NoConstantIsCounted()
        => Assert.IsEmpty(WithoutSemantics(new ConstantNotUsedAnalyzer(), "Public Sub Main()", "    Const Unused As Long = 1", "End Sub"));

    [TestMethod]
    public async Task AProcedureConstant_HasItsReferencesCounted()
    {
        var program = ModuleWorkspace.StandardModule("Program",
            "Option Explicit",
            "Public Sub Main()",
            "    Const Unused As Long = 1",
            "    Const Read As Long = 2",
            "    Debug.Print Read",
            "    Const Base As Long = 3",
            "    Const Derived As Long = Base + 1",
            "    Dim items(Derived) As Long",
            "    Debug.Print items(0)",
            "End Sub");
        var payload = await ModuleWorkspace.SemanticsAsync([], program);
        var constants = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program").Declarations
            .Where(declaration => declaration.Kind is DeclarationKind.Constant)
            .ToDictionary(declaration => declaration.Name, declaration => declaration.References?.Reads);

        Assert.AreEqual(0, constants["Unused"], "a constant nothing reads");
        Assert.AreEqual(1, constants["Read"], "read by a statement");
        Assert.AreEqual(1, constants["Base"], "read by the value of another constant");
        Assert.AreEqual(1, constants["Derived"], "read by the bound of an array");
    }

    #endregion

    #region RDC01016 a line label nothing jumps to

    [TestMethod]
    public void ALabelNothingJumpsTo_IsNotUsed()
    {
        var findings = WithoutSemantics(new LineLabelNotUsedAnalyzer(),
            "Public Sub Main()",
            "    On Error GoTo Handler",
            "    GoTo Done",
            "Unused:",
            "10  Debug.Print 1",
            "Done:",
            "    Exit Sub",
            "Handler:",
            "    Resume Done",
            "End Sub");

        CollectionAssert.AreEqual(new[] { 4 }, Lines(findings), "not a line number");
        AssertAre(findings, RDCoreDiagnosticId.LineLabelNotUsed, DiagnosticSeverity.Information);
        StringAssert.Contains(findings[0].Message, "'Unused'");
    }

    [TestMethod]
    public void ALabel_BelongsToItsProcedure()
        => CollectionAssert.AreEqual(new[] { 6 }, Lines(WithoutSemantics(new LineLabelNotUsedAnalyzer(),
            "Public Sub First()",
            "    GoTo Again",
            "Again:",
            "End Sub",
            "Public Sub Second()",
            "Again:",
            "End Sub")));

    #endregion

    #region RDC01017 a property that cannot be read

    [TestMethod]
    public void APublicPropertyWithNoGet_IsWriteOnly()
    {
        var findings = WithoutSemantics(new WriteOnlyPropertyAnalyzer(),
            "Public Property Let Size(ByVal value As Long)",
            "End Property",
            "Public Property Set Size(ByVal value As Object)",
            "End Property",
            "Property Let Width(ByVal value As Long)",
            "End Property",
            "Public Property Get Height() As Long",
            "End Property",
            "Public Property Let Height(ByVal value As Long)",
            "End Property",
            "Private Property Let Depth(ByVal value As Long)",
            "End Property");

        CollectionAssert.AreEqual(new[] { 1, 5 }, Lines(findings), "Size once, and Width; not Height, which has a Get, nor a Private property");
        AssertAre(findings, RDCoreDiagnosticId.WriteOnlyProperty, DiagnosticSeverity.Information);
    }

    #endregion
}
