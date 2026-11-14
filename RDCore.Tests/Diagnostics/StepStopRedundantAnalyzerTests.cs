using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Platform.Protocol;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// The <c>Step</c> of a <c>For</c> loop (<c>RDC00116</c>, <c>RDC00313</c>), a Boolean assigned in both branches of an <c>If</c> (<c>RDC00209</c>), <c>Stop</c>
/// (<c>RDC00210</c>), a <c>ByRef</c> written (<c>RDC00314</c>) and a module option that states the default (<c>RDC00315</c>).
/// </summary>
[TestClass]
public sealed class StepStopRedundantAnalyzerTests
{
    private static string Module(string source) => $"Attribute VB_Name = \"Program\"\r\n{source.ReplaceLineEndings("\r\n")}\r\n";

    private static readonly Uri Program = new(Path.Combine(ModuleWorkspace.Root, "Program.bas"));

    private static ModuleSemanticsDto StandardModule(Uri uri) => new(uri, true, [], [], []);

    private static IOptions<DiagnosticsOptions> Options(ForStepConvention forStep = ForStepConvention.Implicit, ByRefConvention byRef = ByRefConvention.Explicit)
        => Microsoft.Extensions.Options.Options.Create(new DiagnosticsOptions { ForStep = forStep, ByRefModifier = byRef });

    private static AnalyzerFinding[] Run(IModuleAnalyzer analyzer, string source, ModuleSemanticsDto? semantics = null, Uri? uri = null)
    {
        uri ??= Program;
        var parsed = new ModuleParser().Parse(uri, source);
        Assert.IsEmpty(parsed.SyntaxErrors, string.Join("; ", parsed.SyntaxErrors.Select(error => error.Verbose)));
        return [.. analyzer.Analyze(new ModuleAnalysisContext(uri, parsed, semantics)).OrderBy(finding => finding.Range.Start)];
    }

    private static int[] Lines(AnalyzerFinding[] findings) => [.. findings.Select(finding => finding.Range.Start.Line)];

    private static void AssertAre(AnalyzerFinding[] findings, RDCoreDiagnosticId id)
        => Assert.IsTrue(findings.All(finding => finding is { Severity: DiagnosticSeverity.Information } && finding.Id == id), string.Join("; ", findings.Select(finding => finding.Message)));

    private static string Text(string source, AnalyzerFinding finding)
    {
        var line = source.ReplaceLineEndings("\n").Split('\n')[finding.Range.Start.Line];
        return line[finding.Range.Start.Character..finding.Range.End.Character];
    }

    #region RDC00313 Step 1, RDC00116 no Step

    private const string Loops = "Public Sub DoSomething()\n    Dim i As Long\n    For i = 1 To 10 Step 1\n    Next\n    For i = 1 To 10\n    Next\n    For i = 10 To 1 Step -1\n    Next\n    For i = 1 To 10 Step 2\n    Next\nEnd Sub";

    [TestMethod]
    public void StepOne_IsRedundant_UnderTheImplicitConvention()
    {
        var source = Module(Loops);
        var findings = Run(new ForStepAnalyzer(Options()), source);

        CollectionAssert.AreEqual(new[] { 3 }, Lines(findings));
        AssertAre(findings, RDCoreDiagnosticId.RedundantStepOne);
        Assert.AreEqual("1", Text(source, findings[0]), "the step");
    }

    [TestMethod]
    public void ALoopWithNoStep_IsReported_UnderTheExplicitConvention()
    {
        var source = Module(Loops);
        var findings = Run(new ForStepAnalyzer(Options(forStep: ForStepConvention.Explicit)), source);

        CollectionAssert.AreEqual(new[] { 5 }, Lines(findings));
        AssertAre(findings, RDCoreDiagnosticId.ImplicitForStep);
        Assert.AreEqual("For i = 1 To 10", Text(source, findings[0]), "the header, up to the end value");
    }

    [TestMethod]
    public void TheConventionsDefault_IsImplicit()
        => Assert.AreEqual(ForStepConvention.Implicit, new DiagnosticsOptions().ForStep);

    private static string Fixed(string program, EditFix fix)
    {
        var lines = program.Split("\r\n");
        int OffsetOf(Position position) => lines.Take(position.Line).Sum(line => line.Length + 2) + position.Character;
        foreach (var edit in fix.Edits.OrderByDescending(edit => OffsetOf(edit.Range.Start)))
        {
            program = program[..OffsetOf(edit.Range.Start)] + edit.NewText + program[OffsetOf(edit.Range.End)..];
        }

        return program;
    }

