using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Source;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// The analyzers that read how a module is written: each is given the source of a module, and says what is worth saying about it.
/// </summary>
[TestClass]
public sealed class SyntaxTreeAnalyzerTests
{
    private static AnalyzerFinding[] Run(IModuleAnalyzer analyzer, string source)
    {
        var uri = TestUri.TestModuleUri();
        var parseResult = new ModuleParser().Parse(uri, source);
        Assert.IsEmpty(parseResult.SyntaxErrors, $"the source is valid: {string.Join("; ", parseResult.SyntaxErrors.Select(error => $"{error.Location.Range.Start}: {error.Description} {error.Verbose}"))}");
        return [.. analyzer.Analyze(new ModuleAnalysisContext(uri, parseResult, null)).OrderBy(finding => finding.Range.Start)];
    }

    private static int[] Lines(AnalyzerFinding[] findings) => [.. findings.Select(finding => finding.Range.Start.Line)];

    // declarations in the blocks of every kind of structured statement: the lines that say `MARK` are the ones an analyzer is expected to find.
    private const string Nested = """
        Public Sub Work(ByVal c As Boolean, ByVal n As Long, ByVal target As Object)
            If c Then
                Dim a As Integer 'MARK
            ElseIf Not c Then
                Dim b As Integer 'MARK
            Else
                Dim d As Integer 'MARK
            End If
            For n = 1 To 2
                Dim e As Integer 'MARK
            Next
            Do While c
                Dim f As Integer 'MARK
            Loop
            While c
                Dim g As Integer 'MARK
            Wend
            Select Case n
            Case 1
                Dim h As Integer 'MARK
            Case Else
                Dim j As Integer 'MARK
            End Select
            With target
                Dim k As Integer 'MARK
            End With
        End Sub
        """;

    private static int[] Marked(string source)
        => [.. source.Split('\n').Select((line, index) => (line, index)).Where(entry => entry.line.Contains("'MARK", StringComparison.Ordinal)).Select(entry => entry.index)];

    [TestMethod]
    public void ADeclarationInTheBlockOfAStructuredStatement_IsAnalyzedLikeAnyOther()
        => CollectionAssert.AreEqual(Marked(Nested), Lines(Run(new IntegerDataTypeAnalyzer(), Nested)));

    [TestMethod]
    public void AVariableWithoutATypeInABlock_IsAVariantToo()
        => CollectionAssert.AreEqual(
            Marked(Nested), Lines(Run(new ImplicitVariantDeclarationAnalyzer(), Nested.Replace(" As Integer", string.Empty))));

    [TestMethod]
    public void SeveralDeclarationsOfOneStatementInABlock_AreOneFinding_AndTheStatementsOfTwoBlocksAreNotOne()
    {
        var source = Nested.Replace(" As Integer", ", other As Long");

        CollectionAssert.AreEqual(Marked(Nested), Lines(Run(new MultipleDeclarationsAnalyzer(), source)));
    }

    [TestMethod]
    public void AModuleThatDidNotParse_HasNothingToSayAboutHowItIsWritten()
    {
        var uri = TestUri.TestModuleUri();
        var failed = SDK.Model.AST.ModuleParseResult.Failed(new SourceLocation(uri, default), "no");

        Assert.IsEmpty(new IntegerDataTypeAnalyzer().Analyze(new ModuleAnalysisContext(uri, failed, null)));
    }

    #region RDC00102 Option Base

    [TestMethod]
    public void OptionBase1_IsAHint_AtTheDirective()
    {
        var finding = Run(new OptionBaseAnalyzer(), "Option Explicit\nOption Base 1\n").Single();

        Assert.AreEqual(RDCoreDiagnosticId.ImplicitNonDefaultArrayBase, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Hint, finding.Severity);
        Assert.AreEqual(1, finding.Range.Start.Line);
    }

    [TestMethod]
    [DataRow("Option Base 0\n")]
    [DataRow("Option Explicit\n")]
    public void TheDefaultBase_AndNoBase_AreNotReported(string source)
        => Assert.IsEmpty(Run(new OptionBaseAnalyzer(), source));

    #endregion

    #region RDC00103 Def<Type>

    [TestMethod]
    public void EveryDefTypeDirective_IsAHint_AndNamesTheDirectiveAsWritten()
    {
        var findings = Run(new TypeDefDirectiveAnalyzer(), "DefInt A-C\nDefStr S\nPublic Sub Work()\nEnd Sub\n");

        CollectionAssert.AreEqual(new[] { 0, 1 }, Lines(findings));
        Assert.IsTrue(findings.All(finding => finding is { Id: RDCoreDiagnosticId.ImplicitTypeDeclarationsEnabled, Severity: DiagnosticSeverity.Hint }));
        StringAssert.Contains(findings[0].Message, "DefInt");
        StringAssert.Contains(findings[1].Message, "DefStr");
    }

    #endregion

    #region RDC00104 implicit ByRef

