using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
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

    private AssistantToolCatalog Sut()
    {
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });
        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StandardLibrary.LibraryName])).ProvideSymbols()]);
        var rename = new RenameService(_documents, _parsing, new IntrinsicSymbolResolver(), libraries, options, NullLogger<RenameService>.Instance, OneProject.Registry());
        var actions = new CodeActionService(rename, _documents, new System.IO.Abstractions.TestingHelpers.MockFileSystem(), new ExtractMethodService(rename, _parsing, options),
            new MoveMemberService(rename, _parsing), Substitute.For<RDCore.SDK.Server.Services.IPlatformClientCapabilitiesService>(),
            new OmniSharp.Extensions.LanguageServer.Protocol.Serialization.LspSerializer());
        var documents = new AssistantDocuments(_documents, Substitute.For<ISymbolSyncService>());
        var fixes = new FixToolEdits(documents, _diagnostics, Substitute.For<IDocumentLifecycleService>(), actions, _documents);
        return new AssistantToolCatalog([new ListFixesTool(documents, fixes), new PreviewFixTool(fixes)]);
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

        var rename = fixes.Single(fix => (string?)fix!["kind"] == CodeActionHandler.RefactorRename.Name)!;
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
}