    // the fix of the one finding of the analyzer leaves the module as it is expected, and the program prints the same.
    private static async Task AssertFixedAsync(IModuleAnalyzer analyzer, string before, string after, string title, ModuleSemanticsDto? semantics = null)
    {
        var program = Module(before);
        var fix = Assert.ContainsSingle(Run(analyzer, program, semantics)).Fixes?.Edit;
        Assert.IsNotNull(fix);
        Assert.AreEqual(title, fix.Title);

        var fixedProgram = Fixed(program, fix);
        Assert.AreEqual(Module(after), fixedProgram);
        Assert.IsEmpty(Run(analyzer, fixedProgram, semantics), "the fix leaves nothing to report");
        CollectionAssert.AreEqual(await ModuleWorkspace.RunAsync([], program), await ModuleWorkspace.RunAsync([], fixedProgram), "the program prints the same");
    }

    [TestMethod]
    [DataRow("1 To 3 Step 1", "1 To 3", DisplayName = "a Step 1")]
    [DataRow("1 To 3 _\n        Step 1", "1 To 3", DisplayName = "a Step 1 on a continued line")]
    public async Task TheFix_RemovesStepOne(string before, string after)
        => await AssertFixedAsync(new ForStepAnalyzer(Options()),
            $"Public Sub Main()\n    Dim i As Long\n    For i = {before}\n        Debug.Print i\n    Next\n    Debug.Print i\nEnd Sub",
            $"Public Sub Main()\n    Dim i As Long\n    For i = {after}\n        Debug.Print i\n    Next\n    Debug.Print i\nEnd Sub",
            "Remove Step 1");

    [TestMethod]
    public async Task TheFix_WritesStepOne()
        => await AssertFixedAsync(new ForStepAnalyzer(Options(forStep: ForStepConvention.Explicit)),
            "Public Sub Main()\n    Dim i As Long\n    For i = 1 To 3\n        Debug.Print i\n    Next\n    Debug.Print i\nEnd Sub",
            "Public Sub Main()\n    Dim i As Long\n    For i = 1 To 3 Step 1\n        Debug.Print i\n    Next\n    Debug.Print i\nEnd Sub",
            "Write Step 1");

    #endregion

    #region RDC00209 Boolean assigned in If…Else

    [TestMethod]
    public void ABooleanLiteralAssignedInEachBranch_IsTheCondition()
    {
        var source = Module("Public Sub DoSomething(ByVal foo As Long)\n    Dim bar As Boolean\n    If foo > 0 Then\n        bar = True\n    Else\n        bar = False\n    End If\nEnd Sub");
        var findings = Run(new BooleanAssignedInIfElseAnalyzer(), source);

        CollectionAssert.AreEqual(new[] { 3 }, Lines(findings));
        AssertAre(findings, RDCoreDiagnosticId.BooleanAssignedInIfElse);
        Assert.AreEqual("If foo > 0", Text(source, findings[0]));
        StringAssert.Contains(findings[0].Message, "'bar'");
    }

    [TestMethod]
    public void TheNegation_AndAMemberOfAnObject_AreReported()
    {
        var source = Module("Public Done As Boolean\nPublic Sub DoSomething(ByVal foo As Long)\n    If foo > 0 Then\n        Let Program.Done = False\n    Else\n        Program.Done = True\n    End If\nEnd Sub");
        var findings = Run(new BooleanAssignedInIfElseAnalyzer(), source);

        Assert.HasCount(1, findings);
        StringAssert.Contains(findings[0].Message, "'Program.Done'");
    }

    [TestMethod]
    [DataRow("bar = True", "bar = True", DisplayName = "the same value")]
    [DataRow("bar = True", "baz = False", DisplayName = "two names")]
    [DataRow("bar = 1", "bar = 0", DisplayName = "no Boolean literal")]
    [DataRow("bar = True\n        Debug.Print 1", "bar = False", DisplayName = "a second statement")]
    [DataRow("Set obj = Nothing", "Set obj = Nothing", DisplayName = "a Set")]
    public void AnythingElse_IsNotReported(string then, string otherwise)
        => Assert.IsEmpty(Run(new BooleanAssignedInIfElseAnalyzer(),
            Module($"Public Sub DoSomething(ByVal foo As Long)\n    Dim bar As Boolean, baz As Boolean, obj As Object\n    If foo > 0 Then\n        {then}\n    Else\n        {otherwise}\n    End If\nEnd Sub")));

