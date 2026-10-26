using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Analyzers;
using RDCore.Diagnostics.Handlers;
using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.ProtocolExtensions;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <see cref="DiagnoseDocumentHandler"/> is the inaugural diagnostics provider: it projects the
/// parser's located syntax errors through <see cref="ICoreDiagnosticsFactory"/> and returns them as
/// LSP <see cref="Diagnostic"/>s over <c>rdcore/diagnostics/document</c>.
/// </summary>
[TestClass]
public sealed class DiagnoseDocumentHandlerTests
{
    // a #If that splits the function header — unparseable, so the parser yields located syntax errors.
    private const string SplitConditionalModule = """
        #If VBA7 Then
        Private Function GetPtr() As LongPtr
        #Else
        Private Function GetPtr() As Long
        #End If
            GetPtr = 0
        End Function
        """;

    private const string CleanModule = "Option Explicit\r\n\r\nPublic Sub DoNothing()\r\nEnd Sub\r\n";

    private static DiagnoseDocumentHandler NewHandler(params IModuleAnalyzer[] analyzers)
        => new(new DiagnosticFactory(), analyzers.Length > 0 ? analyzers : [new OptionExplicitAnalyzer(), new ObsoleteCallStatementAnalyzer()], NullLogger<DiagnoseDocumentHandler>.Instance);

    private static DiagnoseDocumentRequest RequestFor(
        string source, out ModuleParseResult parseResult, int version = 1, ModuleSemanticsDto? semantics = null, AnalysisPhase phase = AnalysisPhase.All)
    {
        var uri = TestUri.TestModuleUri();
        parseResult = new ModuleParser().Parse(uri, source);
        return new DiagnoseDocumentRequest
        {
            Json = PlatformJson.Serialize(new DiagnoseDocumentPayload(uri, version, parseResult, semantics, phase)),
        };
    }

    private static Task<DiagnoseDocumentResponse> HandleAsync(DiagnoseDocumentRequest request, params IModuleAnalyzer[] analyzers)
        => NewHandler(analyzers).Handle(request, CancellationToken.None);

    [TestMethod]
    public async Task SyntaxErrors_AreProjectedToCodedLspDiagnostics()
    {
        var request = RequestFor(SplitConditionalModule, out var parseResult, version: 7);
        Assert.IsNotEmpty(parseResult.SyntaxErrors);

        var result = await HandleAsync(request);
        var diagnostics = result.Diagnostics.ToArray();

        Assert.AreEqual(parseResult.SyntaxErrors.Length, diagnostics.Length);
        Assert.AreEqual(7, result.SourceVersion, "the provider echoes the source version back");

        for (var i = 0; i < parseResult.SyntaxErrors.Length; i++)
        {
            var error = parseResult.SyntaxErrors[i];
            var diagnostic = diagnostics[i];

            Assert.AreEqual(error.ToDiagnosticCode(), diagnostic.Code!.Value.String);
            StringAssert.StartsWith(diagnostic.Code!.Value.String, "VBC");
            var href = diagnostic.CodeDescription!.Href.ToString();
            Assert.AreEqual(
                $"https://rubberduck-vba.github.io/RDCore/diagnostics/{error.ToDiagnosticCode().ToLowerInvariant()}.html",
                href, "the LSP client opens this URL — it must resolve to a real docs-site page");
            Assert.AreEqual(error.Location.Range.ToLsp(), diagnostic.Range);
            Assert.AreEqual(error.Description, diagnostic.Message);
            Assert.AreEqual("RDCore", diagnostic.Source);
            Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.IsNotNull(diagnostic.Data, "the verbose token detail rides Data");
        }
    }

    [TestMethod]
    public async Task CleanParse_YieldsNoDiagnostics()
    {
        var result = await HandleAsync(RequestFor(CleanModule, out _));

        Assert.IsEmpty(result.Diagnostics);
        Assert.AreEqual(1, result.SourceVersion);
    }

    private static readonly Uri Module = new("file://rdcore-test#Mod1");
    private static readonly SourceLocation Somewhere = new(Module, new SourceRange(new SourcePosition(4, 2), new SourcePosition(4, 9)));

