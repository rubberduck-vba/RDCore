using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Platform.Protocol;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// What has nothing in it: a branch of an <c>If</c> (<c>RDC01003</c>), another block (<c>RDC01004</c>), a procedure (<c>RDC01009</c>), a module (<c>RDC01010</c>),
/// and the string literal <c>""</c> (<c>RDC00312</c>).
/// </summary>
[TestClass]
public sealed class EmptyAnalyzerTests
{
    private static string Module(string source) => $"Attribute VB_Name = \"Program\"\r\n{source.ReplaceLineEndings("\r\n")}\r\n";

    private static readonly Uri Program = new(Path.Combine(ModuleWorkspace.Root, "Program.bas"));

    // a standard module, as the host describes one: no class, no document.
    private static ModuleSemanticsDto StandardModule(Uri uri) => new(uri, true, [], [], []);

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

    #region RDC01003 empty If branch

    [TestMethod]
    public void AnIfBranchWithNoStatement_IsEmpty()
    {
        var source = Module("Public Sub DoSomething(ByVal foo As Boolean)\n    If foo Then\n    Else\n        Debug.Print 1\n    End If\nEnd Sub");
        var findings = Run(new EmptyIfBlockAnalyzer(), source);

        CollectionAssert.AreEqual(new[] { 2 }, Lines(findings));
        AssertAre(findings, RDCoreDiagnosticId.EmptyIfBlock);
        Assert.AreEqual("If foo", Text(source, findings[0]), "the header: the keyword and the condition");
    }

    [TestMethod]
    public void AnIfBranchWithAStatement_IsNotEmpty()
        => Assert.IsEmpty(Run(new EmptyIfBlockAnalyzer(), Module("Public Sub DoSomething(ByVal foo As Boolean)\n    If Not foo Then\n        Debug.Print 1\n    End If\nEnd Sub")));

    [TestMethod]
    public void CommentsLabelsAndDeclarations_AreNoStatement()
    {
        var findings = Run(new EmptyIfBlockAnalyzer(), Module(
            "Public Sub DoSomething(ByVal foo As Boolean)\n    If foo Then\n        ' a comment\n    End If\n    If Not foo Then\nHere:\n        Dim x As Long\n        Const K As Long = 1\n        Static s As Long\n    End If\nEnd Sub"));

        CollectionAssert.AreEqual(new[] { 2, 5 }, Lines(findings));
    }

    [TestMethod]
    public void AnElseIfBranchWithNoStatement_IsEmpty()
    {
        var source = Module("Public Sub DoSomething(ByVal n As Long)\n    If n = 1 Then\n        Debug.Print 1\n    ElseIf n = 2 Then\n    Else\n        Debug.Print 3\n    End If\nEnd Sub");
        var findings = Run(new EmptyIfBlockAnalyzer(), source);

        CollectionAssert.AreEqual(new[] { 4 }, Lines(findings));
        Assert.AreEqual("ElseIf n = 2", Text(source, findings[0]));
        StringAssert.Contains(findings[0].Message, "ElseIf");
    }

    [TestMethod]
    public void ASingleLineIfWithNothingAfterThen_IsEmpty()
    {
        var source = Module("Public Sub DoSomething(ByVal foo As Boolean)\n    If foo Then Else Debug.Print 1\n    If foo Then Debug.Print 2\nEnd Sub");
        var findings = Run(new EmptyIfBlockAnalyzer(), source);

        CollectionAssert.AreEqual(new[] { 2 }, Lines(findings));
        Assert.AreEqual("If foo", Text(source, findings[0]));
    }

    [TestMethod]
    public void ABranchWithAStatementUnderADirective_IsNotEmpty()
        => Assert.IsEmpty(Run(new EmptyIfBlockAnalyzer(), Module(
            "Public Sub DoSomething(ByVal foo As Boolean)\n    If foo Then\n#If DEBUG Then\n        Debug.Print 1\n#End If\n    End If\nEnd Sub")));