    [TestMethod]
    public void AParameterThatStatesNoMechanism_IsPassedByReference_AndSuggestsSayingSo()
    {
        var findings = Run(new ImplicitByRefModifierAnalyzer(), """
            Public Sub Work(a, ByVal b As Long, ByRef c As Long, Optional d, e As Integer, ParamArray rest())
            End Sub
            """);

        CollectionAssert.AreEqual(new[] { "a", "d", "e" }, findings.Select(finding => finding.Message.Split('\'')[1]).ToArray());
        Assert.IsTrue(findings.All(finding => finding is { Id: RDCoreDiagnosticId.ImplicitByRefModifier, Severity: DiagnosticSeverity.Information }));
    }

    [TestMethod]
    public void TheValueParameterOfAPropertyLetOrSet_IsPassedByValue_AndIsNotReported()
        => Assert.IsEmpty(Run(new ImplicitByRefModifierAnalyzer(), "Property Let Item(ByVal i As Long, v As Long)\nEnd Property\nProperty Set Obj(v As Object)\nEnd Property\n")
            .Where(finding => !finding.Message.Contains("'i'")));

    [TestMethod]
    public void TheParametersOfAnEventAndOfADeclare_AreReportedLikeAnyOther()
    {
        var findings = Run(new ImplicitByRefModifierAnalyzer(), "Event Fired(a)\nDeclare Function Beep Lib \"kernel32\" (dwMs As Long) As Long\n");

        CollectionAssert.AreEqual(new[] { 0, 1 }, Lines(findings));
    }

    #endregion

    #region RDC00105 implicit Public

    [TestMethod]
    public void AMemberThatStatesNoAccess_IsPublic_AndIsReportedAtItsName()
    {
        var findings = Run(new ImplicitPublicMemberAnalyzer(), """
            Dim field As Long
            Enum Colors
                Red
            End Enum
            Type Pt
                X As Long
            End Type
            Event Fired()
            Declare Function Beep Lib "kernel32" () As Long
            Sub A()
            End Sub
            Private Sub B()
            End Sub
            Public Function C() As Long
            End Function
            Function D() As Long
            End Function
            Property Get E() As Long
            End Property
            """);

        CollectionAssert.AreEqual(new[] { "Colors", "Pt", "Fired", "Beep", "A", "D", "E" }, findings.Select(finding => finding.Message.Split('\'')[1]).ToArray());
        Assert.IsTrue(findings.All(finding => finding is { Id: RDCoreDiagnosticId.ImplicitPublicMember, Severity: DiagnosticSeverity.Information }));
        Assert.AreEqual(new SourceRange(new SourcePosition(9, 4), new SourcePosition(9, 5)), findings[4].Range, "the name and not the whole procedure");
    }

    #endregion

    #region RDC00106 implicit Variant declaration

    [TestMethod]
    public void AVariableOrParameterWithoutAType_IsAVariant_AndSuggestsSayingSo()
    {
        var findings = Run(new ImplicitVariantDeclarationAnalyzer(), """
            Private field
            Public Sub Work(x, y As Long, z%, Optional w = 1)
                Dim local1, local2 As Long
                Dim typed As Long, hinted$
                Static counter
                Dim items(1 To 3)
            End Sub
            """);

        CollectionAssert.AreEqual(
            new[] { "field", "x", "w", "local1", "counter", "items" },
            findings.Select(finding => finding.Message.Split('\'')[1]).ToArray());
        Assert.IsTrue(findings.All(finding => finding is { Id: RDCoreDiagnosticId.ImplicitVariantDeclaration, Severity: DiagnosticSeverity.Information }));
    }

    [TestMethod]
    public void ANameADefTypeCovers_IsNotAVariant()
    {
        var findings = Run(new ImplicitVariantDeclarationAnalyzer(), """
            DefInt A-C
            DefVar V
            Public Sub Work(a, other, vv)
                Dim b, c1, d
            End Sub
            """);

        CollectionAssert.AreEqual(new[] { "other", "vv", "d" }, findings.Select(finding => finding.Message.Split('\'')[1]).ToArray(),
            "a, b, c1 are Integers; other and d are Variants; vv is a Variant by DefVar");
    }

    [TestMethod]
    public void AConstant_TakesItsTypeFromItsValue_AndIsNotReported()
        => Assert.IsEmpty(Run(new ImplicitVariantDeclarationAnalyzer(), "Const K = 1\nPublic Sub Work()\n    Const L = 2\nEnd Sub\n"));

    #endregion

    #region RDC00107 implicit Variant return type

    [TestMethod]
    public void AFunctionWithoutAReturnType_ReturnsAVariant()
    {
        var findings = Run(new ImplicitVariantReturnTypeAnalyzer(), """
            Declare Function Beep Lib "kernel32" ()
            Function A()
            End Function
            Function B() As Long
            End Function
            Function C$()
            End Function
            Property Get D()
            End Property
            Sub E()
            End Sub
            """);

        CollectionAssert.AreEqual(new[] { "Beep", "A", "D" }, findings.Select(finding => finding.Message.Split('\'')[1]).ToArray());
        Assert.IsTrue(findings.All(finding => finding is { Id: RDCoreDiagnosticId.ImplicitVariantReturnType, Severity: DiagnosticSeverity.Information }));
    }

