using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer;
using RDCore.LanguageServer.Diagnostics;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Support.Host;
using RDCore.Runtime.Support.Host.Handlers;
using RDCore.SDK.Client;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Workspace;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// RDC01026: the branches of a document's <c>#If</c> blocks that the constants leave out (<strong>MS-VBAL §3.4</strong>), as the environment host evaluates
/// them when it defines the module's symbols, are told to the client by the language server: one Hint per branch, tagged <c>Unnecessary</c>, over the
/// branch's whole lines and none of its directives'.
/// </summary>
[TestClass]
public sealed class DeadBranchDiagnosticsTests
{
    private const string Code = "RDC01026";
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-dead-branches-ws");
    private static readonly Uri WorkspaceRoot = new("file:///c:/ws/");

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();
    private readonly IPlatformOrchestrationService _orchestration = Substitute.For<IPlatformOrchestrationService>();

    private ModuleLiveBranches? _liveBranches;
    private WorkspaceDocument? _document;

    private static string Lines(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    // the document as the workspace reads it now, and its parse, which is the parse the host is sent.
    private ModuleParseResult Opened(string text, int version = 1)
    {
        var document = new WorkspaceDocument("src/Mod1.bas", Root, text, version);
        var uri = document.Id.Uri.ToUri();
        var parse = new ModuleParser().Parse(uri, text);
        _document = document;
        _documents.TryGetDocument(uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = _document; return true; });
        _parsing.TryGetCached(uri, Arg.Any<int>(), out Arg.Any<ModuleParseResult>()).Returns(call => { call[2] = parse; return true; });
        _parsing.TryGetCached(uri, out Arg.Any<ModuleParseResult>()).Returns(call => { call[1] = parse; return true; });
        _parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>()).Returns(parse);
        return parse;
    }

    // the host, its session made of the project with its constants, evaluates the module's directives with them and its own, as the workspace sync has it
    // define the module, and the language server records what it says is dead (SymbolSyncService).
    private async Task<DefineSymbolsResult> DefinedAsync(ModuleParseResult parse, Dictionary<string, string>? projectConstants = null)
    {
        var host = new EnvironmentSessionProvider(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), new MockFileSystem(), NullLogger<EnvironmentSessionProvider>.Instance);
        host.Compose(new RDCoreProject
        {
            Modules = [new RDCoreModule { RelativeUri = "src/Mod1.bas" }],
            PrecompilerConstants = new(projectConstants ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase),
        }, WorkspaceRoot);
        var handler = new DefineSymbolsHandler(host, Substitute.For<IProjectAnalysisSessions>(), Substitute.For<IErrorDetailsRecorder>(),
            NullLogger<DefineSymbolsHandler>.Instance);
        var result = await handler.Handle(new DefineSymbolsParams
        {
            WorkspaceRoot = WorkspaceRoot,
            ModuleName = "Mod1",
            ModuleUri = parse.SyntaxTree!.SourceLocation.Uri,
            Symbols = [],
            PrecompilerTriviaJson = DefineSymbolsParams.PrecompilerTriviaJsonOf(parse),
        }, CancellationToken.None);

        _liveBranches ??= new ModuleLiveBranches(_documents, _parsing);
        _liveBranches.Record(parse, result.DeadBranches);
        return result;
    }

    private DocumentDiagnosticsService Sut(IDisabledDiagnostics? disabled = null)
        => new(_documents, _parsing, _orchestration, Substitute.For<ISymbolSyncService>(), new WorkspaceRefresh(TimeSpan.Zero),
            NullLogger<DocumentDiagnosticsService>.Instance, disabled: disabled, liveBranches: _liveBranches);

    // what the client is told of the document, of the phase asked for; with the core extension as a provider that finds nothing, or with no provider.
    private async Task<Diagnostic[]> LeftOutAsync(bool provider = false, AnalysisPhase phase = AnalysisPhase.All, IDisabledDiagnostics? disabled = null)
    {
        IRDCoreClientApp[] extensions = provider ? [Provider()] : [];
        _orchestration.Extensions.Returns(extensions);
        var result = await Sut(disabled).GetPhaseAsync(_document!.Id.Uri.ToUri(), phase, CancellationToken.None);
        return [.. result.Diagnostics.Where(diagnostic => diagnostic.Code?.String == Code)];
    }

    private IRDCoreClientApp Provider()
    {
        var app = Substitute.For<IRDCoreClientApp>();
        app.ExtensionInfo.Returns(new ExtensionInfo
        {
            Name = "RDCore.Diagnostics.exe",
            Title = "RDCore.Diagnostics",
            Version = new Version(1, 0),
            Publisher = "publisher",
            PublisherWebUrl = "https://example.test",
            Description = "desc",
            FileHash = "hash",
            Capabilities = [new PlatformExtensionServerCapability(nameof(DiagnoseDocument))],
            DiagnosticPrefix = "RDC",
            IsCoreExtension = true,
        });
        app.WaitForReadyAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        app.SendRequestAsync<DiagnoseDocumentParams, DiagnoseDocumentResult>(Arg.Any<DiagnoseDocumentParams>(), Arg.Any<CancellationToken>())
            .Returns(new DiagnoseDocumentResult { Diagnostics = [], SourceVersion = _document!.Version });
        return app;
    }

    // the whole of a line of the document, and of the lines down to another.
    private Range LinesOf(int first, int last) => new(first, 0, last, _document!.Text.Split("\r\n")[last].Length);

    private static IDisabledDiagnostics Disabling(string code)
    {
        var disabled = Substitute.For<IDisabledDiagnostics>();
        var parsed = RDCoreDiagnosticCode.TryParse(code, out var each) ? each : throw new ArgumentException(code);
        disabled.Codes.Returns(new HashSet<RDCoreDiagnosticCode> { parsed });
        return disabled;
    }

    // 0 Option Explicit / 1 #Const DEBUGMODE / 2 #If / 3 the If branch / 4 #Else / 5 the Else branch / 6 #End If
    private static string ModuleConstant(string value) => Lines(
        "Option Explicit",
        $"#Const DEBUGMODE = {value}",
        "#If DEBUGMODE Then",
        "Private Const Mode As String = \"debug\"",
        "#Else",
        "Private Const Mode As String = \"release\"",
        "#End If");

    // 0 Option Explicit / 1 #If / 2 the If branch / 3 #Else / 4 the Else branch / 5 #End If
    private static string ProjectConstant { get; } = Lines(
        "Option Explicit",
        "#If DEBUGMODE Then",
        "Private Const Mode As String = \"debug\"",
        "#Else",
        "Private Const Mode As String = \"release\"",
        "#End If");

    [TestMethod]
    [DataRow("0", 3, false, DisplayName = "#Const 0, no provider: the If branch")]
    [DataRow("1", 5, false, DisplayName = "#Const 1, no provider: the Else branch")]
    [DataRow("0", 3, true, DisplayName = "#Const 0, the core extension: the If branch")]
    [DataRow("1", 5, true, DisplayName = "#Const 1, the core extension: the Else branch")]
    public async Task TheBranchAModuleConstantLeavesOut_IsOneUnnecessaryHint_OverItsLines(string value, int line, bool provider)
    {
        await DefinedAsync(Opened(ModuleConstant(value)));

        var leftOut = (await LeftOutAsync(provider)).Single();

        Assert.AreEqual(LinesOf(line, line), leftOut.Range);
        Assert.AreEqual(DiagnosticSeverity.Hint, leftOut.Severity);
        CollectionAssert.AreEqual(new[] { DiagnosticTag.Unnecessary }, leftOut.Tags!.ToArray());
        Assert.AreEqual(DeadBranchMessages.LeftOut, leftOut.Message);
    }

    [TestMethod]
    [DataRow("0", 2, DisplayName = "DEBUGMODE 0: the If branch")]
    [DataRow("1", 4, DisplayName = "DEBUGMODE 1: the Else branch")]
    public async Task TheBranchAProjectConstantLeavesOut_IsTheOneTold(string value, int line)
    {
        await DefinedAsync(Opened(ProjectConstant), new() { ["DEBUGMODE"] = value });

        Assert.AreEqual(LinesOf(line, line), (await LeftOutAsync()).Single().Range);
    }

    // the project's constants change, and the host defines the module again: what it says is dead now is what the client is told.
    [TestMethod]
    public async Task AProjectConstantThatChanges_LeavesOutTheOtherBranch()
    {
        var parse = Opened(ProjectConstant);
        await DefinedAsync(parse, new() { ["DEBUGMODE"] = "0" });
        var before = (await LeftOutAsync()).Single().Range;

        await DefinedAsync(parse, new() { ["DEBUGMODE"] = "1" });
        var after = (await LeftOutAsync()).Single().Range;

        Assert.AreEqual(LinesOf(2, 2), before);
        Assert.AreEqual(LinesOf(4, 4), after);
    }

    // a block written in a branch that is left out is left out with it: the branch is one finding, the nested directives inside it.
    [TestMethod]
    public async Task ABlockInABranchLeftOut_IsInItsLines()
    {
        await DefinedAsync(Opened(Lines(
            "Option Explicit",
            "#If 0 Then",
            "Private Const A As Long = 1",
            "#If 1 Then",
            "Private Const B As Long = 2",
            "#End If",
            "Private Const C As Long = 3",
            "#End If")));

        CollectionAssert.AreEqual(new[] { LinesOf(2, 6) }, (await LeftOutAsync()).Select(diagnostic => diagnostic.Range).ToArray());
    }

    [TestMethod]
    public async Task ABlockInALiveBranch_HasItsOwnBranchLeftOut()
    {
        await DefinedAsync(Opened(Lines(
            "Option Explicit",
            "#If 1 Then",
            "Private Const A As Long = 1",
            "#If 0 Then",
            "Private Const B As Long = 2",
            "#End If",
            "#End If")));

        CollectionAssert.AreEqual(new[] { LinesOf(4, 4) }, (await LeftOutAsync()).Select(diagnostic => diagnostic.Range).ToArray());
    }

    // the host says the empty #If branch is dead, and it has no line to be told of.
    [TestMethod]
    public async Task ABranchOfNoLine_IsNotTold()
    {
        var result = await DefinedAsync(Opened(Lines(
            "Option Explicit",
            "#If 0 Then",
            "#Else",
            "Private Const A As Long = 1",
            "#End If")));
        Assert.HasCount(1, result.DeadBranches, "the premise: the host evaluates the empty branch as dead");

        Assert.IsEmpty(await LeftOutAsync());
    }

    // the host evaluated the text as it read then: what is dead of the text as it reads now is not known until it is defined again.
    [TestMethod]
    public async Task ATextEditedSinceTheHostDefinedIt_HasNoneTold()
    {
        var text = ModuleConstant("0");
        await DefinedAsync(Opened(text));
        Assert.HasCount(1, await LeftOutAsync(), "the premise: the text the host defined has a branch left out");

        _document = new WorkspaceDocument("src/Mod1.bas", Root, text, 2);

        Assert.IsEmpty(await LeftOutAsync());
    }

    [TestMethod]
    public async Task TheCodeDisabled_HasNoneTold()
    {
        await DefinedAsync(Opened(ModuleConstant("0")));
        Assert.HasCount(1, await LeftOutAsync(), "the premise: the code enabled, the branch is told");

        Assert.IsEmpty(await LeftOutAsync(disabled: Disabling(Code)));
    }

    // the static phase tells them, and the runtime phase, which the client is told after it with the static phase's findings, does not tell them again.
    [TestMethod]
    public async Task TheStaticPhaseTellsThem_AndTheRuntimePhaseDoesNot()
    {
        await DefinedAsync(Opened(ModuleConstant("0")));

        Assert.HasCount(1, await LeftOutAsync(phase: AnalysisPhase.Static));
        Assert.IsEmpty(await LeftOutAsync(phase: AnalysisPhase.Runtime));
    }
}