    [TestMethod]
    [DataRow("n > 0", DisplayName = "a comparison of a Long")]
    [DataRow("Not (n > 0 And d <= 1.5) Or flag", DisplayName = "Not, And and Or of comparisons")]
    [DataRow("o Is Nothing", DisplayName = "an object tested with Is")]
    [DataRow("n", DisplayName = "a Long")]
    [DataRow("d", DisplayName = "a Double")]
    [DataRow("h = 1", DisplayName = "a type hint")]
    public async Task TheFix_RemovesAnEmptyIf_WhoseConditionNeitherCallsNorRaises(string condition)
    {
        var program = Module($"Public Sub Main()\n    Dim n As Long, d As Double, h&\n    Dim o As Collection\n    Dim flag As Boolean\n    Debug.Print 1\n    If {condition} Then\n    End If\n    Debug.Print 2\nEnd Sub");
        var finding = Assert.ContainsSingle(Run(new EmptyIfBlockAnalyzer(), program));
        Assert.AreEqual("Remove the empty If", finding.Fixes?.Edit?.Title);

        var fixedProgram = Fixed(program, finding.Fixes!.Edit!);
        Assert.AreEqual(Module("Public Sub Main()\n    Dim n As Long, d As Double, h&\n    Dim o As Collection\n    Dim flag As Boolean\n    Debug.Print 1\n    Debug.Print 2\nEnd Sub"), fixedProgram);
        CollectionAssert.AreEqual(await ModuleWorkspace.RunAsync([], program), await ModuleWorkspace.RunAsync([], fixedProgram), "the program prints the same");
    }

    [TestMethod]
    [DataRow("Done()", DisplayName = "a call")]
    [DataRow("Ready", DisplayName = "a name that is no variable of the procedure, maybe a procedure")]
    [DataRow("s = \"x\"", DisplayName = "a String")]
    [DataRow("v", DisplayName = "a Variant, which can hold a String")]
    [DataRow("d And 1", DisplayName = "And of a Double, which can overflow")]
    [DataRow("n + 1 > 0", DisplayName = "arithmetic, which can overflow")]
    [DataRow("a(1) > 0", DisplayName = "an element of an array, which can be out of range")]
    [DataRow("o.Count > 0", DisplayName = "a member of an object")]
    public void NoRemovalIsOffered_ForAConditionThatMayCallOrRaise(string condition)
    {
        var findings = Run(new EmptyIfBlockAnalyzer(), Module(
            $"Public Sub DoSomething(ByVal s As String, ByVal v As Variant)\n    Dim n As Long, d As Double, a(1) As Long\n    Dim o As Collection\n    If {condition} Then\n    End If\nEnd Sub"));

        Assert.IsNull(Assert.ContainsSingle(findings).Fixes?.Edit);
    }

    [TestMethod]
    [DataRow("    If n > 0 Then\n        ' later\n    End If", DisplayName = "a comment in it")]
    [DataRow("    If n > 0 Then\n    Else\n        Debug.Print n\n    End If", DisplayName = "an Else")]
    [DataRow("    If n > 0 Then\n    ElseIf n < 0 Then\n        Debug.Print n\n    End If", DisplayName = "an ElseIf")]
    [DataRow("    If n > 0 Then\nHere:\n    End If", DisplayName = "a label in it")]
    [DataRow("    Debug.Print n: If n > 0 Then\n    End If", DisplayName = "a statement before it on its line")]
    public void NoRemovalIsOffered_ForAnIfThatHoldsOrSharesSomething(string block)
    {
        var findings = Run(new EmptyIfBlockAnalyzer(), Module($"Public Sub DoSomething()\n    Dim n As Long\n{block}\nEnd Sub"));

        Assert.IsTrue(findings.All(finding => finding.Fixes?.Edit is null));
    }

    [TestMethod]
    public void ABranchWithOnlyAReDim_IsNotEmpty()
        => Assert.IsEmpty(Run(new EmptyIfBlockAnalyzer(), Module(
            "Public Sub DoSomething(ByVal foo As Boolean)\n    Dim a() As Long\n    If foo Then\n        ReDim a(1)\n    End If\nEnd Sub")));

    #endregion

    #region RDC01004 empty block