    [TestMethod]
    public void AFunctionADefTypeCovers_DoesNotReturnAVariant()
        => Assert.IsEmpty(Run(new ImplicitVariantReturnTypeAnalyzer(), "DefLng F\nFunction Foo()\nEnd Function\n"));

    #endregion

    #region RDC00201 Integer

    [TestMethod]
    public void AsInteger_IsSuggestedToBeALong_InEveryDeclarationButADeclare()
    {
        var findings = Run(new IntegerDataTypeAnalyzer(), """
            Private a As Integer
            Private b As VBA.Integer
            Private c As Long
            Private d As Excel.Integer
            Private e() As Integer
            Type Pt
                X As Integer
            End Type
            Declare Function Beep Lib "kernel32" (ByVal n As Integer) As Integer
            Public Function Calc(ByVal n As integer) As Integer
                Dim local As Integer
            End Function
            """);

        CollectionAssert.AreEqual(new[] { 0, 1, 4, 6, 9, 9, 10 }, Lines(findings));
        Assert.IsTrue(findings.All(finding => finding is { Id: RDCoreDiagnosticId.IntegerDataTypeDeclaration, Severity: DiagnosticSeverity.Information }));
    }

    #endregion

    #region RDC00202 module-level Dim

    [TestMethod]
    public void ADimAtTheModuleLevel_IsOneFindingForTheStatement()
    {
        var findings = Run(new ModuleScopeDimAnalyzer(), """
            Dim a As Long, b
            Private c
            Public d
            Dim e
            Public Sub Work()
                Dim local
            End Sub
            """);

        CollectionAssert.AreEqual(new[] { 0, 3 }, Lines(findings));
        Assert.AreEqual(0, findings[0].Range.End.Line, "the statement and not only its first variable");
        Assert.IsGreaterThan(findings[0].Range.Start.Character + "Dim a As Long".Length, findings[0].Range.End.Character);
        Assert.IsTrue(findings.All(finding => finding is { Id: RDCoreDiagnosticId.ModuleScopeDimDeclaration, Severity: DiagnosticSeverity.Information }));
    }

    #endregion

    #region RDC00203 multi-line parameter

    [TestMethod]
    public void AParameterBrokenOverLines_IsReported_AndAListWithOneOnEachLineIsNot()
    {
        var findings = Run(new MultilineParameterAnalyzer(), """
            Public Sub Work(ByVal a _
                As Long, _
                b As Long, _
                c As Long)
            End Sub
            """);

        var finding = findings.Single();
        Assert.AreEqual(RDCoreDiagnosticId.MultilineParameterDeclaration, finding.Id);
        Assert.AreEqual(DiagnosticSeverity.Information, finding.Severity);
        StringAssert.Contains(finding.Message, "'a'");
    }

    #endregion

    #region RDC00204 multiple declarations

    [TestMethod]
    public void AStatementThatDeclaresSeveralNames_IsOneFinding_AndSeveralStatementsAreNot()
    {
        var findings = Run(new MultipleDeclarationsAnalyzer(), """
            Private x%, y As Integer
            Enum Colors
                Red
                Green
            End Enum
            Public Sub Work()
                Dim a, b As Long
                Dim c As Long: Dim d As Long
                Const K = 1, L = 2
                Dim alone As Long
            End Sub
            """);

        CollectionAssert.AreEqual(new[] { 0, 6, 8 }, Lines(findings));
        Assert.IsTrue(findings.All(finding => finding is { Id: RDCoreDiagnosticId.MultipleDeclarations, Severity: DiagnosticSeverity.Information }));
        Assert.AreEqual(findings[1].Range.Start.Line, findings[1].Range.End.Line, "the statement, on its one line");
        Assert.AreEqual(new SourcePosition(6, 4), findings[1].Range.Start, "from the keyword that starts it");
    }

    #endregion

    #region RDC00205 misleading ByRef

    [TestMethod]
    public void ByRefOnThePropertyValueParameter_IsMisleading()
    {
        var findings = Run(new MisleadingByRefParameterAnalyzer(), """
            Property Let A(i As Long, ByRef v As Long)
            End Property
            Property Set B(ByRef v As Object)
            End Property
            Property Let C(ByVal v As Long)
            End Property
            Property Let D(v As Long)
            End Property
            Sub E(ByRef v As Long)
            End Sub
            """);

        CollectionAssert.AreEqual(new[] { 0, 2 }, Lines(findings));
        Assert.IsTrue(findings.All(finding => finding is { Id: RDCoreDiagnosticId.MisleadingByRefParameter, Severity: DiagnosticSeverity.Information }));
        StringAssert.Contains(findings[0].Message, "'v'");
    }

    #endregion
}