    [TestMethod]
    [DataRow("o Is Nothing", "True", "False", "found = o Is Nothing", "Assign the condition to 'found'", DisplayName = "Is")]
    [DataRow("o Is Nothing", "False", "True", "found = Not (o Is Nothing)", "Assign Not the condition to 'found'", DisplayName = "Is, negated")]
    [DataRow("TypeOf c Is Collection", "True", "False", "found = TypeOf c Is Collection", "Assign the condition to 'found'", DisplayName = "TypeOf, of an object: of Nothing it raises 91")]
    [DataRow("Not (o Is Nothing) And True", "True", "False", "found = Not (o Is Nothing) And True", "Assign the condition to 'found'", DisplayName = "Not, And")]
    public async Task TheFix_AssignsABooleanCondition_AndTheProgramPrintsTheSame(string condition, string then, string otherwise, string assignment, string title)
    {
        static string Source(string body) => $"Public Sub Main()\n    Dim o As Object\n    Dim c As Object\n    Set c = New Collection\n    Dim found As Long\n    Dim i As Long\n    For i = 1 To 2\n{body}\n        Debug.Print found\n        Set o = New Collection\n    Next\nEnd Sub";

        await AssertFixedAsync(new BooleanAssignedInIfElseAnalyzer(),
            Source($"        If {condition} Then\n            found = {then}\n        Else\n            found = {otherwise}\n        End If"),
            Source($"        {assignment}"),
            title);
    }

    [TestMethod]
    [DataRow("n > 0", DisplayName = "a comparison, which is Null when an operand is")]
    [DataRow("n", DisplayName = "a number, whose Not is not False")]
    [DataRow("IsNumeric(n)", DisplayName = "a call, which any procedure of that name can answer")]
    public void NoFixIsOffered_ForAConditionThatMayBeNullOrANumber(string condition)
        => Assert.IsNull(Assert.ContainsSingle(Run(new BooleanAssignedInIfElseAnalyzer(),
            Module($"Public Sub DoSomething(ByVal n As Variant)\n    Dim bar As Boolean\n    If {condition} Then\n        bar = True\n    Else\n        bar = False\n    End If\nEnd Sub"))).Fixes?.Edit);

    [TestMethod]
    public void NoFixIsOffered_WhereAComment_WouldGoWithTheBlock()
        => Assert.IsNull(Assert.ContainsSingle(Run(new BooleanAssignedInIfElseAnalyzer(),
            Module("Public Sub DoSomething(ByVal o As Object)\n    Dim bar As Boolean\n    If o Is Nothing Then ' no object\n        bar = True\n    Else\n        bar = False\n    End If\nEnd Sub"))).Fixes?.Edit);

    [TestMethod]
    public void AnElseIf_MakesItSomethingElse()
        => Assert.IsEmpty(Run(new BooleanAssignedInIfElseAnalyzer(),
            Module("Public Sub DoSomething(ByVal foo As Long)\n    Dim bar As Boolean\n    If foo > 0 Then\n        bar = True\n    ElseIf foo < 0 Then\n        bar = True\n    Else\n        bar = False\n    End If\nEnd Sub")));

    [TestMethod]
    public void AnIfOnOneLine_IsReported_AndOneWithNoElseIsNot()
    {
        var source = Module("Public Sub DoSomething(ByVal foo As Long)\n    Dim bar As Boolean\n    If foo > 0 Then bar = True Else bar = False\n    If foo > 0 Then bar = True\nEnd Sub");
        var findings = Run(new BooleanAssignedInIfElseAnalyzer(), source);

        CollectionAssert.AreEqual(new[] { 3 }, Lines(findings));
        Assert.AreEqual("If foo > 0", Text(source, findings[0]));
    }

    [TestMethod]
    [DataRow("True", "False", "found = o Is Nothing", DisplayName = "the condition")]
    [DataRow("False", "True", "found = Not (o Is Nothing)", DisplayName = "its negation")]
    public async Task TheFix_OfAnIfOnOneLine_AssignsTheCondition_AndTheProgramPrintsTheSame(string then, string otherwise, string assignment)
    {
        static string Source(string body) => $"Public Sub Main()\n    Dim o As Object\n    Dim found As Long\n    Dim i As Long\n    For i = 1 To 2\n{body}\n        Debug.Print found\n        Set o = New Collection\n    Next\nEnd Sub";

        await AssertFixedAsync(new BooleanAssignedInIfElseAnalyzer(),
            Source($"        If o Is Nothing Then found = {then} Else found = {otherwise}"),
            Source($"        {assignment}"),
            then == "True" ? "Assign the condition to 'found'" : "Assign Not the condition to 'found'");
    }