    [TestMethod]
    public void AnElseWithNoStatement_IsEmpty()
    {
        var source = Module("Public Sub DoSomething(ByVal foo As Boolean)\n    If foo Then\n        Debug.Print 1\n    Else\n    End If\nEnd Sub");
        var findings = Run(new EmptyCodeBlockAnalyzer(), source);

        CollectionAssert.AreEqual(new[] { 4 }, Lines(findings));
        AssertAre(findings, RDCoreDiagnosticId.EmptyCodeBlock);
        Assert.AreEqual("Else", Text(source, findings[0]));
        StringAssert.Contains(findings[0].Message, "Else");
    }

    [TestMethod]
    public void AnIfWithNoElse_HasNoEmptyElse()
        => Assert.IsEmpty(Run(new EmptyCodeBlockAnalyzer(), Module("Public Sub DoSomething(ByVal foo As Boolean)\n    If foo Then\n        Debug.Print 1\n    End If\nEnd Sub")));

    private static string Fixed(string program, EditFix fix)
    {
        var edit = Assert.ContainsSingle(fix.Edits);
        var lines = program.Split("\r\n");
        int OffsetOf(Position position) => lines.Take(position.Line).Sum(line => line.Length + 2) + position.Character;
        return program[..OffsetOf(edit.Range.Start)] + edit.NewText + program[OffsetOf(edit.Range.End)..];
    }

    [TestMethod]
    [DataRow("    Else\n", DisplayName = "an Else line")]
    [DataRow("    Else\n\n", DisplayName = "an Else line and a blank line")]
    public async Task TheFix_RemovesAnEmptyElse_AndTheProgramPrintsTheSame(string emptyElse)
    {
        var program = Module($"Public Sub Main()\n    Dim n As Long\n    For n = 1 To 2\n        If n = 1 Then\n            Debug.Print \"one\"\n    {emptyElse}        End If\n        Debug.Print n\n    Next\nEnd Sub");
        var finding = Assert.ContainsSingle(Run(new EmptyCodeBlockAnalyzer(), program));
        Assert.IsNotNull(finding.Fixes?.Edit);
        Assert.AreEqual("Remove the empty Else", finding.Fixes.Edit.Title);

        var fixedProgram = Fixed(program, finding.Fixes.Edit);
        Assert.AreEqual(Module("Public Sub Main()\n    Dim n As Long\n    For n = 1 To 2\n        If n = 1 Then\n            Debug.Print \"one\"\n        End If\n        Debug.Print n\n    Next\nEnd Sub"), fixedProgram);
        Assert.IsEmpty(Run(new EmptyCodeBlockAnalyzer(), fixedProgram));
        CollectionAssert.AreEqual(await ModuleWorkspace.RunAsync([], program), await ModuleWorkspace.RunAsync([], fixedProgram), "the program prints the same");
    }

    [TestMethod]
    public async Task TheFix_KeepsAnElseIfBeforeTheEmptyElse()
    {
        var program = Module("Public Sub Main()\n    Dim n As Long\n    For n = 1 To 3\n        If n = 1 Then\n            Debug.Print \"one\"\n        ElseIf n = 2 Then\n            Debug.Print \"two\"\n        Else\n        End If\n    Next\nEnd Sub");
        var fixedProgram = Fixed(program, Assert.ContainsSingle(Run(new EmptyCodeBlockAnalyzer(), program)).Fixes!.Edit!);

        Assert.AreEqual(Module("Public Sub Main()\n    Dim n As Long\n    For n = 1 To 3\n        If n = 1 Then\n            Debug.Print \"one\"\n        ElseIf n = 2 Then\n            Debug.Print \"two\"\n        End If\n    Next\nEnd Sub"), fixedProgram);
        CollectionAssert.AreEqual(await ModuleWorkspace.RunAsync([], program), await ModuleWorkspace.RunAsync([], fixedProgram), "the program prints the same");
    }

