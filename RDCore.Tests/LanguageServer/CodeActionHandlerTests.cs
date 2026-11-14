using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Refactoring;
using RDCore.LanguageServer.Services.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;
using System.Globalization;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The code actions of a range of a document: the renames that fix its naming diagnostics, and a rename of each name it declares.
/// </summary>
[TestClass]
public sealed class CodeActionHandlerTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-codeaction-ws");

    private const string Program = "10 Dim strName As String\r\n20 Dim x As Long\r\n30 strName = \"a\"\r\n40 Debug.Print strName, x\r\n";

    // a module the shell has loaded: its statements can be extracted, which the shell's own program's cannot.
    private const string Module = "Attribute VB_Name = \"Module1\"\r\nPublic Sub Main()\r\n    Dim strName As String\r\n    Dim x As Long\r\n    strName = \"a\"\r\n    Debug.Print strName, x\r\nEnd Sub\r\n";

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();

    // what the client said it can do, as it initialized; none, a client that takes neither documentChanges nor change annotations.
    private ClientCapabilities? _client;

    public CodeActionHandlerTests()
        => Hold(new WorkspaceDocument("Main.rdc", Root, Program, version: 2), new WorkspaceDocument("Module1.bas", Root, Module, version: 1));

    // the documents of the workspace: the program and a module, or the modules a test holds instead.
    private void Hold(params WorkspaceDocument[] documents)
    {
        _documents.WorkspaceRoot.Returns(Root);
        _documents.GetAllDocuments().Returns(documents);
        foreach (var document in documents)
        {
            var uri = document.Id.Uri.ToUri();
            _documents.TryGetDocument(uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = document; return true; });
            _parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new ModuleParser().Parse(uri, ParsingClientService.ModuleSourceOf(document))));
        }

        // an extraction offered with its edit is verified on its text, parsed again.
        _parsing.ParseFragmentAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ModuleParser().Parse(call.Arg<Uri>(), call.Arg<string>())));
    }

    private CodeActionHandler Sut()
    {
        var handler = Handler();
        if (_client is not null)
        {
            ((IRegistration<CodeActionRegistrationOptions, CodeActionCapability>)handler)
                .GetRegistrationOptions(new CodeActionCapability(), _client);
        }

        return handler;
    }

    private CodeActionHandler Handler()
    {
        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StandardLibrary.LibraryName])).ProvideSymbols()]);
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });
        var rename = new RenameService(_documents, _parsing, new IntrinsicSymbolResolver(), libraries, options, NullLogger<RenameService>.Instance, OneProject.Registry());
        return new CodeActionHandler(rename, _documents, new System.IO.Abstractions.TestingHelpers.MockFileSystem(), new ExtractMethodService(rename, _parsing, options),
            Substitute.For<RDCore.SDK.Server.Services.IPlatformClientCapabilitiesService>(), new OmniSharp.Extensions.LanguageServer.Protocol.Serialization.LspSerializer());
    }

    private static DocumentUri Document => DocumentUri.From(Path.Combine(Root, "Main.rdc"));

    private static Diagnostic Finding(Range range, params string[] suggestions) => new()
    {
        Range = range,
        Code = "RDC01002",
        Source = "RDCore",
        Message = "Hungarian",
        Data = new DiagnosticFixes { Rename = new RenameFix { Suggestions = suggestions } }.ToData(),
    };

    private Task<List<CodeAction>> ActionsAsync(Range range, CodeActionKind[]? only = null, params Diagnostic[] diagnostics)
        => ActionsAsync(Document, range, only, diagnostics);

    private async Task<List<CodeAction>> ActionsAsync(DocumentUri document, Range range, CodeActionKind[]? only, params Diagnostic[] diagnostics)
    {
        var culture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        try
        {
            var answer = await Sut().Handle(new CodeActionParams
            {
                TextDocument = new TextDocumentIdentifier(document),
                Range = range,
                Context = new CodeActionContext
                {
                    Diagnostics = new Container<Diagnostic>(diagnostics),
                    Only = only is null ? null : new Container<CodeActionKind>(only),
                },
            }, CancellationToken.None);
            return [.. answer!.Select(item => item.CodeAction!)];
        }
        finally
        {
            CultureInfo.CurrentUICulture = culture;
        }
    }

    [TestMethod]
    public async Task ADiagnosticFixedByARename_IsOfferedTheRenameToEachSuggestion_AndToANameTheUserChooses()
    {
        var diagnostic = Finding(new Range(0, 7, 0, 14), "Name");

        var actions = await ActionsAsync(new Range(0, 7, 0, 14), [CodeActionKind.QuickFix], diagnostic);

        Assert.HasCount(2, actions);
        Assert.AreEqual("Rename 'strName' to 'Name'", actions[0].Title);
        Assert.IsTrue(actions[0].IsPreferred);
        // a client that has not said it takes documentChanges is sent changes (RefactoringWireTests has both).
        var edits = actions[0].Edit!.Changes!.Single().Value.Select(edit => edit.Range).ToArray();
        CollectionAssert.AreEqual(new[] { new Range(0, 7, 0, 14), new Range(2, 3, 2, 10), new Range(3, 15, 3, 22) }, edits);

        Assert.AreEqual("Rename 'strName'…", actions[1].Title);
        Assert.AreEqual(RefactoringCommands.Rename, actions[1].Command!.Name);
        Assert.AreEqual(Document.ToString(), actions[1].Command!.Arguments![0].ToString());
        Assert.AreEqual(diagnostic, actions[1].Diagnostics!.Single());
    }

    [TestMethod]
    public async Task ADiagnosticFixedByEdits_IsOfferedTheEdits_AsTheAnalyzerMadeThem()
    {
        var edits = new[]
        {
            new TextEdit { Range = new Range(1, 0, 1, 0), NewText = "    Dim result As Long\r\n" },
            new TextEdit { Range = new Range(2, 4, 2, 9), NewText = "result" },
        };
        var diagnostic = new Diagnostic
        {
            Range = new Range(2, 4, 2, 9),
            Code = "RDC00115",
            Source = "RDCore",
            Message = "Implicit result local",
            Data = new DiagnosticFixes { Edit = new EditFix { Title = "Declare a local for the return value of 'Total'", Edits = edits } }.ToData(),
        };

        var action = Assert.ContainsSingle(await ActionsAsync(new Range(2, 4, 2, 9), [CodeActionKind.QuickFix], diagnostic));

        Assert.AreEqual("Declare a local for the return value of 'Total'", action.Title);
        Assert.IsTrue(action.IsPreferred);
        Assert.AreEqual(diagnostic, action.Diagnostics!.Single());
        var (document, changes) = action.Edit!.Changes!.Single();
        Assert.AreEqual(Document, document);
        CollectionAssert.AreEqual(edits.Select(edit => (edit.Range, edit.NewText)).ToArray(), changes.Select(edit => (edit.Range, edit.NewText)).ToArray());
    }

    [TestMethod]
    public async Task ASuggestionThatCannotBeMade_IsNotOffered()
    {
        var actions = await ActionsAsync(new Range(0, 7, 0, 14), [CodeActionKind.QuickFix], Finding(new Range(0, 7, 0, 14), "x"));

        CollectionAssert.AreEqual(new[] { "Rename 'strName'…" }, actions.Select(action => action.Title).ToArray(), "x is declared already");
    }

    [TestMethod]
    public async Task EachNameARangeDeclares_IsOfferedARename()
    {
        var actions = await ActionsAsync(new Range(0, 0, 3, 0));

        CollectionAssert.AreEqual(new[] { "Rename 'strName'…", "Rename 'x'…" }, actions.Select(action => action.Title).ToArray());
        Assert.IsTrue(actions.All(action => action.Kind == CodeActionHandler.RefactorRename));
    }

    [TestMethod]
    public async Task AtAPosition_TheNameThere_IsOfferedARename()
    {
        var actions = await ActionsAsync(new Range(3, 24, 3, 24));

        CollectionAssert.AreEqual(new[] { "Rename 'x'…" }, actions.Select(action => action.Title).ToArray());
    }

    [TestMethod]
    public async Task AClientThatAsksForQuickFixesOnly_IsNotOfferedTheRefactorings()
        => Assert.IsEmpty(await ActionsAsync(new Range(0, 0, 3, 0), [CodeActionKind.QuickFix]));

    [TestMethod]
    public async Task TheShell_IsOfferedTheFixesAndTheRenamesOfARange_AndNotItsExtraction()
    {
        var document = DocumentUri.From(Path.Combine(Root, "Module1.bas"));
        var diagnostic = Finding(new Range(2, 8, 2, 15), "Name");
        var range = new Range(2, 0, 6, 0);

        var everything = await ActionsAsync(document, range, null, diagnostic);
        var shell = await ActionsAsync(document, range, RDCore.CLI.App.Repl.ReplPlatformClient.CodeActionKinds, diagnostic);

        Assert.IsTrue(everything.Any(action => action.Kind == CodeActionHandler.RefactorExtractMethod), "a client that asks for no kind is offered the extraction");
        CollectionAssert.AreEqual(
            everything.Where(action => action.Kind == CodeActionKind.QuickFix || action.Kind == CodeActionHandler.RefactorRename).Select(action => action.Title).ToArray(),
            shell.Select(action => action.Title).ToArray());
        Assert.IsTrue(shell.Any(action => action.Kind == CodeActionKind.QuickFix), "the fix of the diagnostic");
        Assert.IsTrue(shell.Any(action => action.Kind == CodeActionHandler.RefactorRename), "the rename of the name the range declares");
    }

    // a member called late-bound: nothing says what o holds; the code says w holds a Widget.
    private const string Widget = "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\nAttribute VB_Name = \"Widget\"\r\nPublic Sub Spin()\r\nEnd Sub\r\n";
    private const string Caller = "Attribute VB_Name = \"Module1\"\r\nPublic Sub Bar(ByVal o As Object)\r\n    o.Spin\r\n    Dim w As Object\r\n    Set w = New Widget\r\n    w.Spin\r\nEnd Sub\r\n";

    [TestMethod]
    [DataRow(false, DisplayName = "a client that can't ask its user")]
    [DataRow(true, DisplayName = "a client that takes change annotations")]
    public async Task ARenameFixWithLateBoundEdits_IsOfferedAsAnEdit_OnlyToAClientThatAsksItsUser(bool annotations)
    {
        Hold(new WorkspaceDocument("Widget.cls", Root, Widget, version: 1), new WorkspaceDocument("Module1.bas", Root, Caller, version: 1));
        _client = annotations
            ? new ClientCapabilities
            {
                Workspace = new WorkspaceClientCapabilities
                {
                    WorkspaceEdit = new WorkspaceEditCapability { DocumentChanges = true, ChangeAnnotationSupport = new WorkspaceEditSupportCapabilitiesChangeAnnotationSupport() },
                },
            }
            : null;
        var name = new Range(5, 11, 5, 15);

        var actions = await ActionsAsync(DocumentUri.From(Path.Combine(Root, "Widget.cls")), name, [CodeActionKind.QuickFix], Finding(name, "Turn"));

        var titles = actions.Select(action => action.Title).ToArray();
        CollectionAssert.AreEqual(annotations ? new[] { "Rename 'Spin' to 'Turn'", "Rename 'Spin'…" } : new[] { "Rename 'Spin'…" }, titles,
            "an edit without the late-bound calls would leave o.Spin and w.Spin calling a member Widget no longer has");
    }
}