    #endregion

    #region RDC00210 Stop

    [TestMethod]
    public void Stop_IsReported()
    {
        var source = Module("Public Sub DoSomething()\n    Debug.Print 1\n    Stop\nEnd Sub");
        var findings = Run(new StopStatementAnalyzer(), source);

        CollectionAssert.AreEqual(new[] { 3 }, Lines(findings));
        AssertAre(findings, RDCoreDiagnosticId.StopStatement);
        Assert.AreEqual("Stop", Text(source, findings[0]));
    }

    [TestMethod]
    public void End_IsNoStop()
        => Assert.IsEmpty(Run(new StopStatementAnalyzer(), Module("Public Sub DoSomething()\n    End\nEnd Sub")));

    [TestMethod]
    public void TheFix_RemovesTheStop_AndSaysTheProgramNoLongerBreaks()
    {
        var source = Module("Public Sub DoSomething()\n    Debug.Print 1\n    Stop\n    Debug.Print 2\nEnd Sub");
        var fix = Assert.ContainsSingle(Run(new StopStatementAnalyzer(), source)).Fixes?.Edit;
        Assert.IsNotNull(fix);

        Assert.AreEqual("Remove the Stop: the program no longer breaks here", fix.Title);
        Assert.AreEqual(Module("Public Sub DoSomething()\n    Debug.Print 1\n    Debug.Print 2\nEnd Sub"), Fixed(source, fix));
    }

    [TestMethod]
    [DataRow("    Debug.Print 1: Stop", DisplayName = "after a statement")]
    [DataRow("    Stop: Debug.Print 1", DisplayName = "before a statement")]
    [DataRow("Here: Stop", DisplayName = "after a label")]
    [DataRow("    If True Then Stop", DisplayName = "in a single-line If")]
    [DataRow("    Stop ' look here", DisplayName = "before a comment")]
    public void NoRemovalIsOffered_ForAStopThatSharesItsLine(string line)
        => Assert.IsNull(Assert.ContainsSingle(Run(new StopStatementAnalyzer(), Module($"Public Sub DoSomething()\n{line}\nEnd Sub"))).Fixes?.Edit);

    #endregion

    #region RDC00314 ByRef written, RDC00104 under the other convention

    private const string ByRefs = "Declare PtrSafe Function Beep Lib \"kernel32\" (ByRef dwMs As Long) As Long\nPublic Sub DoSomething(ByRef foo As Long, bar As Long, ByVal baz As Long, ParamArray rest() As Variant)\nEnd Sub\nPublic Property Let Value(ByRef v As Long)\nEnd Property";

    [TestMethod]
    public void ByRefWritten_IsRedundant_UnderTheImplicitConvention()
    {
        var source = Module(ByRefs);
        var findings = Run(new RedundantByRefModifierAnalyzer(Options(byRef: ByRefConvention.Implicit)), source, StandardModule(Program));

        CollectionAssert.AreEqual(new[] { 2 }, Lines(findings), "neither a property's value, nor a Declare");
        AssertAre(findings, RDCoreDiagnosticId.RedundantByRefModifier);
        Assert.AreEqual("ByRef foo As Long", Text(source, findings[0]));
        StringAssert.Contains(findings[0].Message, "'foo'");
    }

    [TestMethod]
    public void ByRefWritten_IsTheConvention_ByDefault()
    {
        var source = Module(ByRefs);

        Assert.IsEmpty(Run(new RedundantByRefModifierAnalyzer(Options()), source, StandardModule(Program)));
        CollectionAssert.AreEqual(new[] { 2 }, Lines(Run(new ImplicitByRefModifierAnalyzer(Options()), source)), "bar states neither");
    }

    [TestMethod]
    public void NeitherWritten_IsTheConvention_UnderTheImplicitOne()
        => Assert.IsEmpty(Run(new ImplicitByRefModifierAnalyzer(Options(byRef: ByRefConvention.Implicit)), Module(ByRefs)));

    [TestMethod]
    public void WithNoSemantics_NothingIsSaid()
        => Assert.IsEmpty(Run(new RedundantByRefModifierAnalyzer(Options(byRef: ByRefConvention.Implicit)), Module(ByRefs)));