    private static ModuleSemanticsDto Semantics(
        bool optionExplicit = true, CompileErrorDto[]? declarationErrors = null, ProcedureSemanticsDto[]? procedures = null)
        => new(Module, optionExplicit, [.. declarationErrors ?? []], [.. procedures ?? []], []);

    private static ProcedureSemanticsDto Procedure(CompileErrorDto[]? errors = null, ExpressionFactDto[]? expressions = null)
        => new(new Uri("file://rdcore-test#Mod1.Run"), true, [.. errors ?? []], [.. expressions ?? []]);

    [TestMethod]
    public async Task WhatTheHostFoundWrongWithTheCode_IsReportedAsCompileErrors()
    {
        var semantics = Semantics(
            declarationErrors: [new CompileErrorDto(VBCompileErrorId.DuplicateDeclaration, Somewhere, "Total")],
            procedures: [Procedure([new CompileErrorDto(VBCompileErrorId.ExitDoNotWithinDoLoop, Somewhere, "Exit Do")])]);

        var result = await HandleAsync(RequestFor(CleanModule, out _, semantics: semantics));

        CollectionAssert.AreEqual(new[] { "VBC09303", "VBC09312" }, result.Diagnostics.Select(diagnostic => diagnostic.Code!.Value.String).ToArray());
        Assert.IsTrue(result.Diagnostics.All(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.IsTrue(result.Diagnostics.All(diagnostic => diagnostic.Range == Somewhere.Range.ToLsp()));
    }

    [TestMethod]
    public async Task AModuleWithoutOptionExplicit_IsAWarning_ByAFactTheHostVouchesFor()
    {
        var result = await HandleAsync(RequestFor(CleanModule, out _, semantics: Semantics(optionExplicit: false)));

        var diagnostic = result.Diagnostics.Single();
        Assert.AreEqual("RDC00101", diagnostic.Code!.Value.String);
        Assert.AreEqual(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.AreEqual("https://rubberduck-vba.github.io/RDCore/diagnostics/rdc00101.html", diagnostic.CodeDescription!.Href.ToString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(diagnostic.Message));
    }

    [TestMethod]
    public async Task AModuleWithOptionExplicit_HasNothingToSay()
        => Assert.IsEmpty((await HandleAsync(RequestFor(CleanModule, out _, semantics: Semantics(optionExplicit: true)))).Diagnostics);

    [TestMethod]
    public async Task WithNoFactsFromTheHost_AnAnalyzerHasNothingToSay_ForItWillNotGuess()
        => Assert.IsEmpty((await HandleAsync(RequestFor("Public Sub Run()\r\nEnd Sub", out _))).Diagnostics);

    [TestMethod]
    public async Task TheCallKeyword_IsAHintAtWhereTheCalleeIsWritten()
    {
        var callee = new ExpressionFactDto(
            new SyntaxNodeId("file:///doc", [3, 1]), Somewhere, null, ExpressionClassification.Subroutine, null, ValueExpressionSemanticFlags.ExplicitCallKeyword, null);
        var other = callee with { Node = new SyntaxNodeId("file:///doc", [4, 1]), Flags = 0 };

        var result = await HandleAsync(RequestFor(CleanModule, out _, semantics: Semantics(procedures: [Procedure(expressions: [callee, other])])));

        var diagnostic = result.Diagnostics.Single();
        Assert.AreEqual("RDC00302", diagnostic.Code!.Value.String);
        Assert.AreEqual(DiagnosticSeverity.Hint, diagnostic.Severity);
        Assert.AreEqual(Somewhere.Range.ToLsp(), diagnostic.Range);
    }

    private sealed class FailingAnalyzer : IModuleAnalyzer
    {
        public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context) => throw new InvalidOperationException("boom");
    }

    [TestMethod]
    public async Task AnAnalyzerThatFails_DoesNotSinkTheOthers()
    {
        var result = await HandleAsync(
            RequestFor(CleanModule, out _, semantics: Semantics(optionExplicit: false)), new FailingAnalyzer(), new OptionExplicitAnalyzer());

        Assert.AreEqual("RDC00101", result.Diagnostics.Single().Code!.Value.String);
    }

    [TestMethod]
    public async Task WhatTheSyntaxTreeSays_NeedsNoFactsFromTheHost_AndLinksToItsPage()
    {
        var result = await HandleAsync(RequestFor("Public Sub Work(total As Long)\r\nEnd Sub\r\n", out _), new ImplicitByRefModifierAnalyzer());

        var diagnostic = result.Diagnostics.Single();
        Assert.AreEqual("RDC00104", diagnostic.Code!.Value.String);
        Assert.AreEqual(DiagnosticSeverity.Information, diagnostic.Severity);
        Assert.AreEqual("https://rubberduck-vba.github.io/RDCore/diagnostics/rdc00104.html", diagnostic.CodeDescription!.Href.ToString());
        StringAssert.Contains(diagnostic.Message, "'total'");
    }

    private sealed class SaysSomething(AnalysisPhase phase, int line) : IModuleAnalyzer
    {
        public AnalysisPhase Phase => phase;

        public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
            => [new AnalyzerFinding(RDCoreDiagnosticId.ImplicitNarrowingConversion, new SourceRange(new SourcePosition(line, 0), new SourcePosition(line, 1)), DiagnosticSeverity.Warning, phase.ToString())];
    }

    [TestMethod]
    public async Task AnAnalyzerIsAsked_OnlyInThePhaseItDeclares()
    {
        var statically = new SaysSomething(AnalysisPhase.Static, 1);
        var atRuntime = new SaysSomething(AnalysisPhase.Runtime, 2);

        var staticOnly = await HandleAsync(RequestFor(CleanModule, out _, phase: AnalysisPhase.Static), statically, atRuntime);
        var runtimeOnly = await HandleAsync(RequestFor(CleanModule, out _, phase: AnalysisPhase.Runtime), statically, atRuntime);
        var everything = await HandleAsync(RequestFor(CleanModule, out _), statically, atRuntime);

        CollectionAssert.AreEqual(new[] { "Static" }, staticOnly.Diagnostics.Select(diagnostic => diagnostic.Message).ToArray());
        CollectionAssert.AreEqual(new[] { "Runtime" }, runtimeOnly.Diagnostics.Select(diagnostic => diagnostic.Message).ToArray());
        Assert.AreEqual(2, everything.Diagnostics.Count(), "a request that does not say is a request for everything");
    }

    [TestMethod]
    public async Task WhatIsWrongWithTheSyntax_IsOfTheStaticPhase_AndIsNotSaidAgainByTheRuntimeOne()
    {
        var staticPhase = await HandleAsync(RequestFor(SplitConditionalModule, out _, phase: AnalysisPhase.Static));
        var runtimePhase = await HandleAsync(RequestFor(SplitConditionalModule, out _, phase: AnalysisPhase.Runtime));

        Assert.IsNotEmpty(staticPhase.Diagnostics.ToArray());
        Assert.IsEmpty(runtimePhase.Diagnostics.ToArray(), "the phases are put together by the language server, so each finding is in one of them");
    }

    [TestMethod]
    public void AnAnalyzerThatDoesNotSay_IsOfTheStaticPhase()
        => Assert.AreEqual(AnalysisPhase.Static, ((IModuleAnalyzer)new OptionExplicitAnalyzer()).Phase);

    [TestMethod]
    public void TheAnalyzerThatReadsWhatEvaluatingTheCodeFound_IsOfTheRuntimePhase()
        => Assert.AreEqual(AnalysisPhase.Runtime, new ImplicitNarrowingConversionAnalyzer().Phase);

    private const string OverflowModule = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n    Dim n As Byte\r\n    n = 5678\r\n    Error 5\r\nEnd Sub\r\n";

    // what the language says of the module: the analyzer is there to show that it is not what is asserted.
    private static async Task<Diagnostic[]> LanguageDiagnosticsAsync(AnalysisPhase phase)
    {
        var semantics = (await Cli.ModuleWorkspace.SemanticsAsync([], OverflowModule)).Modules.Single();
        var result = await HandleAsync(RequestFor(OverflowModule, out _, semantics: semantics, phase: phase), new SaysSomething(AnalysisPhase.All, 0));
        return [.. result.Diagnostics.Where(diagnostic => diagnostic.Code!.Value.String!.StartsWith("VB"))];
    }

    [TestMethod]
    public async Task ARunTimeErrorTheCodeRaisesWheneverItRuns_IsAnError_WhereItIsRaised()
    {
        var diagnostic = (await LanguageDiagnosticsAsync(AnalysisPhase.Runtime)).Single();

        Assert.AreEqual("VBR00006", diagnostic.Code!.Value.String);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.AreEqual(4, diagnostic.Range.Start.Line, "the assignment that cannot hold its value");
        Assert.AreEqual("https://rubberduck-vba.github.io/RDCore/diagnostics/vbr00006.html", diagnostic.CodeDescription!.Href.ToString());
    }

    [TestMethod]
    public async Task ARunTimeError_IsOfTheRuntimePhase()
        => Assert.IsEmpty(await LanguageDiagnosticsAsync(AnalysisPhase.Static));

    // the run-time diagnostics of the body of Main, written after a declaration of each of these variables and a Function Half(ByVal Value As Long).
    private static async Task<Diagnostic[]> RuntimeDiagnosticsOfAsync(params string[] body)
    {
        var module = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n"
            + "    Dim b() As Byte\r\n    Dim n As Long\r\n    Dim i As Integer\r\n    Dim s As String\r\n    Dim v As Variant\r\n"
            + string.Concat(body.Select(line => $"    {line}\r\n"))
            + "End Sub\r\nPrivate Function Half(ByVal Value As Long) As Long\r\n    Half = Value \\ 2\r\nEnd Function\r\n";
        var semantics = (await Cli.ModuleWorkspace.SemanticsAsync([], module)).Modules.Single();
        var result = await HandleAsync(RequestFor(module, out _, semantics: semantics, phase: AnalysisPhase.Runtime), new SaysSomething(AnalysisPhase.All, 0));
        return [.. result.Diagnostics.Where(diagnostic => diagnostic.Code!.Value.String!.StartsWith("VB"))];
    }

    // the first line of the body of Main, after its declarations.
    private const int FirstLineOfTheBody = 8;

    // MS-VBAL 5.5.1.2.6: a Byte() is let-coerced to nothing but a Byte() and a String. What it holds is not known to an analysis, but its type is, and the
    // type decides: whatever it holds, it is error 13.
    [TestMethod]
    [DataRow("n = b", DisplayName = "assigned to a Long")]
    [DataRow("n = Half(b)", DisplayName = "passed to a ByVal Long parameter")]
    [DataRow("n = (b)", DisplayName = "in parentheses, assigned to a Long")]
    [DataRow("i = b", DisplayName = "assigned to an Integer")]
    public async Task AByteArray_WhereItIsNoNumber_IsATypeMismatch_WhateverItHolds(string line)
    {
        var diagnostic = (await RuntimeDiagnosticsOfAsync(line)).Single();

        Assert.AreEqual("VBR00013", diagnostic.Code!.Value.String);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.AreEqual(FirstLineOfTheBody, diagnostic.Range.Start.Line);
    }

    // what is not known to happen is not said: a Byte() is a String, a Variant can hold a Long, and a Long can fit an Integer.
    [TestMethod]
    [DataRow("s = b", DisplayName = "a Byte() assigned to a String")]
    [DataRow("n = v", DisplayName = "a Variant assigned to a Long")]
    [DataRow("i = n", DisplayName = "a Long assigned to an Integer")]
    [DataRow("n = v + 1", DisplayName = "a Variant operand")]
    public async Task AnErrorTheValuesMayOrMayNotRaise_IsNotReported(string line)
        => Assert.IsEmpty(await RuntimeDiagnosticsOfAsync(line));

    [TestMethod]
    public async Task AnErrorTheCodeRaisesForItself_IsNotReported()
        => Assert.IsFalse((await LanguageDiagnosticsAsync(AnalysisPhase.Runtime)).Any(diagnostic => diagnostic.Range.Start.Line == 5), "Error 5 is what the code is there to do");
}
