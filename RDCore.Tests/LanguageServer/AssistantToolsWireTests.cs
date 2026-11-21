using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Client;
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
using RDCore.SDK.Client;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics;
using RDCore.SDK.Server.Assistant;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;
using System.IO.Pipelines;
using System.Text.Json.Nodes;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// <c>rdcore/tools/list</c> and <c>rdcore/tools/call</c> over the wire, as a client of an assistant asks them: the read tools answer from the workspace with
/// paths from its root and 1-based positions, a call the tool cannot make is a refusal the assistant reads, and a client that did not advertise the tools is
/// not served them.
/// </summary>
[TestClass]
public sealed class AssistantToolsWireTests
{
    // a cap on a hang, not a budget: each wait is on the answer itself, which a loaded machine gives as late as it does.
    private static readonly TimeSpan SafetyCap = TimeSpan.FromMinutes(5);

    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-assistant-tools-wire-ws");

    private const string Helpers = """
        Attribute VB_Name = "Helpers"
        Public strTitle As String

        Public Sub Show()
            Debug.Print strTitle
        End Sub
        """;

    private const string Module1 = """
        Attribute VB_Name = "Module1"
        Public Sub Main()
            strTitle = "Hello"
            Show
        End Sub
        """;

    private static Uri Document(string fileName) => DocumentUri.From(Path.Combine(Root, fileName)).ToUri();

    private static readonly Uri Title = SymbolUri.Member(SymbolUri.Module(Root, "Helpers"), "strTitle");

    // the workspace's first load as the test holds it: the server holds no document until the load is released, and the tools wait on it.
    private sealed class HeldLoad
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _waitedOn = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static HeldLoad Released()
        {
            var load = new HeldLoad();
            load.Release();
            return load;
        }

        public bool IsReleased => _released.Task.IsCompleted;

        // a tool is waiting on the load.
        public Task WaitedOn => _waitedOn.Task;

        public Task WaitAsync(CancellationToken token)
        {
            _waitedOn.TrySetResult();
            return _released.Task.WaitAsync(token);
        }

