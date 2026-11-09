using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using OmniSharp.Extensions.LanguageServer.Server;
using RDCore.LanguageServer.Diagnostics;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Refactoring;
using RDCore.LanguageServer.Services.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Client;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Progress;
using RDCore.SDK.Server.Serialization;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;
using System.IO.Pipelines;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// What a client of the platform asks to rename a name and fix a diagnostic, over the wire, as the shell asks it: the diagnostics of a document, its code actions,
/// and the rename - each answer read by the client as it is sent.
/// </summary>
[TestClass]
public sealed class RefactoringWireTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-refactoring-wire-ws");
    private const string Program = "10 Dim strName As String\r\n20 strName = \"a\"\r\n";

    // a client that says it takes a WorkspaceEdit's documentChanges is sent them, each with the version of the document its edits are of; one that does not
    // reads changes alone (LSP 3.17 §WorkspaceEdit), and is sent the same edits as changes.
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    [DataRow(true, DisplayName = "a client that takes documentChanges")]
    [DataRow(false, DisplayName = "a client that does not")]
    public async Task AClient_ReadsTheDiagnostics_TheCodeActions_AndTheRename_ItIsAnswered(bool takesDocumentChanges)
    {
        var documents = Substitute.For<IWorkspaceDocumentService>();
        var parsing = Substitute.For<IParsingClientService>();
        documents.WorkspaceRoot.Returns(Root);
        var program = new WorkspaceDocument("Main.rdc", Root, Program, version: 4);
        var uri = program.Id.Uri.ToUri();
        documents.GetAllDocuments().Returns([program]);
        parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new ModuleParser().Parse(uri, ParsingClientService.ModuleSourceOf(program))));

        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StdLibSymbolProvider.LibraryName])).ProvideSymbols()]);
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });
        var rename = new RenameService(documents, parsing, new IntrinsicSymbolResolver(), libraries, options, NullLogger<RenameService>.Instance, OneProject.Registry());

        var finding = new Diagnostic
        {
            Range = new Range(0, 7, 0, 14),
            Code = "RDC01002",
            Source = "RDCore",
            Message = "Hungarian",
            Data = new DiagnosticFixes { Rename = new RenameFix { Suggestions = ["Name"] } }.ToData(),
        };
        var diagnostics = Substitute.For<IDocumentDiagnosticsService>();
        diagnostics.GetAsync(Arg.Any<Uri>(), Arg.Any<string?>(), Arg.Any<CancellationToken>(), Arg.Any<IProgress<WorkProgressStep>?>())
            .Returns(DocumentDiagnosticsResult.Fresh(4, [finding]));

        // a request that is not answered fails the test, rather than keeping it waiting.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var toServer = new Pipe();
        var toClient = new Pipe();
        var serving = OmniSharpLanguageServer.From(server => server
            .WithInput(toServer.Reader)
            .WithOutput(toClient.Writer)
            // as the platform's servers write a pull diagnostic report (RDCoreServerApp).
            .WithSerializer(new PullDiagnosticsLspSerializer())
            .AddHandler(new DocumentDiagnosticHandler(diagnostics, Substitute.For<IDocumentLifecycleService>(), new WorkProgressReporter(), new IncomingDocumentChanges(),
                new RDCore.LanguageServer.Runtime.OpenDialogs()))
            .AddHandler(new CodeActionHandler(rename, documents, new System.IO.Abstractions.TestingHelpers.MockFileSystem()))
            // the rename handler asks the client through the server it is a handler of.
            .WithServices(services => services.AddSingleton<IRenameService>(rename))
            .WithHandler<RenameHandler>()
            .AddHandler(new PrepareRenameHandler(rename)), timeout.Token);
        var connecting = LanguageClient.From(client => client.WithInput(toClient.Reader).WithOutput(toServer.Writer)
            .WithClientCapabilities(new OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.ClientCapabilities
            {
                Workspace = new OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.WorkspaceClientCapabilities
                {
                    WorkspaceEdit = new OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.WorkspaceEditCapability { DocumentChanges = takesDocumentChanges },
                },
            }), timeout.Token);

        await Task.WhenAll(serving, connecting);
        using var server = await serving;
        using var languageClient = await connecting;
        var document = new TextDocumentIdentifier(DocumentUri.From(uri));

        var pulled = (await languageClient.SendRequest<DocumentDiagnosticPullReport?>(new DocumentDiagnosticPullParams { TextDocument = document }, timeout.Token))!.Items;
        Assert.HasCount(1, pulled);
        CollectionAssert.AreEqual(new[] { "Name" }, DiagnosticFixes.Of(pulled[0])?.Rename?.Suggestions.ToArray(), "the fix travels in the data");

        var actions = await languageClient.SendRequest(new CodeActionParams
        {
            TextDocument = document,
            Range = pulled[0].Range,
            Context = new CodeActionContext { Diagnostics = new Container<Diagnostic>(pulled) },
        }, timeout.Token);
        var titles = actions!.Select(action => action.CodeAction!.Title).ToArray();
        CollectionAssert.AreEqual(new[] { RefactoringTitles.RenameTo("strName", "Name"), RefactoringTitles.Rename("strName") }, titles);
        AssertEditsOf(actions!.First().CodeAction!.Edit!, takesDocumentChanges, document.Uri, expected: 2);

        var prepared = await languageClient.SendRequest(new PrepareRenameParams { TextDocument = document, Position = new Position(1, 5) }, timeout.Token);
        Assert.AreEqual("strName", prepared?.PlaceholderRange?.Placeholder);

        var renamed = await languageClient.SendRequest(new RenameParams { TextDocument = document, Position = new Position(1, 5), NewName = "Title" }, timeout.Token);
        AssertEditsOf(renamed!, takesDocumentChanges, document.Uri, expected: 2);

        var refused = await Assert.ThrowsAsync<OmniSharp.Extensions.JsonRpc.Server.JsonRpcException>(
            () => languageClient.SendRequest(new RenameParams { TextDocument = document, Position = new Position(1, 5), NewName = "Next" }, timeout.Token));
        StringAssert.Contains(refused.Message, "Next", "the message of the refusal is the client's to show");
        Assert.AreEqual(-32803, refused.ErrorCode, "RequestFailed: an answer, which a client does not log as a fault");
    }

    // a procedure a library calls by its name in a string is renamed there too if the user confirms it: a client that takes change annotations is sent the edit
    // annotated, to ask its user itself; one that does not is asked by the server, and sent what the user chose - or nothing, when they chose nothing. A client
    // with no user to ask is sent the rest of the rename.
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    [DataRow("rename", 2, DisplayName = "a client that can't ask, whose user renames the strings too")]
    [DataRow("leave", 1, DisplayName = "a client that can't ask, whose user leaves them")]
    [DataRow(null, 0, DisplayName = "a client that can't ask, whose user chooses nothing")]
    [DataRow("annotations", 2, DisplayName = "a client that takes change annotations")]
    [DataRow("unattended", 1, DisplayName = "an unattended client, which takes change annotations and has no user to ask")]
    public async Task AProcedureCalledByItsNameInAString_IsRenamedThere_AsTheUserConfirms(string? answer, int expected)
    {
        const string source = "Public Sub Foo()\r\nEnd Sub\r\n\r\nPublic Sub Bar()\r\n    Dim o As Object\r\n    CallByName o, \"Foo\", VbMethod\r\nEnd Sub\r\n";
        var documents = Substitute.For<IWorkspaceDocumentService>();
        var parsing = Substitute.For<IParsingClientService>();
        documents.WorkspaceRoot.Returns(Root);
        var module = new WorkspaceDocument("Module1.bas", Root, source, version: 4);
        var uri = module.Id.Uri.ToUri();
        documents.GetAllDocuments().Returns([module]);
        parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new ModuleParser().Parse(uri, ParsingClientService.ModuleSourceOf(module))));
        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StdLibSymbolProvider.LibraryName])).ProvideSymbols()]);
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });
        var rename = new RenameService(documents, parsing, new IntrinsicSymbolResolver(), libraries, options, NullLogger<RenameService>.Instance, OneProject.Registry());
        var expectations = Substitute.For<IPlatformClientCapabilitiesService>();
        expectations.Expects(Arg.Any<Func<LanguageServerCapabilities, CorePlatformClientCapability>>()).Returns(answer == "unattended");
        var takesAnnotations = answer is "annotations" or "unattended";

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var toServer = new Pipe();
        var toClient = new Pipe();
        var asked = new List<ShowMessageRequestParams>();
        var serving = OmniSharpLanguageServer.From(server => server
            .WithInput(toServer.Reader)
            .WithOutput(toClient.Writer)
            // as the platform's servers write a change annotation (RDCoreServerApp).
            .WithSerializer(new PullDiagnosticsLspSerializer())
            .WithServices(services => services.AddSingleton<IRenameService>(rename).AddSingleton(expectations))
            .WithHandler<RenameHandler>(), timeout.Token);
        var connecting = LanguageClient.From(client => client.WithInput(toClient.Reader).WithOutput(toServer.Writer)
            .WithClientCapabilities(new OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.ClientCapabilities
            {
                Workspace = new OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.WorkspaceClientCapabilities
                {
                    WorkspaceEdit = new OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.WorkspaceEditCapability
                    {
                        DocumentChanges = true,
                        ChangeAnnotationSupport = takesAnnotations
                            ? new OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.WorkspaceEditSupportCapabilitiesChangeAnnotationSupport()
                            : null,
                    },
                },
            })
            .OnShowMessageRequest(request =>
            {
                asked.Add(request);
                var title = answer switch { "rename" => RefactoringTitles.RenameInStringRename(), "leave" => RefactoringTitles.RenameInStringLeave(), _ => null };
                return Task.FromResult(title is null ? null! : new MessageActionItem { Title = title });
            }), timeout.Token);

        await Task.WhenAll(serving, connecting);
        using var server = await serving;
        using var languageClient = await connecting;
        var document = new TextDocumentIdentifier(DocumentUri.From(uri));

        var renamed = await languageClient.SendRequest(new RenameParams { TextDocument = document, Position = new Position(0, 12), NewName = "Baz" }, timeout.Token);

        Assert.HasCount(takesAnnotations ? 0 : 1, asked, "the server asks a client that can't ask its user, and no other");
        if (expected == 0)
        {
            Assert.IsNull(renamed, "nothing is renamed when the user does not choose");
            return;
        }

        var edits = renamed!.DocumentChanges!.Single().TextDocumentEdit!.Edits.ToList();
        Assert.HasCount(expected, edits);
        Assert.IsTrue(edits.All(edit => edit.NewText == "Baz"));
        if (answer == "unattended")
        {
            Assert.IsFalse(edits.Any(edit => edit is AnnotatedTextEdit), "the string's edit is not sent");
            Assert.IsNull(renamed.ChangeAnnotations);
        }
        else if (answer == "annotations")
        {
            var annotated = Assert.IsInstanceOfType<AnnotatedTextEdit>(edits[1], "the string's edit is annotated");
            var annotation = renamed.ChangeAnnotations!.Single();
            Assert.AreEqual(RenameWorkspace.InString.Identifier, annotation.Key.Identifier);
            Assert.AreEqual(annotation.Key.Identifier, annotated.AnnotationId.Identifier, "the edit names the annotation");
            Assert.IsTrue(annotation.Value.NeedsConfirmation, "the client asks its user");
        }
        else
        {
            StringAssert.Contains(asked[0].Message, "Foo");
            Assert.IsFalse(edits.Any(edit => edit is AnnotatedTextEdit), "a client that can't ask is sent no annotation");
        }
    }

    // a member of a class that may be what a late-bound access calls is renamed there too if the user confirms it, as a string is, an access the code backs
    // apart from one that is only possible; a client that can't ask its user is sent the rest of the rename, and none of the late-bound edits.
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    [DataRow("rename", 3, DisplayName = "a client that can't ask, whose user renames the late-bound accesses too")]
    [DataRow("leave", 1, DisplayName = "a client that can't ask, whose user leaves them")]
    [DataRow(null, 0, DisplayName = "a client that can't ask, whose user chooses nothing")]
    [DataRow("annotations", 3, DisplayName = "a client that takes change annotations")]
    [DataRow("unattended", 1, DisplayName = "an unattended client, which takes change annotations and has no user to ask")]
    public async Task AMemberAccessedLateBound_IsRenamedThere_AsTheUserConfirms_AndNotWhereNoUserCan(string? answer, int expected)
    {
        const string widget = "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\nAttribute VB_Name = \"Widget\"\r\nPublic Sub Spin()\r\nEnd Sub\r\n";
        const string main = "Attribute VB_Name = \"Module1\"\r\nPublic Sub Bar(ByVal o As Object)\r\n    o.Spin\r\n    Dim w As Object\r\n    Set w = New Widget\r\n    w.Spin\r\nEnd Sub\r\n";
        var documents = Substitute.For<IWorkspaceDocumentService>();
        var parsing = Substitute.For<IParsingClientService>();
        documents.WorkspaceRoot.Returns(Root);
        var classModule = new WorkspaceDocument("Widget.cls", Root, widget, version: 4);
        var module = new WorkspaceDocument("Module1.bas", Root, main, version: 4);
        documents.GetAllDocuments().Returns([classModule, module]);
        foreach (var each in new[] { classModule, module })
        {
            var eachUri = each.Id.Uri.ToUri();
            parsing.ParseDocumentAsync(eachUri, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ModuleParser().Parse(eachUri, ParsingClientService.ModuleSourceOf(each))));
        }

        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StdLibSymbolProvider.LibraryName])).ProvideSymbols()]);
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });
        var rename = new RenameService(documents, parsing, new IntrinsicSymbolResolver(), libraries, options, NullLogger<RenameService>.Instance, OneProject.Registry());
        var expectations = Substitute.For<IPlatformClientCapabilitiesService>();
        expectations.Expects(Arg.Any<Func<LanguageServerCapabilities, CorePlatformClientCapability>>()).Returns(answer == "unattended");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var toServer = new Pipe();
        var toClient = new Pipe();
        var asked = new List<ShowMessageRequestParams>();
        var serving = OmniSharpLanguageServer.From(server => server
            .WithInput(toServer.Reader)
            .WithOutput(toClient.Writer)
            .WithSerializer(new PullDiagnosticsLspSerializer())
            .WithServices(services => services.AddSingleton<IRenameService>(rename).AddSingleton(expectations))
            .WithHandler<RenameHandler>(), timeout.Token);
        var takesAnnotations = answer is "annotations" or "unattended";
        var connecting = LanguageClient.From(client => client.WithInput(toClient.Reader).WithOutput(toServer.Writer)
            .WithClientCapabilities(new OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.ClientCapabilities
            {
                Workspace = new OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.WorkspaceClientCapabilities
                {
                    WorkspaceEdit = new OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.WorkspaceEditCapability
                    {
                        DocumentChanges = true,
                        ChangeAnnotationSupport = takesAnnotations
                            ? new OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.WorkspaceEditSupportCapabilitiesChangeAnnotationSupport()
                            : null,
                    },
                },
            })
            .OnShowMessageRequest(request =>
            {
                asked.Add(request);
                var title = answer switch { "rename" => RefactoringTitles.RenameLateBoundRename(), "leave" => RefactoringTitles.RenameLateBoundLeave(), _ => null };
                return Task.FromResult(title is null ? null! : new MessageActionItem { Title = title });
            }), timeout.Token);

        await Task.WhenAll(serving, connecting);
        using var server = await serving;
        using var languageClient = await connecting;
        var document = new TextDocumentIdentifier(classModule.Id.Uri);

        var renamed = await languageClient.SendRequest(new RenameParams { TextDocument = document, Position = new Position(5, 12), NewName = "Turn" }, timeout.Token);

        Assert.HasCount(takesAnnotations ? 0 : answer is null ? 1 : 2, asked, "the server asks a client that can't ask its user, once for each annotation, and no other");
        if (expected == 0)
        {
            Assert.IsNull(renamed, "nothing is renamed when the user does not choose");
            return;
        }

        var edits = renamed!.DocumentChanges!.SelectMany(change => change.TextDocumentEdit!.Edits).ToList();
        Assert.HasCount(expected, edits);
        Assert.IsTrue(edits.All(edit => edit.NewText == "Turn"));
        switch (answer)
        {
            case "annotations":
                var annotated = edits.OfType<AnnotatedTextEdit>().ToDictionary(edit => edit.Range.Start.Line, edit => edit.AnnotationId.Identifier);
                Assert.AreEqual(RenameWorkspace.LateBound.Identifier, annotated[2], "nothing says what the parameter holds");
                Assert.AreEqual(RenameWorkspace.LateBoundBacked.Identifier, annotated[5], "the code says w holds a Widget");
                var annotations = renamed.ChangeAnnotations!;
                Assert.HasCount(2, annotations);
                Assert.IsTrue(annotations.Values.All(annotation => annotation.NeedsConfirmation == true), "the client asks its user");
                Assert.AreEqual(RefactoringTitles.RenameLateBoundLabel("Widget", isBacked: true),
                    annotations.Single(annotation => annotation.Key.Identifier == RenameWorkspace.LateBoundBacked.Identifier).Value.Label);
                Assert.AreEqual(RefactoringTitles.RenameLateBoundLabel("Widget", isBacked: false),
                    annotations.Single(annotation => annotation.Key.Identifier == RenameWorkspace.LateBound.Identifier).Value.Label);
                break;
            case "unattended":
                Assert.IsFalse(edits.Any(edit => edit is AnnotatedTextEdit), "the late-bound edit is not sent");
                Assert.IsNull(renamed.ChangeAnnotations);
                break;
            default:
                StringAssert.Contains(asked[0].Message, "Spin");
                StringAssert.Contains(asked[0].Message, "Widget");
                Assert.IsFalse(edits.Any(edit => edit is AnnotatedTextEdit), "a client that can't ask is sent no annotation");
                break;
        }
    }

    // the edits of the document, in the form the client takes: documentChanges with the version they are edits of, or changes, and not the other.
    private static void AssertEditsOf(WorkspaceEdit edit, bool takesDocumentChanges, DocumentUri document, int expected)
    {
        if (takesDocumentChanges)
        {
            var change = edit.DocumentChanges!.Single().TextDocumentEdit!;
            Assert.AreEqual(4, change.TextDocument.Version);
            Assert.HasCount(expected, change.Edits);
            Assert.IsNull(edit.Changes);
        }
        else
        {
            Assert.IsNull(edit.DocumentChanges, "a client that does not take documentChanges reads changes alone");
            var (uri, edits) = edit.Changes!.Single();
            Assert.AreEqual(document, uri);
            Assert.HasCount(expected, edits);
        }
    }
}
