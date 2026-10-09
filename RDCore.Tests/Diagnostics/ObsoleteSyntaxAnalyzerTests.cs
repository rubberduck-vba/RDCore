using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Workspace;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// The syntax that is a relic: <c>Rem</c>, <c>Error</c>, <c>Global</c>, <c>Let</c>, type-declaration characters, <c>While…Wend</c> and <c>On Local Error</c>. What
/// depends on the language, or on what a name is bound to, is a fact the host vouches for: those tests run the code through the host.
/// </summary>
[TestClass]
public sealed class ObsoleteSyntaxAnalyzerTests
{
    private static string Module(string source) => $"Attribute VB_Name = \"Program\"\r\n{source.Replace("\n", "\r\n")}\r\n";

    // without the host: what is written is all there is.
    private static AnalyzerFinding[] Run(IModuleAnalyzer analyzer, string source, ModuleSemanticsDto? semantics = null)
    {
        // the document the host loaded the module from, since the node ids of its facts name it.
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        var parsed = new ModuleParser().Parse(uri, source);
        Assert.IsEmpty(parsed.SyntaxErrors, string.Join("; ", parsed.SyntaxErrors.Select(error => error.Verbose)));
        return [.. analyzer.Analyze(new ModuleAnalysisContext(uri, parsed, semantics)).OrderBy(finding => finding.Range.Start)];
    }

    // through the host: the facts it vouches for come along.
    private static async Task<AnalyzerFinding[]> RunThroughHostAsync(IModuleAnalyzer analyzer, string source, SupportedLanguage? language = null)
    {
        var module = Module(source);
        var payload = await ModuleWorkspace.SemanticsAsync([], module, language: language);
        return Run(analyzer, module, payload.Modules.Single());
    }

    private static int[] Lines(AnalyzerFinding[] findings) => [.. findings.Select(finding => finding.Range.Start.Line)];

    private static void AssertAre(AnalyzerFinding[] findings, RDCoreDiagnosticId id)
        => Assert.IsTrue(findings.All(finding => finding is { Severity: DiagnosticSeverity.Information } && finding.Id == id));

    #region RDC00303 Rem

    private const string Comments = "Public Sub Work()\n    Rem a comment\n    ' a quote\n    Debug.Print 1 : REM trailing\n    ' Remember me\nEnd Sub";

    [TestMethod]
    [DataRow("vba")]
    [DataRow("vb6")]
    public async Task ARemComment_IsObsolete_WhereTheLanguageHasTheQuote(string language)
    {
        var findings = await RunThroughHostAsync(new ObsoleteRemCommentAnalyzer(), Comments, SupportedLanguages.Get(language));

        CollectionAssert.AreEqual(new[] { 2, 4 }, Lines(findings), "the Rem comments; a quote, and a comment that begins with Remember, are no Rem");
        AssertAre(findings, RDCoreDiagnosticId.ObsoleteCommentSyntax);
        Assert.AreEqual("Rem".Length, findings[0].Range.End.Character - findings[0].Range.Start.Character, "the keyword");
    }

    [TestMethod]
    public async Task ARemComment_IsHowABasicWritesOne()
        => Assert.IsEmpty(await RunThroughHostAsync(new ObsoleteRemCommentAnalyzer(), Comments, SupportedLanguages.BASIC));

    [TestMethod]
    public void WithNoWordFromTheHostOnTheLanguage_RemIsNotJudged()
        => Assert.IsEmpty(Run(new ObsoleteRemCommentAnalyzer(), Module(Comments)));

    #endregion

    #region RDC00304 Error

    private const string RaisesAnError = "Public Sub Work(ByVal c As Boolean)\n    Error 5\n    If c Then\n        Error 6\n    End If\n    Err.Raise 7\nEnd Sub";

    [TestMethod]
    public async Task AnErrorStatement_IsObsolete_WhereErrRaiseIsAvailable()
    {
        var findings = await RunThroughHostAsync(new ObsoleteErrorStatementAnalyzer(), RaisesAnError);

        CollectionAssert.AreEqual(new[] { 2, 4 }, Lines(findings), "also the one in a block");
        AssertAre(findings, RDCoreDiagnosticId.ObsoleteErrorSyntax);
    }

    [TestMethod]
    public async Task AnErrorStatement_IsHowABasicRaisesAnError()
        => Assert.IsEmpty(await RunThroughHostAsync(new ObsoleteErrorStatementAnalyzer(), RaisesAnError, SupportedLanguages.BASIC));

    #endregion

    #region RDC00305 Global