        public void Release() => _released.TrySetResult();
    }

    // the tools, and what answers rdcore/semantics/describe and /outline, over the two documents, once the workspace is loaded.
    private static (AssistantToolCatalog Catalog, ISemanticsDescriptionService Semantics) Serve(HeldLoad load, TimeSpan loadBound)
    {
        var documents = Substitute.For<IWorkspaceDocumentService>();
        var parsing = Substitute.For<IParsingClientService>();
        documents.WorkspaceRoot.Returns(Root);
        var loaded = new[] { new WorkspaceDocument("Module1.bas", Root, Module1, version: 1), new WorkspaceDocument("Helpers.bas", Root, Helpers, version: 1) };
        WorkspaceDocument[] All() => load.IsReleased ? loaded : [];
        documents.GetAllDocuments().Returns(_ => All());
        documents.TryGetDocument(Arg.Any<Uri>(), out Arg.Any<WorkspaceDocument>()).Returns(call =>
        {
            var found = All().FirstOrDefault(document => document.Id.Uri.ToUri() == call.ArgAt<Uri>(0));
            call[1] = found!;
            return found is not null;
        });
        documents.TryGetModule(Arg.Any<Uri>(), out Arg.Any<WorkspaceDocument>()).Returns(call =>
        {
            var found = All().FirstOrDefault(document => document.Id.Uri.ToUri() == call.ArgAt<Uri>(0));
            call[1] = found!;
            return found is not null;
        });
        foreach (var document in loaded)
        {
            var uri = document.Id.Uri.ToUri();
            parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ModuleParser().Parse(uri, ParsingClientService.ModuleSourceOf(document))));
        }

        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StandardLibrary.LibraryName])).ProvideSymbols()]);
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });
        var workspace = new RenameService(documents, parsing, new IntrinsicSymbolResolver(), libraries, options, NullLogger<RenameService>.Instance, OneProject.Registry());

        // one finding in Module1, none in Helpers.
        var diagnostics = Substitute.For<IDocumentDiagnosticsService>();
        diagnostics.GetAsync(Arg.Any<Uri>(), Arg.Any<string?>(), Arg.Any<CancellationToken>(), Arg.Any<IProgress<RDCore.SDK.Server.Progress.WorkProgressStep>?>())
            .Returns(new DocumentDiagnosticsResult("1", false, []));
        diagnostics.GetAsync(Document("Module1.bas"), Arg.Any<string?>(), Arg.Any<CancellationToken>(), Arg.Any<IProgress<RDCore.SDK.Server.Progress.WorkProgressStep>?>())
            .Returns(new DocumentDiagnosticsResult("1", false,
            [
                new Diagnostic { Range = new Range(2, 4, 2, 12), Code = "RDC01011", Severity = DiagnosticSeverity.Warning, Message = "m" },
            ]));

        // the host's static pass: strTitle is read once and written once, and is the String variable written at Module1 3:5.
        diagnostics.SemanticsAsync(Document("Helpers.bas"), Arg.Any<CancellationToken>()).Returns(new ModuleSemanticsDto(
            SymbolUri.Module(Root, "Helpers"), true, [], [],
            [new DeclarationFactDto(Title, "strTitle", DeclarationKind.Variable, AccessModifier.Public, false,
                new SourceLocation(Document("Helpers.bas"), new SourceRange(1, 7, 1, 15)), new DeclarationReferences(1, 1, 0))],
            Language: "vba"));
        diagnostics.SemanticsAsync(Document("Module1.bas"), Arg.Any<CancellationToken>()).Returns(new ModuleSemanticsDto(
            SymbolUri.Module(Root, "Module1"), true, [],
            [new ProcedureSemanticsDto(SymbolUri.Member(SymbolUri.Module(Root, "Module1"), "Main"), true, [],
                [new ExpressionFactDto(new SyntaxNodeId(Document("Module1.bas").ToString(), [0, 1]), new SourceLocation(Document("Module1.bas"), new SourceRange(2, 4, 2, 12)),
                    "String", ExpressionClassification.Variable, Title, default, null)])],
            []));

        var semantics = new SemanticsDescriptionService(workspace, diagnostics, documents, parsing, new IntrinsicSymbolResolver(), options);
        var symbols = Substitute.For<ISymbolSyncService>();
        symbols.WorkspaceDefinedAsync(Arg.Any<CancellationToken>()).Returns(call => load.WaitAsync(call.Arg<CancellationToken>()));
        var assistantDocuments = new AssistantDocuments(documents, symbols) { LoadBound = loadBound };
        return (new AssistantToolCatalog(
        [
            new GetDiagnosticsTool(assistantDocuments, diagnostics, Substitute.For<IDocumentLifecycleService>()),
            new FindSymbolTool(assistantDocuments, new WorkspaceSymbolSearch(documents, parsing, new IntrinsicSymbolResolver(), options)),
            new ReferencesTool(assistantDocuments, workspace),
            new DefinitionTool(assistantDocuments, workspace),
            new DescribeAtTool(assistantDocuments, semantics),
            new OutlineTool(assistantDocuments, semantics),
        ]), semantics);
    }

    private static PlatformClientCapabilitiesService Advertised(bool tools)
    {
        var capabilities = new PlatformClientCapabilitiesService();
        capabilities.Record(new PlatformInitializeParams
        {
            ExpectedComponent = CoreServerComponent.LanguageServer,
            Expected = new CorePlatformClientCapabilities { LanguageServer = new LanguageServerCapabilities { AssistantTools = new AssistantTools(tools) } },
        });
        return capabilities;
    }

    // the answer to each request, from a client that does or does not advertise the tools; to a loaded workspace, unless the load is held.
    private static async Task<TResponse> AskAsync<TResponse>(bool advertised, MediatR.IRequest<TResponse> request, HeldLoad? load = null, TimeSpan? loadBound = null)
    {
        // a request that is not answered fails the test, rather than keeping it waiting.
        using var timeout = new CancellationTokenSource(SafetyCap);
        var toServer = new Pipe();
        var toClient = new Pipe();
        var (catalog, semantics) = Serve(load ?? HeldLoad.Released(), loadBound ?? AssistantDocuments.DefaultLoadBound);
        var capabilities = Advertised(advertised);
        var serving = OmniSharpLanguageServer.From(server => server
            .WithInput(toServer.Reader)
            .WithOutput(toClient.Writer)
            .AddHandler(new ToolsListHandler(catalog, capabilities))
            .AddHandler(new ToolsCallHandler(catalog, capabilities, new RDCore.SDK.Server.IncomingDocumentChanges()))
            .AddHandler(new SemanticsDescribeHandler(semantics))
            .AddHandler(new SemanticsOutlineHandler(semantics)), timeout.Token);
        var connecting = LanguageClient.From(client => client
            .WithInput(toClient.Reader)
            .WithOutput(toServer.Writer), timeout.Token);

        await Task.WhenAll(serving, connecting);
        using var server = await serving;
        using var languageClient = await connecting;
        return await languageClient.SendRequest(request, timeout.Token);
    }

    private static Task<ToolCallReply> CallAsync(string tool, JsonObject? arguments, HeldLoad? load = null, TimeSpan? loadBound = null)
        => AskAsync<ToolCallReply>(advertised: true, new ToolCallRequest(new ToolCallParams { Name = tool, Arguments = arguments }), load, loadBound);

    private static JsonObject At(string path, int line, int column) => new() { ["path"] = path, ["line"] = line, ["column"] = column };

    private static string[] LocationsOf(ToolCallReply reply)
        => [.. reply.StructuredContent!["locations"]!.AsArray().Select(location => $"{location!["path"]}:{location["range"]!["start"]!["line"]}:{location["range"]!["start"]!["column"]}")];

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task AClientThatAdvertisesTheTools_IsListedThem_EachWithItsSchemaAndHints()
    {
        var listed = await AskAsync<ToolsListReply>(advertised: true, new ToolsListRequest());

        CollectionAssert.AreEqual(
            new[] { CoreAssistantTools.GetDiagnostics, CoreAssistantTools.FindSymbol, CoreAssistantTools.References, CoreAssistantTools.Definition, CoreAssistantTools.DescribeAt, CoreAssistantTools.Outline },
            listed.Tools.Select(tool => tool.Name).ToArray());
        foreach (var tool in listed.Tools)
        {
            Assert.AreEqual("object", (string?)tool.InputSchema["type"], tool.Name);
            Assert.IsNotNull(tool.OutputSchema, tool.Name);
            Assert.IsFalse(string.IsNullOrWhiteSpace(tool.Description), tool.Name);
            Assert.IsTrue(tool.Annotations.ReadOnlyHint && tool.Annotations.IdempotentHint && !tool.Annotations.DestructiveHint, tool.Name);
        }

        var references = listed.Tools.Single(tool => tool.Name == CoreAssistantTools.References);
        CollectionAssert.AreEquivalent(new[] { "path", "line", "column" }, references.InputSchema["required"]!.AsArray().Select(name => (string?)name).ToArray());
        Assert.IsNotNull(references.InputSchema["properties"]!["includeDeclaration"]!["description"]);
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task AClientThatDidNotAdvertiseTheTools_IsNotServedThem()
    {
        await Assert.ThrowsAsync<Exception>(() => AskAsync<ToolsListReply>(advertised: false, new ToolsListRequest()));
        await Assert.ThrowsAsync<Exception>(() => AskAsync<ToolCallReply>(advertised: false, new ToolCallRequest(new ToolCallParams { Name = CoreAssistantTools.GetDiagnostics })));
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task Definition_SaysWhereTheNameAtAPositionIsDeclared_FromTheWorkspaceRoot_OneBased()
    {
        // line 3 of Module1: "    strTitle = ..."
        var reply = await CallAsync(CoreAssistantTools.Definition, At("Module1.bas", 3, 7));

        Assert.IsFalse(reply.IsError, reply.Content.Single().Text);
        CollectionAssert.AreEqual(new[] { "Helpers.bas:2:8" }, LocationsOf(reply));
        Assert.AreEqual("Helpers.bas:2:8", reply.Content.Single().Text);
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task References_ListEveryNameThatRefersToTheSymbol_AndItsDeclarationUnlessLeftOut()
    {
        var all = await CallAsync(CoreAssistantTools.References, At("Module1.bas", 3, 5));
        var withoutDeclaration = At("Module1.bas", 3, 5);
        withoutDeclaration["includeDeclaration"] = false;
        var uses = await CallAsync(CoreAssistantTools.References, withoutDeclaration);

        CollectionAssert.AreEquivalent(new[] { "Helpers.bas:2:8", "Helpers.bas:5:17", "Module1.bas:3:5" }, LocationsOf(all));
        Assert.AreEqual(3, (int)all.StructuredContent!["total"]!);
        CollectionAssert.AreEquivalent(new[] { "Helpers.bas:5:17", "Module1.bas:3:5" }, LocationsOf(uses));
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task FindSymbol_FindsTheDeclarationsWhoseNameMatchesTheQuery()
    {
        var reply = await CallAsync(CoreAssistantTools.FindSymbol, new JsonObject { ["query"] = "shw" });

        var first = reply.StructuredContent!["symbols"]!.AsArray()[0]!;
        Assert.AreEqual("Show", (string?)first["name"]);
        Assert.AreEqual("Helpers.bas", (string?)first["location"]!["path"]);
        Assert.Contains("Helpers.bas:4:", reply.Content.Single().Text);
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task AToolCalledWhileTheWorkspaceLoads_AnswersOnceItIsLoaded_FromWhatItLoaded()
    {
        var load = new HeldLoad();
        var calling = CallAsync(CoreAssistantTools.FindSymbol, new JsonObject { ["query"] = "shw" }, load);

        await load.WaitedOn.WaitAsync(SafetyCap);
        Assert.IsFalse(calling.IsCompleted, "a workspace that is loading has no answer yet");
        load.Release();
        var reply = await calling;

        Assert.IsFalse(reply.IsError, reply.Content.Single().Text);
        Assert.AreEqual("Show", (string?)reply.StructuredContent!["symbols"]!.AsArray()[0]!["name"]);
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    [DataRow(CoreAssistantTools.GetDiagnostics, """{}""")]
    [DataRow(CoreAssistantTools.FindSymbol, """{"query":"shw"}""")]
    [DataRow(CoreAssistantTools.References, """{"path":"Module1.bas","line":3,"column":5}""")]
    [DataRow(CoreAssistantTools.Definition, """{"path":"Module1.bas","line":3,"column":5}""")]
    [DataRow(CoreAssistantTools.DescribeAt, """{"path":"Module1.bas","line":3,"column":5}""")]
    [DataRow(CoreAssistantTools.Outline, """{"path":"Helpers.bas"}""")]
    public async Task AToolWhoseWaitForTheLoadRunsOut_SaysTheWorkspaceIsStillLoading(string tool, string arguments)
    {
        // a bound of zero runs out on a load that is held.
        var reply = await CallAsync(tool, JsonNode.Parse(arguments)!.AsObject(), new HeldLoad(), TimeSpan.Zero);

        Assert.IsTrue(reply.IsError, tool);
        Assert.AreEqual(AssistantToolTexts.WorkspaceLoading, reply.Content.Single().Text, tool);
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task GetDiagnostics_ListsTheFindingsOfTheWorkspace_OrOfOneDocument()
    {
        var workspace = await CallAsync(CoreAssistantTools.GetDiagnostics, null);
        var helpers = await CallAsync(CoreAssistantTools.GetDiagnostics, new JsonObject { ["path"] = "Helpers.bas" });

        Assert.AreEqual(1, (int)workspace.StructuredContent!["total"]!);
        var finding = workspace.StructuredContent["diagnostics"]!.AsArray().Single()!;
        Assert.AreEqual("Module1.bas", (string?)finding["location"]!["path"]);
        Assert.AreEqual("Warning", (string?)finding["severity"]);
        Assert.IsFalse((bool)finding["hasFix"]!);
        Assert.AreEqual("Module1.bas:3:5 RDC01011 Warning: m", workspace.Content.Single().Text);
        Assert.AreEqual(0, (int)helpers.StructuredContent!["total"]!);
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task ACallTheToolCannotMake_IsARefusalThatSaysWhy()
    {
        var nowhere = await CallAsync(CoreAssistantTools.Definition, At("Nowhere.bas", 1, 1));
        var notAPosition = await CallAsync(CoreAssistantTools.Definition, At("Module1.bas", 0, 1));
        var notALine = await CallAsync(CoreAssistantTools.Definition, new JsonObject { ["path"] = "Module1.bas", ["line"] = "three", ["column"] = 1 });
        var unknownArgument = await CallAsync(CoreAssistantTools.FindSymbol, new JsonObject { ["query"] = "shw", ["limit"] = 3 });

        Assert.IsTrue(nowhere.IsError);
        Assert.Contains("Nowhere.bas", nowhere.Content.Single().Text);
        Assert.IsNull(nowhere.StructuredContent);
        Assert.IsTrue(notAPosition.IsError);
        Assert.IsTrue(notALine.IsError);
        Assert.IsTrue(unknownArgument.IsError);
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task Describe_SaysWhatTheNameRefersTo_WithTheHostsFacts_AndTheExpressionWrittenThere()
    {
        var described = await AskAsync<DescribeResult>(advertised: false, new SemanticsDescribeRequest { Uri = Document("Module1.bas"), Position = new SourcePosition(2, 6) });

        var symbol = described.Symbol!;
        Assert.AreEqual("strTitle", symbol.Name);
        Assert.AreEqual(DescribedSymbolKind.Variable, symbol.Kind);
        Assert.AreEqual(DescribedScope.Module, symbol.Scope);
        Assert.AreEqual("String", symbol.DeclaredType);
        Assert.AreEqual(AccessModifier.Public, symbol.Access);
        Assert.AreEqual(Document("Helpers.bas"), symbol.Location?.Uri);
        Assert.AreEqual(new SourcePosition(1, 7), symbol.Location?.Range.Start);
        Assert.AreEqual(new DeclarationReferences(1, 1, 0), symbol.References);
        Assert.IsFalse(symbol.IsImplicit);

        Assert.AreEqual(ExpressionClassification.Variable, described.Expression?.Classification);
        Assert.AreEqual(new SourceRange(2, 4, 2, 12), described.Expression?.Range);
        Assert.AreEqual(new SourceRange(2, 4, 2, 12), described.Range);
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task DescribeAt_AnswersTheSameFacts_OneBased_FromTheWorkspaceRoot()
    {
        var reply = await CallAsync(CoreAssistantTools.DescribeAt, At("Module1.bas", 3, 7));

        var symbol = reply.StructuredContent!["symbol"]!;
        Assert.AreEqual("strTitle", (string?)symbol["name"]);
        Assert.AreEqual("Variable", (string?)symbol["kind"]);
        Assert.AreEqual("Helpers.bas", (string?)symbol["location"]!["path"]);
        Assert.AreEqual(2, (int?)symbol["location"]!["range"]!["start"]!["line"]);
        Assert.AreEqual(5, (int?)reply.StructuredContent["expression"]!["range"]!["start"]!["column"]);
        StringAssert.Contains(reply.Content.Single().Text, "Public strTitle As String");
        StringAssert.Contains(reply.Content.Single().Text, "Helpers.bas:2:8");
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task DescribeAt_WhereNothingIsWritten_SaysSo()
    {
        var reply = await CallAsync(CoreAssistantTools.DescribeAt, At("Helpers.bas", 3, 1));

        Assert.IsFalse(reply.IsError);
        Assert.IsNull(reply.StructuredContent!["symbol"]);
        Assert.AreEqual(AssistantToolTexts.NothingDescribed, reply.Content.Single().Text);
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task Outline_ListsWhatTheModuleDeclares_InOrder_WithTheHostsModuleFacts()
    {
        var outline = await AskAsync<ModuleOutline>(advertised: false, new SemanticsOutlineRequest { Uri = Document("Helpers.bas") });

        Assert.AreEqual("Helpers", outline.Name);
        Assert.AreEqual("vba", outline.Language);
        Assert.IsTrue(outline.OptionExplicit);
        CollectionAssert.AreEqual(new[] { "strTitle", "Show" }, outline.Entries.Select(entry => entry.Name).ToArray());
        Assert.AreEqual(DescribedSymbolKind.Procedure, outline.Entries[1].Kind);
        Assert.AreEqual(new SourcePosition(3, 11), outline.Entries[1].SelectionRange.Start);
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task OutlineTool_ListsTheEntries_OneBased()
    {
        var reply = await CallAsync(CoreAssistantTools.Outline, new JsonObject { ["path"] = "Helpers.bas" });

        Assert.AreEqual("Helpers.bas", (string?)reply.StructuredContent!["path"]);
        Assert.AreEqual(2, (int?)reply.StructuredContent["total"]);
        Assert.AreEqual("2:8 Public strTitle As String" + Environment.NewLine + "4:12 Public Sub Show()", reply.Content.Single().Text);
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task ACallOfAToolTheServerDoesNotHave_IsAnError()
        => await Assert.ThrowsAsync<Exception>(() => CallAsync("run_program", null));
}