    [TestMethod]
    public async Task AnImplementationOrAnEventHandler_RepeatsTheDeclarationItMatches()
    {
        var shape = ModuleWorkspace.ClassModule("IShape", "Public Sub Move(ByRef dx As Long)", "End Sub");
        var button = ModuleWorkspace.ClassModule("Button", "Public Event Changed(ByRef value As Long)", "Public Sub Press()", "    RaiseEvent Changed(1)", "End Sub");
        var square = ModuleWorkspace.ClassModule("Square",
            "Implements IShape", "Private WithEvents Source As Button",
            "Private Sub IShape_Move(ByRef dx As Long)", "End Sub",
            "Private Sub Source_Changed(ByRef value As Long)", "End Sub",
            "Private Sub Grow(ByRef by As Long)", "End Sub");
        var payload = await ModuleWorkspace.SemanticsAsync([("IShape", shape), ("Button", button), ("Square", square)], "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nEnd Sub\r\n");
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Square");

        var findings = Run(new RedundantByRefModifierAnalyzer(Options(byRef: ByRefConvention.Implicit)), square, model, new Uri(Path.Combine(ModuleWorkspace.Root, "Square.cls")));

        Assert.HasCount(1, findings);
        StringAssert.Contains(findings[0].Message, "'by'");
    }

    [TestMethod]
    [DataRow("ByRef n As Long", "n As Long", DisplayName = "ByRef")]
    [DataRow("Optional ByRef n As Long = 1", "Optional n As Long = 1", DisplayName = "Optional ByRef")]
    public async Task TheFix_RemovesByRef_AndTheArgumentIsStillPassedByReference(string before, string after)
    {
        static string Source(string parameter) => $"Public Sub Main()\n    Dim x As Long\n    x = 41\n    Bump x\n    Debug.Print x\nEnd Sub\nPublic Sub Bump({parameter})\n    n = n + 1\nEnd Sub";

        await AssertFixedAsync(new RedundantByRefModifierAnalyzer(Options(byRef: ByRefConvention.Implicit)), Source(before), Source(after), "Remove ByRef from 'n'", StandardModule(Program));
    }

    [TestMethod]
    public void NoRemovalIsOffered_AfterOptional_WrittenWithMoreThanOneSpace()
        => Assert.IsNull(Assert.ContainsSingle(Run(new RedundantByRefModifierAnalyzer(Options(byRef: ByRefConvention.Implicit)),
            Module("Public Sub DoSomething(Optional  ByRef n As Long)\nEnd Sub"), StandardModule(Program))).Fixes?.Edit);

    #endregion

    #region RDC00315 a module option that states the default

    [TestMethod]
    public void TheDefaultOptions_AreRedundant()
    {
        var source = Module("Option Explicit\nOption Base 0\nOption Compare Binary\nPublic Sub DoSomething()\nEnd Sub");
        var findings = Run(new RedundantModuleOptionAnalyzer(), source);

        CollectionAssert.AreEqual(new[] { 2, 3 }, Lines(findings));
        AssertAre(findings, RDCoreDiagnosticId.RedundantModuleOption);
        StringAssert.StartsWith(findings[0].Message, "Option Base 0");
        StringAssert.StartsWith(findings[1].Message, "Option Compare Binary");
    }

    [TestMethod]
    public void TheOtherOptions_AreNot()
        => Assert.IsEmpty(Run(new RedundantModuleOptionAnalyzer(), Module("Option Explicit\nOption Base 1\nOption Compare Text\nOption Private Module\nPublic Sub DoSomething()\nEnd Sub")));

    [TestMethod]
    [DataRow("Option Base 0", DisplayName = "Option Base 0")]
    [DataRow("Option Compare Binary", DisplayName = "Option Compare Binary")]
    public async Task TheFix_RemovesTheOption_AndTheModuleHasItStill(string option)
        => await AssertFixedAsync(new RedundantModuleOptionAnalyzer(),
            $"Option Explicit\n{option}\nPublic Sub Main()\n    Dim a(2) As Long\n    Debug.Print LBound(a)\n    Debug.Print \"a\" = \"A\"\n    Debug.Print \"a\" < \"B\"\nEnd Sub",
            "Option Explicit\nPublic Sub Main()\n    Dim a(2) As Long\n    Debug.Print LBound(a)\n    Debug.Print \"a\" = \"A\"\n    Debug.Print \"a\" < \"B\"\nEnd Sub",
            $"Remove {option}");

    [TestMethod]
    public void NoRemovalIsOffered_ForAnOptionThatSharesItsLine()
        => Assert.IsNull(Assert.ContainsSingle(Run(new RedundantModuleOptionAnalyzer(), Module("Option Explicit: Option Base 0\nPublic Sub DoSomething()\nEnd Sub"))).Fixes?.Edit);

    #endregion
}