    [TestMethod]
    [DataRow("    Else\n        ' nothing to do\n", DisplayName = "a comment in the Else")]
    [DataRow("    Else ' nothing to do\n", DisplayName = "a comment on the Else line")]
    [DataRow("    Else\nHere:\n", DisplayName = "a line label in the Else")]
    [DataRow("    Else\n        Dim x As Long\n", DisplayName = "a declaration in the Else")]
    [DataRow("    Else\n#If DEBUG Then\n#End If\n", DisplayName = "a directive in the Else")]
    public void NoRemovalIsOffered_ForAnElseThatHoldsSomething(string emptyElse)
    {
        var finding = Assert.ContainsSingle(Run(new EmptyCodeBlockAnalyzer(), Module($"Public Sub DoSomething(ByVal foo As Boolean)\n    If foo Then\n        Debug.Print 1\n{emptyElse}    End If\nEnd Sub")));

        Assert.IsNull(finding.Fixes?.Edit);
    }

    [TestMethod]
    [DataRow("    If foo Then\n        Debug.Print 1: Else\n    End If", DisplayName = "a statement before the Else on its line")]
    [DataRow("    If foo Then\n        Debug.Print 1\n    Else: End If", DisplayName = "the End If on the Else line")]
    public void NoRemovalIsOffered_ForAnElseThatSharesItsLine(string block)
    {
        var findings = Run(new EmptyCodeBlockAnalyzer(), Module($"Public Sub DoSomething(ByVal foo As Boolean)\n{block}\nEnd Sub"));

        Assert.IsTrue(findings.All(finding => finding.Fixes?.Edit is null));
    }

    [TestMethod]
    public void NoRemovalIsOffered_ForAnEmptyLoopOrCase()
    {
        var findings = Run(new EmptyCodeBlockAnalyzer(), Module(
            "Public Sub DoSomething(ByVal foo As Long)\n    Do While foo > 0\n    Loop\n    Select Case foo\n        Case 0\n        Case Else\n            Debug.Print foo\n    End Select\nEnd Sub"));

        Assert.HasCount(2, findings);
        Assert.IsTrue(findings.All(finding => finding.Fixes?.Edit is null));
    }

    [TestMethod]
    public void ACaseWithNoStatement_IsEmpty_AndAnEmptyCaseElseIsNot()
    {
        var source = Module(
            "Public Sub DoSomething(ByVal foo As Long)\n    Select Case foo\n        Case 0 ' empty block\n        Case 1, 2 To 3\n        Case Is > 3\n            Debug.Print foo\n        Case Else\n    End Select\nEnd Sub");
        var findings = Run(new EmptyCodeBlockAnalyzer(), source);

        CollectionAssert.AreEqual(new[] { 3, 4 }, Lines(findings));
        AssertAre(findings, RDCoreDiagnosticId.EmptyCodeBlock);
        Assert.AreEqual("Case 0", Text(source, findings[0]));
        Assert.AreEqual("Case 1, 2 To 3", Text(source, findings[1]));
    }

    [TestMethod]
    [DataRow("Do\n        ' no executable statement...\n    Loop While foo < 100", "Do")]
    [DataRow("Do\n    Loop Until foo < 100", "Do")]
    [DataRow("Do\n    Loop", "Do")]
    [DataRow("Do While foo < 100\n    Loop", "Do While foo < 100")]
    [DataRow("Do Until foo < 100\n    Loop", "Do Until foo < 100")]
    [DataRow("While foo < 100\n        'no executable statements... would be an infinite loop if entered\n    Wend", "While foo < 100")]
    [DataRow("For i = 0 To foo\n        ' no executable statement...\n    Next", "For i = 0 To foo")]
    [DataRow("For i = 0 To foo Step 2\n    Next i", "For i = 0 To foo Step 2")]
    [DataRow("For Each v In Array(1, 2)\n        ' no executable statement...\n    Next", "For Each v In Array(1, 2)")]
    public void ALoopWithNoStatement_IsEmpty(string loop, string header)
    {
        var source = Module($"Public Sub DoSomething(ByVal foo As Long)\n    Dim i As Long\n    Dim v As Variant\n    {loop}\nEnd Sub");
        var findings = Run(new EmptyCodeBlockAnalyzer(), source);

        CollectionAssert.AreEqual(new[] { 4 }, Lines(findings));
        AssertAre(findings, RDCoreDiagnosticId.EmptyCodeBlock);
        Assert.AreEqual(header, Text(source, findings[0]));
    }

