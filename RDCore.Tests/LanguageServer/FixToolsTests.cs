using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Assistant;
using RDCore.LanguageServer.Diagnostics;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Assistant;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Server.Assistant;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;
using System.Globalization;
using System.Text.Json.Nodes;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// <c>list_fixes</c> and <c>preview_fix</c>: the code actions of a document as an assistant reads them, each named by an id that finds it again, and what a fix
/// would change as a unified diff of each document.
/// </summary>
[TestClass]
public sealed class FixToolsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-fix-tools-ws");

    private const string Program = "10 Dim strName As String\r\n20 Dim x As Long\r\n30 strName = \"a\"\r\n40 Debug.Print strName, x\r\n";

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();
    private readonly IDocumentDiagnosticsService _diagnostics = Substitute.For<IDocumentDiagnosticsService>();

    public FixToolsTests()
    {
        var document = new WorkspaceDocument("Main.rdc", Root, Program, version: 2);
        var uri = document.Id.Uri.ToUri();
        _documents.WorkspaceRoot.Returns(Root);
        _documents.GetAllDocuments().Returns([document]);
        _documents.TryGetDocument(uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = document; return true; });
        _parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new ModuleParser().Parse(uri, ParsingClientService.ModuleSourceOf(document))));
        _diagnostics.GetAsync(uri, Arg.Any<string?>(), Arg.Any<CancellationToken>(), Arg.Any<IProgress<RDCore.SDK.Server.Progress.WorkProgressStep>?>())
            .Returns(new DocumentDiagnosticsResult("1", false, [FixedByEdits(version: 2)]));
    }

    // a finding of RDC00115 on line 3, fixed by two edits of the program: the declaration of a local, or the parameter passed ByRef.
    private static Diagnostic FixedByEdits(int version) => new()
    {
        Range = new Range(2, 3, 2, 10),
        Code = "RDC00115",
        Source = "RDCore",
        Severity = DiagnosticSeverity.Warning,
        Message = "Implicit result local",
        Data = new DiagnosticFixes
        {
            Edits =
            [
                new EditFix
                {
                    Title = "Declare a local",
                    Edits = [new TextEdit { Range = new Range(1, 0, 1, 0), NewText = "15 Dim result As Long\r\n" }, new TextEdit { Range = new Range(2, 3, 2, 10), NewText = "result" }],
                    Version = version,
                },
                new EditFix { Title = "Pass it ByRef", Edits = [new TextEdit { Range = new Range(2, 3, 2, 10), NewText = "x" }], ChangesBehavior = true, Version = version },
            ],
        }.ToData(),
    };

    // the client the edits are applied by: one that applies them, and answers what it is told to.
    private readonly IClientEdits _client = Substitute.For<IClientEdits>();

    private ApplyWorkspaceEditResponse _applied = new() { Applied = true };

    private void ClientApplies(bool applies = true)
    {
        _client.Capabilities.Returns(new ClientCapabilities
        {
            Workspace = new WorkspaceClientCapabilities
            {
                ApplyEdit = applies,
                WorkspaceEdit = new WorkspaceEditCapability
                {
                    DocumentChanges = true,
                    ChangeAnnotationSupport = new WorkspaceEditSupportCapabilitiesChangeAnnotationSupport(),
                    ResourceOperations = new Container<ResourceOperationKind>(ResourceOperationKind.Rename),
                },
            },
        });
        _client.ApplyAsync(Arg.Any<string>(), Arg.Any<WorkspaceEdit>(), Arg.Any<CancellationToken>()).Returns(_ => _applied);
    }

    private FixToolEdits Fixes(AssistantDocuments documents, IRenameService rename)
    {
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });
        var actions = new CodeActionService(rename, _documents, new System.IO.Abstractions.TestingHelpers.MockFileSystem(), new ExtractMethodService(rename, _parsing, options),
            new MoveMemberService(rename, _parsing), Substitute.For<RDCore.SDK.Server.Services.IPlatformClientCapabilitiesService>(),
            new OmniSharp.Extensions.LanguageServer.Protocol.Serialization.LspSerializer());
        return new FixToolEdits(documents, _diagnostics, Substitute.For<IDocumentLifecycleService>(), actions, _documents,
            new System.IO.Abstractions.TestingHelpers.MockFileSystem(), _client);
    }

    private RenameService Rename()
    {
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });
        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StandardLibrary.LibraryName])).ProvideSymbols()]);
        return new RenameService(_documents, _parsing, new IntrinsicSymbolResolver(), libraries, options, NullLogger<RenameService>.Instance, OneProject.Registry());
    }

    private AssistantToolCatalog Sut()
    {
        var rename = Rename();
        var documents = new AssistantDocuments(_documents, Substitute.For<ISymbolSyncService>());
        var fixes = Fixes(documents, rename);
        return new AssistantToolCatalog([new ListFixesTool(fixes), new PreviewFixTool(fixes), new ApplyFixTool(fixes), new RenameTool(rename, fixes)]);
    }

    private async Task<ToolCallResult> CallAsync(string tool, JsonObject arguments)
    {
        var culture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        try
        {
            return await Sut().CallAsync(new ToolCallParams { Name = tool, Arguments = arguments }, CancellationToken.None);
        }
        finally
        {
            CultureInfo.CurrentUICulture = culture;
        }
    }

    private async Task<JsonArray> FixesAsync(JsonObject arguments)
    {
        var listed = await CallAsync(CoreAssistantTools.ListFixes, arguments);
        Assert.IsFalse(listed.IsError, listed.Content.Single().Text);
        return listed.StructuredContent!["fixes"]!.AsArray();
    }

    [TestMethod]
    public async Task TheFixesOfADocument_AreThoseOfItsDiagnostics_BestFirst_EachNamedByWhatItIs()
    {
        var fixes = await FixesAsync(new JsonObject { ["path"] = "Main.rdc" });

        CollectionAssert.AreEqual(new[] { "Declare a local", "Pass it ByRef" }, fixes.Select(fix => (string?)fix!["title"]).ToArray());
        CollectionAssert.AreEqual(new[] { true, false }, fixes.Select(fix => (bool)fix!["isPreferred"]!).ToArray());
        Assert.IsTrue(fixes.All(fix => (string?)fix!["kind"] == "quickfix" && (bool)fix["hasEdit"]! && (string?)fix["diagnostic"]!["code"] == "RDC00115"));

        Assert.IsTrue(FixIds.TryRead((string?)fixes[0]!["fixId"], out var first));
        Assert.AreEqual(new FixIdParts("Main.rdc", 2, new TextRange(new(3, 4), new(3, 11)), "quickfix", "Declare a local"), first);
    }

    [TestMethod]
    public async Task TheFixesListed_AreSaidOnePerLine_WithTheirIds()
    {
        var listed = await CallAsync(CoreAssistantTools.ListFixes, new JsonObject { ["path"] = "Main.rdc" });
        var lines = listed.Content.Single().Text.Split(Environment.NewLine);

        StringAssert.StartsWith(lines[0], "quickfix: Declare a local (preferred) fixes RDC00115 at Main.rdc:3:4");
        Assert.AreEqual("  fixId: " + listed.StructuredContent!["fixes"]![0]!["fixId"], lines[1]);
    }

    [TestMethod]
    public async Task APreview_IsTheUnifiedDiffOfEachDocumentTheFixChanges()
    {
        var fixes = await FixesAsync(new JsonObject { ["path"] = "Main.rdc" });

        var preview = await CallAsync(CoreAssistantTools.PreviewFix, new JsonObject { ["fixId"] = (string?)fixes[0]!["fixId"] });

        Assert.IsFalse(preview.IsError, preview.Content.Single().Text);
        var files = preview.StructuredContent!["files"]!.AsArray();
        Assert.AreEqual("Main.rdc", (string?)files.Single()!["path"]);
        Assert.IsNull(files[0]!["newPath"]);
        Assert.AreEqual(
            "--- Main.rdc\n+++ Main.rdc\n@@ -1,4 +1,5 @@\n 10 Dim strName As String\n+15 Dim result As Long\n 20 Dim x As Long\n-30 strName = \"a\"\n+30 result = \"a\"\n 40 Debug.Print strName, x\n",
            (string?)files[0]!["diff"]);
        Assert.IsEmpty(preview.StructuredContent["questions"]!.AsArray());
    }

    [TestMethod]
    public async Task AFixOfAnotherVersionOfTheDocument_IsNotFound_AndIsListedAgain()
    {
        var stale = FixIds.Of(new FixIdParts("Main.rdc", 1, new TextRange(new(3, 4), new(3, 11)), "quickfix", "Declare a local"));

        var preview = await CallAsync(CoreAssistantTools.PreviewFix, new JsonObject { ["fixId"] = stale });

        Assert.IsTrue(preview.IsError);
        Assert.AreEqual("The document changed after the fix was listed: list the fixes again.", preview.Content.Single().Text);
    }

    [TestMethod]
    [DataRow("Declare a global", DisplayName = "a title no fix has")]
    [DataRow(null, DisplayName = "not an id")]
    public async Task AnIdThatNamesNoFix_IsRefused(string? title)
    {
        var id = title is null ? "not-a-fix!" : FixIds.Of(new FixIdParts("Main.rdc", 2, new TextRange(new(3, 4), new(3, 11)), "quickfix", title));

        var preview = await CallAsync(CoreAssistantTools.PreviewFix, new JsonObject { ["fixId"] = id });

        Assert.IsTrue(preview.IsError, preview.Content.Single().Text);
    }

    [TestMethod]
    public async Task TheFixesOfARange_AreAlsoTheRefactoringsOfTheCodeThere_ThoseRunFromAnEditorHavingNoEdit()
    {
        // the declaration of strName, on line 1: no finding there, and the rename of the name it declares.
        var fixes = await FixesAsync(new JsonObject { ["path"] = "Main.rdc", ["line"] = 1, ["column"] = 8, ["endColumn"] = 15 });

        var rename = fixes.Single(fix => (string?)fix!["kind"] == CodeActionHandler.RefactorRename.ToString())!;
        Assert.IsFalse((bool)rename["hasEdit"]!);
        Assert.IsNull(rename["diagnostic"]);
        Assert.IsFalse(fixes.Any(fix => (string?)fix!["kind"] == "quickfix"));

        var preview = await CallAsync(CoreAssistantTools.PreviewFix, new JsonObject { ["fixId"] = (string?)rename["fixId"] });
        Assert.IsTrue(preview.IsError);
    }

    [TestMethod]
    [DataRow(0, 1, DisplayName = "line 0")]
    [DataRow(1, 0, DisplayName = "column 0")]
    public async Task APositionBefore1_IsRefused(int line, int column)
    {
        var listed = await CallAsync(CoreAssistantTools.ListFixes, new JsonObject { ["path"] = "Main.rdc", ["line"] = line, ["column"] = column });

        Assert.IsTrue(listed.IsError);
    }

    [TestMethod]
    public async Task AFixApplied_IsAppliedByTheClient_WithItsTitle_AndSaysWhatItChanged()
    {
        ClientApplies();
        var fixes = await FixesAsync(new JsonObject { ["path"] = "Main.rdc" });

        var applied = await CallAsync(CoreAssistantTools.ApplyFix, new JsonObject { ["fixId"] = (string?)fixes[0]!["fixId"] });

        Assert.IsFalse(applied.IsError, applied.Content.Single().Text);
        Assert.IsTrue((bool)applied.StructuredContent!["applied"]!);
        StringAssert.Contains((string?)applied.StructuredContent["files"]![0]!["diff"], "+15 Dim result As Long\n");
        await _client.Received(1).ApplyAsync("Declare a local", Arg.Is<WorkspaceEdit>(edit => edit.DocumentChanges!.Single().TextDocumentEdit!.Edits.Count() == 2),
            Arg.Any<CancellationToken>());
        StringAssert.StartsWith(applied.Content.Single().Text, "Applied.");
    }

    [TestMethod]
    public async Task AFixTheClientDoesNotApply_IsRefused_WithWhy()
    {
        ClientApplies();
        _applied = new ApplyWorkspaceEditResponse { Applied = false, FailureReason = "the document changed" };
        var fixes = await FixesAsync(new JsonObject { ["path"] = "Main.rdc" });

        var applied = await CallAsync(CoreAssistantTools.ApplyFix, new JsonObject { ["fixId"] = (string?)fixes[0]!["fixId"] });

        Assert.IsTrue(applied.IsError);
        Assert.AreEqual("The editor did not apply the edit: the document changed", applied.Content.Single().Text);
    }

    [TestMethod]
    public async Task AClientThatAppliesNoEdits_IsRefusedTheFix_AndToldToPreviewIt()
    {
        ClientApplies(applies: false);
        var fixes = await FixesAsync(new JsonObject { ["path"] = "Main.rdc" });

        var applied = await CallAsync(CoreAssistantTools.ApplyFix, new JsonObject { ["fixId"] = (string?)fixes[0]!["fixId"] });

        Assert.IsTrue(applied.IsError);
        StringAssert.Contains(applied.Content.Single().Text, "preview_fix");
        await _client.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default!, default);
    }

    [TestMethod]
    public async Task ARename_IsAppliedByTheClient_EverywhereTheNameIsUsed()
    {
        ClientApplies();

        // strName, declared on line 1.
        var renamed = await CallAsync(CoreAssistantTools.Rename, new JsonObject { ["path"] = "Main.rdc", ["line"] = 1, ["column"] = 8, ["newName"] = "userName" });

        Assert.IsFalse(renamed.IsError, renamed.Content.Single().Text);
        var diff = (string?)renamed.StructuredContent!["files"]!.AsArray().Single()!["diff"];
        StringAssert.Contains(diff, "+10 Dim userName As String\n");
        StringAssert.Contains(diff, "+40 Debug.Print userName, x\n");
        await _client.Received(1).ApplyAsync("Rename to userName", Arg.Any<WorkspaceEdit>(), Arg.Any<CancellationToken>());
    }

    // an edit of the program that asks whether its second edit is made.
    private static WorkspaceEdit Asking() => new()
    {
        DocumentChanges = new Container<WorkspaceEditDocumentChange>(new WorkspaceEditDocumentChange(new TextDocumentEdit
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = DocumentUri.From(Path.Combine(Root, "Main.rdc")) },
            Edits = new TextEditContainer(
                new TextEdit { Range = new Range(0, 7, 0, 14), NewText = "userName" },
                new AnnotatedTextEdit { Range = new Range(3, 15, 3, 22), NewText = "userName", AnnotationId = "inString" }),
        })),
        ChangeAnnotations = new Dictionary<ChangeAnnotationIdentifier, ChangeAnnotation>
        {
            ["inString"] = new ChangeAnnotation { Label = "Rename in strings?", NeedsConfirmation = true },
        },
    };

    [TestMethod]
    public async Task AnEditThatAsks_IsNotApplied_ByACallThatAnswersNone_AndSaysItsQuestions()
    {
        ClientApplies();
        var fixes = Fixes(new AssistantDocuments(_documents, Substitute.For<ISymbolSyncService>()), Rename());

        var result = await fixes.ApplyAsync("Rename", Asking(), accept: null, CancellationToken.None);

        Assert.IsFalse(result.Applied);
        Assert.AreEqual("inString", result.Questions.Single().Id);
        Assert.AreEqual("Main.rdc", result.Files.Single().Path);
        await _client.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default!, default);
    }

    [TestMethod]
    [DataRow(true, 2, DisplayName = "answered yes: both edits")]
    [DataRow(false, 1, DisplayName = "answered no: the other edit alone")]
    public async Task AnEditThatAsks_IsApplied_AsItsQuestionsAreAnswered(bool yes, int edits)
    {
        ClientApplies();
        var fixes = Fixes(new AssistantDocuments(_documents, Substitute.For<ISymbolSyncService>()), Rename());

        var result = await fixes.ApplyAsync("Rename", Asking(), accept: yes ? ["inString"] : [], CancellationToken.None);

        Assert.IsTrue(result.Applied);
        Assert.IsEmpty(result.Questions);
        await _client.Received(1).ApplyAsync("Rename", Arg.Is<WorkspaceEdit>(edit => edit.DocumentChanges!.Single().TextDocumentEdit!.Edits.Count() == edits
            && (edit.ChangeAnnotations == null || !edit.ChangeAnnotations.Values.Any(annotation => annotation.NeedsConfirmation == true))), Arg.Any<CancellationToken>());
    }
}