    [TestMethod]
    public void TheGlobalModifier_IsPublic_AndIsReportedAsTheKeyword()
    {
        var findings = Run(new ObsoleteGlobalModifierAnalyzer(), "Global total As Long\nGlobal Const Limit = 1\nGlobal a, b As Long\nPublic fine As Long\nPrivate hidden As Long\n");

        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, Lines(findings), "one for each statement, however many names it declares");
        AssertAre(findings, RDCoreDiagnosticId.ObsoleteGlobalModifier);
        Assert.AreEqual(new SourceRange(new SourcePosition(0, 0), new SourcePosition(0, 6)), findings[0].Range);
    }

    #endregion

    #region RDC00306 Let

    [TestMethod]
    public void ALetStatement_IsObsolete_AndIsReportedAsTheKeyword_WhereverItIsWritten()
    {
        var findings = Run(new ObsoleteLetStatementAnalyzer(), "Public Sub Work(ByVal c As Boolean)\n    Dim n As Long\n    Let n = 1\n    n = 2\n    If c Then\n        Let n = 3\n    End If\nEnd Sub");

        CollectionAssert.AreEqual(new[] { 2, 5 }, Lines(findings));
        AssertAre(findings, RDCoreDiagnosticId.ObsoleteLetStatement);
        Assert.AreEqual(new SourceRange(new SourcePosition(2, 4), new SourcePosition(2, 7)), findings[0].Range);
    }

    #endregion

    #region RDC00307 type hints

    private const string Hinted = """
        Option Explicit
        Private text$
        Public Function Title$()
            Title$ = "x"
        End Function
        Public Sub Work(ByVal count%)
            Dim plain As String
            plain = Mid$("abc", 1, 1)
            plain = Error$(5)
            count% = 1
            Debug.Print text$, plain
        End Sub
        """;

    [TestMethod]
    public async Task ATypeHintOnADeclaration_AndOnAReferenceToIt_IsObsolete()
    {
        var findings = await RunThroughHostAsync(new ObsoleteTypeHintAnalyzer(), Hinted);

        // (the module has a header line) text$ declared; Title$ declared and assigned; count% declared and assigned; text$ read.
        CollectionAssert.AreEqual(new[] { 2, 3, 4, 6, 10, 11 }, Lines(findings));
        AssertAre(findings, RDCoreDiagnosticId.ObsoleteTypeHint);
        StringAssert.Contains(findings[0].Message, "'text'");
    }

    [TestMethod]
    public async Task ATypeHintOnAMemberOfTheStandardLibrary_HasNoDeclarationToChange()
    {
        var findings = await RunThroughHostAsync(new ObsoleteTypeHintAnalyzer(), Hinted);

        Assert.IsFalse(findings.Any(finding => finding.Range.Start.Line is 8 or 9), "Mid$ and Error$");
    }

    [TestMethod]
    public void WithNoFactsFromTheHost_OnlyTheDeclarationsAreReported()
    {
        var findings = Run(new ObsoleteTypeHintAnalyzer(), Module(Hinted));

        CollectionAssert.AreEqual(new[] { 2, 3, 6 }, Lines(findings));
    }

    #endregion

    #region RDC00308 While…Wend

    [TestMethod]
    public void AWhileWendLoop_IsObsolete_AndIsReportedAsItsHeader()
    {
        var findings = Run(new ObsoleteWhileWendAnalyzer(), "Public Sub Work(ByVal c As Boolean)\n    While c\n        Debug.Print 1\n    Wend\n    Do While c\n        Debug.Print 2\n    Loop\nEnd Sub");

        var finding = findings.Single();
        Assert.AreEqual(RDCoreDiagnosticId.ObsoleteWhileWend, finding.Id);
        Assert.AreEqual(new SourceRange(new SourcePosition(1, 4), new SourcePosition(1, 11)), finding.Range, "While and its condition");
    }

    #endregion

    #region RDC00309 On Local Error

    [TestMethod]
    public void OnLocalError_IsObsolete_AndOnErrorIsNot()
    {
        var findings = Run(new ObsoleteOnLocalErrorStatementAnalyzer(),
            "Public Sub Work()\n    On Local Error GoTo Handler\n    On Local Error Resume Next\n    On Error GoTo Handler\n    On Error Resume Next\n    Exit Sub\nHandler:\nEnd Sub");

        CollectionAssert.AreEqual(new[] { 1, 2 }, Lines(findings));
        AssertAre(findings, RDCoreDiagnosticId.ObsoleteOnLocalErrorStatement);
    }

    #endregion
}