    [TestMethod]
    [DataRow("Do\n        Debug.Print foo\n    Loop While foo < 100")]
    [DataRow("While foo < 100\n        foo = foo + 1\n    Wend")]
    [DataRow("For i = 0 To foo\n        Debug.Print i\n    Next")]
    [DataRow("For Each v In Array(1, 2)\n        Debug.Print v\n    Next")]
    public void ALoopWithAStatement_IsNotEmpty(string loop)
        => Assert.IsEmpty(Run(new EmptyCodeBlockAnalyzer(), Module($"Public Sub DoSomething(ByVal foo As Long)\n    Dim i As Long\n    Dim v As Variant\n    {loop}\nEnd Sub")));

    [TestMethod]
    public void AnEmptyBlockInsideAnother_IsFound()
    {
        var findings = Run(new EmptyCodeBlockAnalyzer(), Module(
            "Public Sub DoSomething(ByVal foo As Long)\n    Dim i As Long\n    Do While foo > 0\n        For i = 1 To foo\n        Next\n        foo = foo - 1\n    Loop\nEnd Sub"));

        CollectionAssert.AreEqual(new[] { 4 }, Lines(findings), "the For, not the Do that holds it");
    }

    #endregion

    #region RDC01009 empty procedure

    [TestMethod]
    public void AProcedureWithNoStatement_IsEmpty()
    {
        var findings = Run(new EmptyProcedureAnalyzer(), Module(
            "Sub Foo()\n    ' ...\nEnd Sub\nSub Bar()\n    MsgBox \"?\"\nEnd Sub\nFunction Baz() As Long\n    Dim x As Long\nEnd Function\nProperty Get Qux() As Long\nEnd Property"), StandardModule(Program));

        CollectionAssert.AreEqual(new[] { 1, 7, 10 }, Lines(findings));
        AssertAre(findings, RDCoreDiagnosticId.EmptyProcedure);
        StringAssert.Contains(findings[0].Message, "'Foo'");
        Assert.AreEqual("Foo".Length, findings[0].Range.End.Character - findings[0].Range.Start.Character, "the name");
    }

    [TestMethod]
    public void ADeclareStatement_HasNoBody()
        => Assert.IsEmpty(Run(new EmptyProcedureAnalyzer(), Module("Private Declare PtrSafe Sub Sleep Lib \"kernel32\" (ByVal ms As Long)"), StandardModule(Program)));

    [TestMethod]
    public void WithNoWordFromTheHost_WhetherTheModuleIsAClassIsNotKnown()
        => Assert.IsEmpty(Run(new EmptyProcedureAnalyzer(), Module("Sub Foo()\nEnd Sub")));

