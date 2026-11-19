using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using RDCore.SDK.Server.ProtocolExtensions;
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
    public async Task AVariableNothingRefersTo_HasItsDeclarationRemoved()
    {
        string[] lines =
        [
            "Option Explicit",
            "Private field As Long",
            "Public Sub Main()",
            "    Dim unused As Long",
            "    Dim assigned As Long",
            "    assigned = 1",
            "    Dim first As Long, second As Long",
            "    second = 2",
            "    Debug.Print second",
            "    Dim noted As Long ' kept for later",
            "    Dim sized() As Long",
            "    ReDim sized(1)",
            "    Debug.Print 1",
            "End Sub",
        ];
        var findings = await ProgramAsync(new VariableNotUsedAnalyzer(), lines);

        CollectionAssert.AreEqual(new[] { 2, 4 }, Lines([.. findings.Where(finding => finding.Fixes?.Edits.SingleOrDefault()is not null)]),
            "the field and the local nothing refers to; not one assigned, one declared with another name, one with a comment on its line, nor one a ReDim names");
        var fix = findings.Single(finding => finding.Range.Start.Line == 4).Fixes!.Edits.Single();
        Assert.AreEqual("Remove the declaration of 'unused'", fix.Title);
        Assert.AreEqual(ModuleWorkspace.StandardModule("Program", [.. lines.Where((_, index) => index != 3)]), fix.Edits.ApplyTo(ModuleWorkspace.StandardModule("Program", lines)));
    }

    [TestMethod]
    public void WithNoSemantics_NoVariableIsCounted()
        => Assert.IsEmpty(WithoutSemantics(new VariableNotUsedAnalyzer(), "Public Sub Main()", "    Dim unused As Long", "End Sub"));

    // MS-VBAL §3.4: the code of a branch that is not live is not compiled, its declarations no more than its statements. The live branch is code like any.
    [TestMethod]
    [TestCategory("MS-VBAL 3.4 Conditional Compilation")]
    public async Task AVariableDeclaredInABranchThatIsNotLive_IsNotDeclared_AndOneOfTheLiveBranchIsCounted()
    {
        var findings = await ProgramAsync(new VariableNotUsedAnalyzer(),
            "Option Explicit",
            "#Const DEV = 1",
            "Public Sub Main()",
            "#If DEV Then",
            "    Dim assigned As Long",
            "    assigned = 1",
            "    Dim unused As Long",
            "#Else",
            "    Dim startTime As Single",
            "    startTime = Timer",
            "    Dim elapsed As Single",
            "    elapsed = Timer - startTime",
            "    Dim never As Long",
            "#End If",
            "End Sub");

        CollectionAssert.AreEqual(new[] { 5, 7 }, Lines(findings), string.Join("; ", findings.Select(finding => finding.Message)));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 3.4 Conditional Compilation")]
    public async Task AVariableDeclaredInEachBranch_IsDeclaredOnce_ByTheLiveBranch()
    {
        var program = ModuleWorkspace.StandardModule("Program",
            "Option Explicit",
            "#Const DEV = 0",
            "Public Sub Main()",
            "#If DEV Then",
            "    Dim n As String",
            "#Else",
            "    Dim n As Long",
            "    n = 1",
            "    Debug.Print n",
            "#End If",
            "End Sub");
        var payload = await ModuleWorkspace.SemanticsAsync([], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");

        var declared = model.Declarations.Where(declaration => declaration.Name == "n").ToArray();

        Assert.AreEqual(1, declared.Length, string.Join(", ", model.Declarations.Select(declaration => $"{declaration.Name} L{declaration.Location.Range.Start.Line}")));
        Assert.AreEqual(7, declared[0].Location.Range.Start.Line);
        Assert.IsEmpty(Run(new VariableNotUsedAnalyzer(), program, model, new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"))));
    }

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

    // MS-VBAL §5.3.1.11: a call assigns an argument only through a ByRef parameter that its procedure writes, or passes on by reference to one that does.
    [TestMethod]
    public async Task AVariablePassedWhereNoProcedureAssignsIt_HasItsDefaultValue()
    {
        var findings = await ProgramAsync(new VariableNotAssignedAnalyzer(),
            "Option Explicit",
            "Public Sub Main()",
            "    Dim byValue As Long",
            "    Show byValue",
            "    Dim onlyRead As Long",
            "    Peek onlyRead",
            "    Dim relayed As Long",
            "    Relay relayed",
            "    Dim recursive As Long",
            "    Spin recursive, 3",
            "    Dim named As Long",
            "    Peek x:=named",
            "    Debug.Print byValue + onlyRead + relayed + recursive + named",
            "End Sub",
            "Private Sub Show(ByVal x As Long)", "    Debug.Print x", "End Sub",
            "Private Sub Peek(ByRef x As Long)", "    Debug.Print x", "End Sub",
            "Private Sub Relay(ByRef x As Long)", "    Bump x", "End Sub",
            "Private Sub Bump(ByRef x As Long)", "    x = x + 1", "End Sub",
            "Private Sub Spin(ByRef x As Long, ByVal n As Long)", "    If n > 0 Then Spin x, n - 1", "End Sub");

        CollectionAssert.AreEqual(new[] { 3, 5, 9, 11 }, Lines(findings),
            "given ByVal, given ByRef to a procedure that only reads it, by name, to one that calls itself and never assigns it; not one relayed to Bump");
        AssertAre(findings, RDCoreDiagnosticId.VariableNotAssigned, DiagnosticSeverity.Warning);
    }

    // a procedure called through an object may be another class's that implements the class; one of another module, or of a library, is not followed.
    [TestMethod]
    public async Task AVariablePassedToAProcedureNotFollowed_IsNotReported()
    {
        var findings = await ClassAsync(new VariableNotAssignedAnalyzer(), "Counter",
            "Option Explicit",
            "Public Sub Peek(ByRef x As Long)", "    Debug.Print x", "End Sub",
            "Public Sub Main(ByVal other As Counter)",
            "    Dim throughObject As Long",
            "    other.Peek throughObject",
            "    Dim throughMe As Long",
            "    Me.Peek throughMe",
            "    Debug.Print throughObject + throughMe",
            "End Sub");

        CollectionAssert.AreEqual(new[] { 12 }, Lines(findings), "only the one passed through Me, whose procedure is the class's own");
    }

    [TestMethod]
    public async Task ADynamicArray_AnObject_AndAReDim_AreGivenWhatTheyHoldOtherwise()
        => Assert.IsEmpty(await ProgramAsync(new VariableNotAssignedAnalyzer(),
            "Option Explicit",
            "Public Sub Main()",
            "    Dim values() As Long",
            "    Debug.Print UBound(values)",
            "    Dim items As Collection",
            "    Debug.Print items Is Nothing",
            "    Dim resized As Variant",
            "    ReDim resized(2)",
            "    Debug.Print resized(0)",
            "End Sub"));

    [TestMethod]
    [DataRow("    o.MoveNext", DisplayName = "a member call")]
    [DataRow("    Debug.Print o Is Nothing", DisplayName = "an Is test")]
    [DataRow("    Dim other As Object\r\n    Set other = o\r\n    Debug.Print other Is Nothing", DisplayName = "a Set from it")]
    public async Task AnObjectVariableThatNoSetAssigns_IsNotAssigned(string read)
    {
        var finding = Assert.ContainsSingle(await ProgramAsync(new VariableNotAssignedAnalyzer(),
            "Option Explicit", "Public Sub Main()", "    Dim o As Object", read, "End Sub"));

        Assert.AreEqual(RDCoreDiagnosticId.VariableNotAssigned, finding.Id);
        StringAssert.Contains(finding.Message, "'o'");
    }

    [TestMethod]
    public async Task AFixedArrayNoElementOfWhichIsWritten_IsNotAssigned()
        => StringAssert.Contains(Assert.ContainsSingle(await ProgramAsync(new VariableNotAssignedAnalyzer(),
            "Option Explicit",
            "Public Sub Main()",
            "    Dim totals(1 To 12) As Currency",
            "    Debug.Print totals(1)",
            "End Sub")).Message, "'totals'");

    [TestMethod]
    [DataRow("    totals(3) = 5", DisplayName = "an element assigned")]
    [DataRow("    Fill totals(3)", DisplayName = "an element passed ByRef")]
    public async Task AFixedArrayAnElementOfWhichIsWritten_IsAssigned(string write)
        => Assert.IsEmpty(await ProgramAsync(new VariableNotAssignedAnalyzer(),
            "Option Explicit",
            "Public Sub Main()",
            "    Dim totals(1 To 12) As Currency",
            write,
            "    Debug.Print totals(3)",
            "End Sub",
            "Private Sub Fill(ByRef value As Currency)",
            "    value = 1",
            "End Sub"));

    [TestMethod]
    public async Task AReDimOfAnotherProcedure_DoesNotLeaveOutAVariableOfTheSameName()
    {
        var finding = Assert.ContainsSingle(await ProgramAsync(new VariableNotAssignedAnalyzer(),
            "Option Explicit",
            "Public Sub Main()",
            "    Dim n As Long",
            "    Debug.Print n",
            "End Sub",
            "Public Sub Resize()",
            "    Dim n() As Long",
            "    ReDim n(3)",
            "    Debug.Print n(0)",
            "End Sub"));

        // the n of Resize is an array, and left out whatever its ReDim: the finding is Main's.
        StringAssert.Contains(finding.Message, "'n'");
    }

    [TestMethod]
    public async Task AnObjectVariableThatASetAssigns_IsAssigned()
        => Assert.IsEmpty(await ProgramAsync(new VariableNotAssignedAnalyzer(),
            "Option Explicit", "Public Sub Main()", "    Dim o As Object", "    Set o = New Collection", "    Debug.Print o.Count", "End Sub"));

    [TestMethod]
    [DataRow("    Dim o As Object", "    Debug.Print o Is Nothing", DisplayName = "an object, which only a Set assigns")]
    [DataRow("    Dim totals(1 To 12) As Currency", "    Debug.Print totals(1)", DisplayName = "a fixed-size array")]
    public async Task NoFixIsOffered_ForAnObjectOrAnArray(string declaration, string read)
        => Assert.IsNull(Assert.ContainsSingle(await ProgramAsync(new VariableNotAssignedAnalyzer(),
            "Option Explicit", "Public Sub Main()", declaration, read, "End Sub")).Fixes?.Edits.SingleOrDefault());

    [TestMethod]
    [DataRow("Dim n As Long", "n = 0", "0", DisplayName = "a Long")]
    [DataRow("Dim n As Date", "n = #12:00:00 AM#", "0", DisplayName = "a Date")]
    [DataRow("Dim n As String", "n = vbNullString", "0", DisplayName = "a String, which StrPtr tells from an empty one")]
    [DataRow("Dim n As Boolean", "n = False", "False", DisplayName = "a Boolean")]
    [DataRow("Dim n", "n = Empty", "True", DisplayName = "a Variant")]
    [DataRow("Dim n&", "n = 0", "0", DisplayName = "a type character")]
    [DataRow("Static n As Long", "n = 0", "0", DisplayName = "a Static local")]
    public async Task TheFix_AssignsTheDefaultValueAfterTheDeclaration_AndTheProgramPrintsTheSame(string declaration, string assignment, string printed)
    {
        // a Date prints in the session's regional settings; its serial number does not.
        var read = declaration.Contains("String") ? "StrPtr(n)" : declaration.Contains("Date") ? "CDbl(n)" : declaration == "Dim n" ? "IsEmpty(n)" : "n";
        string[] before = ["Public Sub Main()", $"    {declaration}", $"    Debug.Print {read}", "End Sub"];
        var program = ModuleWorkspace.StandardModule("Program", before);

        var fix = Assert.ContainsSingle(await ProgramAsync(new VariableNotAssignedAnalyzer(), before)).Fixes?.Edits.SingleOrDefault();

        Assert.IsNotNull(fix);
        var edit = Assert.ContainsSingle(fix.Edits);
        Assert.AreEqual(new Position(3, 0), edit.Range.Start, "on the line after the declaration");
        Assert.AreEqual($"    {assignment}\r\n", edit.NewText);
        var lines = program.Split("\r\n").ToList();
        lines.Insert(3, $"    {assignment}");
        var fixedProgram = string.Join("\r\n", lines);
        CollectionAssert.AreEqual(new[] { printed }, await ModuleWorkspace.RunAsync([], program), "before the fix");
        CollectionAssert.AreEqual(new[] { printed }, await ModuleWorkspace.RunAsync([], fixedProgram), "after the fix");
    }

    [TestMethod]
    [DataRow("Dim s As String * 3", DisplayName = "a fixed-length string, whose default is null characters")]
    [DataRow("Dim n", "DefLng A-Z", DisplayName = "no As clause where a DefType statement may type it")]
    public async Task NoFixIsOffered_WhereTheDefaultHasNoLiteral(string declaration, string directive = "")
    {
        var name = declaration.Split(' ')[1];
        string[] lines = [.. directive.Length > 0 ? [directive] : Array.Empty<string>(), "Public Sub Main()", $"    {declaration}", $"    Debug.Print {name}", "End Sub"];

        Assert.IsNull(Assert.ContainsSingle(await ProgramAsync(new VariableNotAssignedAnalyzer(), lines)).Fixes?.Edits.SingleOrDefault());
    }

    [TestMethod]
    public async Task NoFixIsOffered_ForAFieldOfTheModule()
        => Assert.IsNull(Assert.ContainsSingle(await ProgramAsync(new VariableNotAssignedAnalyzer(),
            "Private count As Long", "Public Sub Main()", "    Debug.Print count", "End Sub")).Fixes?.Edits.SingleOrDefault());

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

    [TestMethod]
    public async Task ThePropertyThatImplementsAnInterfaceMember_DoesNotChooseItsSignature_FromItsGetOn()
    {
        // a property's declaration is its first accessor, a Get here. Its Let still implements the interface, so its value is the interface member's and is
        // not reported, whether that member is a property (Tag) or a variable (Label): only Other's value is.
        var settings = ModuleWorkspace.ClassModule("ISettings", "Public Label As String",
            "Public Property Get Tag() As Variant", "End Property", "Public Property Let Tag(ByVal value As Variant)", "End Property");
        string[] lines =
        [
            "Option Explicit",
            "Implements ISettings",
            "Private Property Get ISettings_Label() As String",
            "End Property",
            "Private Property Let ISettings_Label(ByVal value As String)",
            "End Property",
            "Private Property Get ISettings_Tag() As Variant",
            "End Property",
            "Private Property Let ISettings_Tag(ByVal value As Variant)",
            "End Property",
            "Private Property Get Other() As Long",
            "End Property",
            "Private Property Let Other(ByVal value As Long)",
            "End Property",
        ];
        var source = ModuleWorkspace.ClassModule("Settings", lines);
        var payload = await ModuleWorkspace.SemanticsAsync([("ISettings", settings), ("Settings", source)], ModuleWorkspace.StandardModule("Program", "Public Sub Main()", "End Sub"));
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Settings");

        var findings = Run(new ParameterNotUsedAnalyzer(), source, model, new Uri(Path.Combine(ModuleWorkspace.Root, "Settings.cls")));

        Assert.HasCount(1, findings, "only the property that implements nothing: " + string.Join("; ", findings.Select(finding => $"{finding.Range.Start.Line}: {finding.Message}")));
        Assert.AreEqual(17, findings[0].Range.Start.Line);
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

    private static async Task AssertLocalCopyAsync(string[] before, string[] after, string title)
    {
        var program = ModuleWorkspace.StandardModule("Program", before);
        var fix = Assert.ContainsSingle(await ProgramAsync(new AssignedByValParameterAnalyzer(), before)).Fixes?.Edits.SingleOrDefault();
        Assert.IsNotNull(fix);
        Assert.AreEqual(title, fix.Title);

        var fixedProgram = WithEdits(program, fix);
        Assert.AreEqual(ModuleWorkspace.StandardModule("Program", after), fixedProgram);
        Assert.IsEmpty(await ProgramAsync(new AssignedByValParameterAnalyzer(), after), "the parameter is no longer assigned");
        CollectionAssert.AreEqual(await ModuleWorkspace.RunAsync([], program), await ModuleWorkspace.RunAsync([], fixedProgram), "the program prints the same");
    }

    [TestMethod]
    public async Task TheFix_CopiesTheParameterIntoALocal_AndUsesIt()
        => await AssertLocalCopyAsync(
            ["Public Sub Main()", "    Debug.Print Twice(3)", "End Sub",
             "Public Function Twice(ByVal n As Long) As Long", "    n = n * 2", "    Twice = n", "End Function"],
            ["Public Sub Main()", "    Debug.Print Twice(3)", "End Sub",
             "Public Function Twice(ByVal n As Long) As Long", "    Dim localN As Long", "    localN = n", "    localN = localN * 2", "    Twice = localN", "End Function"],
            "Copy 'n' into a local variable 'localN', and use it in place of the parameter");

    [TestMethod]
    public async Task TheFix_CopiesAnObjectWithSet()
        => await AssertLocalCopyAsync(
            ["Public Sub Main()", "    Dim c As New Collection", "    c.Add 1", "    Show c", "End Sub",
             "Public Sub Show(ByVal items As Collection)", "    Debug.Print items.Count", "    Set items = Nothing", "    Debug.Print items Is Nothing", "End Sub"],
            ["Public Sub Main()", "    Dim c As New Collection", "    c.Add 1", "    Show c", "End Sub",
             "Public Sub Show(ByVal items As Collection)", "    Dim localItems As Collection", "    Set localItems = items", "    Debug.Print localItems.Count",
             "    Set localItems = Nothing", "    Debug.Print localItems Is Nothing", "End Sub"],
            "Copy 'items' into a local variable 'localItems', and use it in place of the parameter");

    [TestMethod]
    public async Task TheFix_CopiesAVariant_AsWhatItHolds()
        => await AssertLocalCopyAsync(
            ["Public Sub Main()", "    Bump 41", "End Sub",
             "Public Sub Bump(ByVal v)", "    v = v + 1", "    Debug.Print v", "End Sub"],
            ["Public Sub Main()", "    Bump 41", "End Sub",
             "Public Sub Bump(ByVal v)", "    Dim localV As Variant", "    If IsObject(v) Then Set localV = v Else localV = v", "    localV = localV + 1", "    Debug.Print localV", "End Sub"],
            "Copy 'v' into a local variable 'localV', and use it in place of the parameter");

    [TestMethod]
    public async Task NoCopyIsOffered_ForAOneLineProcedure()
    {
        var finding = Assert.ContainsSingle(await ProgramAsync(new AssignedByValParameterAnalyzer(), "Public Sub Work(ByVal n As Long): n = 1: End Sub"));

        Assert.IsNull(finding.Fixes?.Edits.SingleOrDefault());
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

        CollectionAssert.AreEqual(new[] { 2, 4 }, Lines(findings), "not one read, passed, or read by another constant or a bound, in a procedure or in the module");
        AssertAre(findings, RDCoreDiagnosticId.ConstantNotUsed, DiagnosticSeverity.Information);
        StringAssert.Contains(findings[0].Message, "'Hidden'");
        StringAssert.Contains(findings[1].Message, "'Unused'");
    }

    [TestMethod]
    public async Task AConstantOfTheModule_ReadByADeclarationOfTheModule_IsUsed_AndAPublicOneIsNotCounted()
    {
        var findings = await ProgramAsync(new ConstantNotUsedAnalyzer(),
            "Option Explicit",
            "Private Const Size As Long = 3",
            "Private Items(Size) As Long",
            "Private Const Base As Long = 1",
            "Private Const Twice As Long = Base * 2",
            "Public Const Limit As Long = 4",
            "Public Sub Main()",
            "    Debug.Print Items(0), Twice",
            "End Sub");

        Assert.IsEmpty(findings, "read by the bounds of an array of the module, by the value of another constant; a Public one may be read by another module");
    }

    [TestMethod]
    public async Task AConstantNothingRefersTo_HasItsDeclarationRemoved()
    {
        string[] lines =
        [
            "Option Explicit",
            "Public Sub Main()",
            "    Const Unused As Long = 1",
            "    Const First As Long = 1, Second As Long = 2",
            "    Const Noted As Long = 1 ' kept for later",
            "    Debug.Print 1",
            "End Sub",
        ];
        var findings = await ProgramAsync(new ConstantNotUsedAnalyzer(), lines);

        CollectionAssert.AreEqual(new[] { 3, 4, 4, 5 }, Lines(findings));
        CollectionAssert.AreEqual(new[] { 3 }, Lines([.. findings.Where(finding => finding.Fixes?.Edits.SingleOrDefault()is not null)]),
            "not a constant declared with another, nor one with a comment on its line");
        var fix = findings[0].Fixes!.Edits.Single();
        Assert.AreEqual("Remove the declaration of 'Unused'", fix.Title);
        Assert.AreEqual(ModuleWorkspace.StandardModule("Program", [.. lines.Where((_, index) => index != 2)]), fix.Edits.ApplyTo(ModuleWorkspace.StandardModule("Program", lines)));
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

    // the module with every edit of the fix made, the last one first.
    private static string WithGetter(string module, EditFix fix)
    {
        var lines = module.Split("\r\n");
        int OffsetOf(Position position) => lines.Take(position.Line).Sum(line => line.Length + 2) + position.Character;
        foreach (var edit in fix.Edits.OrderByDescending(edit => OffsetOf(edit.Range.Start)))
        {
            module = module[..OffsetOf(edit.Range.Start)] + edit.NewText + module[OffsetOf(edit.Range.End)..];
        }

        return module;
    }

    [TestMethod]
    public async Task TheFix_AddsAGetThatReturnsWhatTheLetIsGiven()
    {
        string[] before = ["Option Explicit", "Private mWidth As Long", "Public Property Let Size(ByVal value As Long)", "    mWidth = value * 2", "End Property",
            "Public Property Get Width() As Long", "    Width = mWidth", "End Property"];
        var fix = Assert.ContainsSingle(await ClassAsync(new WriteOnlyPropertyAnalyzer(), "Shape", before)).Fixes?.Edits.SingleOrDefault();
        Assert.IsNotNull(fix);
        Assert.AreEqual("Add a Property Get 'Size' that returns the value the Let keeps in 'mSize'", fix.Title);

        var shape = ModuleWorkspace.ClassModule("Shape", before);
        var fixedShape = WithGetter(shape, fix);
        Assert.AreEqual(ModuleWorkspace.ClassModule("Shape",
            "Option Explicit", "Private mWidth As Long", "Private mSize As Long", "", "Public Property Let Size(ByVal value As Long)", "    mSize = value", "    mWidth = value * 2",
            "End Property", "", "Public Property Get Size() As Long", "    Size = mSize", "End Property",
            "Public Property Get Width() As Long", "    Width = mWidth", "End Property"), fixedShape);

        var program = ModuleWorkspace.StandardModule("Program", "Public Sub Main()", "    Dim s As New Shape", "    s.Size = 21", "    Debug.Print s.Width", "End Sub");
        CollectionAssert.AreEqual(await ModuleWorkspace.RunAsync([("Shape", shape)], program), await ModuleWorkspace.RunAsync([("Shape", fixedShape)], program), "the program prints the same");
        CollectionAssert.AreEqual(new[] { "21" },
            await ModuleWorkspace.RunAsync([("Shape", fixedShape)], ModuleWorkspace.StandardModule("Program", "Public Sub Main()", "    Dim s As New Shape", "    s.Size = 21", "    Debug.Print s.Size", "End Sub")),
            "the Get returns what the Let was given");
    }

    [TestMethod]
    public async Task NoGetIsAdded_ForAPropertyWithASet_OrWithNoSemantics()
    {
        var withSet = Assert.ContainsSingle(await ClassAsync(new WriteOnlyPropertyAnalyzer(), "Holder",
            "Public Property Let Item(ByVal value As Variant)", "End Property", "Public Property Set Item(ByVal value As Object)", "End Property"));
        var withoutSemantics = Assert.ContainsSingle(WithoutSemantics(new WriteOnlyPropertyAnalyzer(), "Public Property Let Size(ByVal value As Long)", "End Property"));

        Assert.IsNull(withSet.Fixes?.Edits.SingleOrDefault(), "the variable would keep the object alive");
        Assert.IsNull(withoutSemantics.Fixes?.Edits.SingleOrDefault(), "whether another module implements the class is not known");
    }

    [TestMethod]
    public async Task NoGetIsAdded_ToAClassThatAnotherClassImplements()
    {
        var shape = ModuleWorkspace.ClassModule("IShape", "Public Property Let Size(ByVal value As Long)", "End Property");
        var square = ModuleWorkspace.ClassModule("Square",
            "Implements IShape", "Private Property Let IShape_Size(ByVal value As Long)", "End Property");
        var payload = await ModuleWorkspace.SemanticsAsync([("IShape", shape), ("Square", square)], ModuleWorkspace.StandardModule("Program", "Public Sub Main()", "End Sub"));
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "IShape");

        var finding = Assert.ContainsSingle(Run(new WriteOnlyPropertyAnalyzer(), shape, model, new Uri(Path.Combine(ModuleWorkspace.Root, "IShape.cls"))));

        Assert.IsNull(finding.Fixes?.Edits.SingleOrDefault(), "a Get in the interface is a member that Square does not implement");
    }

    #endregion
}