    private static async Task<AnalyzerFinding[]> ClassAsync(string name, params string[] lines)
    {
        var shape = ModuleWorkspace.ClassModule("IShape", "Public Sub Draw()", "End Sub", "Public Function Area() As Long", "End Function");
        var button = ModuleWorkspace.ClassModule("Button", "Public Event Click()", "Public Sub Press()", "    RaiseEvent Click", "End Sub");
        var source = ModuleWorkspace.ClassModule(name, lines);
        var classes = name == "IShape" ? new[] { ("IShape", shape), ("Button", button) } : [("IShape", shape), ("Button", button), (name, source)];
        var payload = await ModuleWorkspace.SemanticsAsync(classes, "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nEnd Sub\r\n");
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == name);

        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, $"{name}.cls"));
        return Run(new EmptyProcedureAnalyzer(), name == "IShape" ? shape : source, model, uri);
    }

    [TestMethod]
    public async Task APublicMemberOfAClass_MayBeAMemberOfAnInterface()
        => Assert.IsEmpty(await ClassAsync("IShape"));

    [TestMethod]
    public async Task APrivateOrFriendMemberOfAClass_IsInNoInterface()
    {
        var findings = await ClassAsync("Shape", "Private Sub Class_Initialize()", "End Sub", "Friend Sub Reset()", "End Sub", "Public Sub Draw()", "End Sub");

        CollectionAssert.AreEqual(new[] { "Class_Initialize", "Reset" }, findings.Select(finding => finding.Message.Split('\'')[1]).ToArray());
    }

    [TestMethod]
    public async Task AnImplementationOrAnEventHandler_IsNoMemberOfAnInterface()
    {
        var findings = await ClassAsync("Square",
            "Implements IShape", "Private WithEvents Source As Button",
            "Public Sub IShape_Draw()", "End Sub",
            "Private Function IShape_Area() As Long", "    IShape_Area = 4", "End Function",
            "Public Sub Source_Click()", "End Sub");

        CollectionAssert.AreEqual(new[] { "IShape_Draw", "Source_Click" }, findings.Select(finding => finding.Message.Split('\'')[1]).ToArray());
    }

    #endregion

    #region RDC01010 empty module

    [TestMethod]
    public void AModuleOfOptionsAndComments_DeclaresNothing()
    {
        var findings = Run(new EmptyModuleAnalyzer(), Module("Option Explicit\nOption Compare Text\nDefLng A-Z\n' a comment"), StandardModule(Program));

        Assert.HasCount(1, findings);
        AssertAre(findings, RDCoreDiagnosticId.EmptyModule);
        StringAssert.Contains(findings[0].Message, "'Program'");
        Assert.AreEqual(0, findings[0].Range.Start.Line, "the module: its first line");
    }

    [TestMethod]
    [DataRow("Private x As Long")]
    [DataRow("Public Const K As Long = 1")]
    [DataRow("Public Enum Colour\n    Red\nEnd Enum")]
    [DataRow("Public Type Point\n    X As Long\nEnd Type")]
    [DataRow("Private Declare PtrSafe Sub Sleep Lib \"kernel32\" (ByVal ms As Long)")]
    [DataRow("Public Sub Work()\nEnd Sub")]
    public void AModuleThatDeclaresAnything_IsNotEmpty(string declaration)
        => Assert.IsEmpty(Run(new EmptyModuleAnalyzer(), Module($"Option Explicit\n{declaration}"), StandardModule(Program)));

    [TestMethod]
    public void AClassModuleThatImplementsAnInterface_IsNotEmpty()
    {
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Shape.cls"));
        Assert.IsEmpty(Run(new EmptyModuleAnalyzer(), ModuleWorkspace.ClassModule("Shape", "Implements IShape"), StandardModule(uri) with { Class = SDK.Semantics.Flags.ClassModuleSemanticFlags.None }, uri));
    }

    [TestMethod]
    public void AnEmptyClassModule_DeclaresNothing()
    {
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Shape.cls"));
        Assert.HasCount(1, Run(new EmptyModuleAnalyzer(), ModuleWorkspace.ClassModule("Shape", "Option Explicit"), StandardModule(uri) with { Class = SDK.Semantics.Flags.ClassModuleSemanticFlags.None }, uri));
    }

    [TestMethod]
    public void AForm_IsItsDesignerAsWellAsItsCode()
    {
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Form1.frm"));
        var form = "VERSION 5.00\r\nBegin {C62A69F0-16DC-11CE-9E98-00AA00574A4F} Form1 \r\n   Caption         =   \"Form1\"\r\nEnd\r\n"
            + "Attribute VB_Name = \"Form1\"\r\nAttribute VB_PredeclaredId = True\r\nOption Explicit\r\n";

        Assert.IsEmpty(Run(new EmptyModuleAnalyzer(), form, StandardModule(uri) with { Class = SDK.Semantics.Flags.ClassModuleSemanticFlags.DefaultInstance }, uri));
    }

    [TestMethod]
    public void ADocumentModule_IsTheCodeOfItsDocument()
    {
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Sheet1.doccls"));
        var document = StandardModule(uri) with { Class = SDK.Semantics.Flags.ClassModuleSemanticFlags.None, Document = new SDK.Semantics.DocumentModuleFact(null, null, []) };

        Assert.IsEmpty(Run(new EmptyModuleAnalyzer(), ModuleWorkspace.ClassModule("Sheet1", "Option Explicit"), document, uri));
    }

    [TestMethod]
    public void WithNoWordFromTheHost_WhetherTheModuleIsADocumentIsNotKnown()
        => Assert.IsEmpty(Run(new EmptyModuleAnalyzer(), Module("Option Explicit")));

    #endregion

    #region RDC00312 empty string literal

    [TestMethod]
    public void AnEmptyStringLiteral_IsWhatVbNullStringSays()
    {
        var source = Module("Private Const K As String = \"\"\nPublic Sub DoSomething(ByVal foo As String, Optional ByVal bar As String = \"\")\n    If foo = \"\" Then\n        Debug.Print \"\"\"\"; \"a\"; \" \"\n    End If\nEnd Sub");
        var findings = Run(new EmptyStringLiteralAnalyzer(), source);

        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, Lines(findings), "a constant's value, a default value, an operand; a quote (\"\"\"\") and other strings are not empty");
        Assert.IsTrue(findings.All(finding => finding is { Severity: DiagnosticSeverity.Information, Id: RDCoreDiagnosticId.EmptyStringLiteral }));
        Assert.AreEqual("\"\"", Text(source, findings[2]));
    }

    [TestMethod]
    [DataRow("If s = \"\" Then Debug.Print \"empty\"", "If s = vbNullString Then Debug.Print \"empty\"", DisplayName = "=")]
    [DataRow("If \"\" <> s Then Debug.Print \"not empty\"", "If vbNullString <> s Then Debug.Print \"not empty\"", DisplayName = "<>, on the left")]
    [DataRow("If s Like \"\" Then Debug.Print \"like\"", "If s Like vbNullString Then Debug.Print \"like\"", DisplayName = "Like")]
    [DataRow("Select Case s\n    Case \"\"\n        Debug.Print \"case\"\n    End Select", "Select Case s\n    Case vbNullString\n        Debug.Print \"case\"\n    End Select", DisplayName = "a Case value")]
    [DataRow("Select Case s\n    Case \"\" To \"a\"\n        Debug.Print \"range\"\n    End Select", "Select Case s\n    Case vbNullString To \"a\"\n        Debug.Print \"range\"\n    End Select", DisplayName = "a Case range")]
    public async Task TheFix_ReplacesAComparedLiteral_AndTheProgramPrintsTheSame(string before, string after)
    {
        string Program(string line) => Module($"Public Sub Main()\n    Dim s As String\n    {line}\n    s = \"x\"\n    {line}\nEnd Sub");
        var program = Program(before);
        var findings = Run(new EmptyStringLiteralAnalyzer(), program);
        Assert.HasCount(2, findings);

        var fixedProgram = program;
        foreach (var finding in findings.OrderByDescending(finding => finding.Range.Start))
        {
            Assert.AreEqual("Replace \"\" with vbNullString", finding.Fixes?.Edit?.Title);
            fixedProgram = Fixed(fixedProgram, finding.Fixes!.Edit!);
        }

        Assert.AreEqual(Program(after), fixedProgram);
        CollectionAssert.AreEqual(await ModuleWorkspace.RunAsync([], program), await ModuleWorkspace.RunAsync([], fixedProgram), "the program prints the same");
    }

    [TestMethod]
    [DataRow("    s = \"\"", DisplayName = "an assignment")]
    [DataRow("    Debug.Print StrPtr(\"\")", DisplayName = "an argument")]
    [DataRow("    Debug.Print \"\" & s", DisplayName = "a concatenation")]
    public void NoReplacementIsOffered_WhereTheStringMayReachItsAddress(string line)
    {
        var finding = Assert.ContainsSingle(Run(new EmptyStringLiteralAnalyzer(), Module($"Public Sub DoSomething()\n    Dim s As String\n{line}\nEnd Sub")));

        Assert.IsNull(finding.Fixes?.Edit);
    }

    [TestMethod]
    public void VbNullString_IsNoLiteral()
        => Assert.IsEmpty(Run(new EmptyStringLiteralAnalyzer(), Module("Public Sub DoSomething(ByVal foo As String)\n    If foo = vbNullString Then\n        Debug.Print 1\n    End If\nEnd Sub")));

    #endregion
}
