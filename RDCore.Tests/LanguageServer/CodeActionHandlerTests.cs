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
    private RDCore.SDK.Server.Services.IPlatformClientCapabilitiesService _capabilities = Substitute.For<RDCore.SDK.Server.Services.IPlatformClientCapabilitiesService>();

    // what the client said it can do, as it initialized; none, a client that takes neither documentChanges nor change annotations.
    private ClientCapabilities? _client;

    // the project of the workspace, which a fix that changes it is offered against, and the libraries it can reference.
    private readonly IProjectFileService _projectFiles = Substitute.For<IProjectFileService>();
    private readonly ILibrarySource _libraries = new InMemoryLibrarySource(new LibraryDescription
    {
        Name = "Excel",
        Origin = new LibraryOrigin { Guid = "{00020813-0000-0000-C000-000000000046}", Version = "1.9" },
    });

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
            _capabilities, new OmniSharp.Extensions.LanguageServer.Protocol.Serialization.LspSerializer(), _projectFiles, _libraries);
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

    // a client that said it takes a change of a list of its settings (rdcore/settings/listValue), as the shell does.
    private void ClientTakesListChanges()
    {
        var capabilities = new RDCore.SDK.Server.Services.PlatformClientCapabilitiesService();
        capabilities.Record(new RDCore.SDK.Platform.Protocol.PlatformInitializeParams
        {
            Expected = new RDCore.SDK.Client.CorePlatformClientCapabilities
            {
                LanguageServer = new RDCore.SDK.Client.LanguageServerCapabilities { ListSettingValue = new RDCore.SDK.Client.ListSettingValue(true) },
            },
        });
        _capabilities = capabilities;
    }

    private static Diagnostic Coded(string code, Range range) => new() { Range = range, Code = code, Source = "RDCore", Message = code };

    [TestMethod]
    public async Task ACodeOfTheDiagnosticsOfTheRange_IsOfferedTheActionThatDisablesIt_InEveryWorkspace_Once()
    {
        ClientTakesListChanges();
        var first = Coded("RDC01002", new Range(0, 7, 0, 14));
        var second = Coded("RDC01002", new Range(3, 15, 3, 22));

        var action = Assert.ContainsSingle(await ActionsAsync(new Range(0, 0, 3, 22), [CodeActionKind.QuickFix], first, second));

        Assert.AreEqual("Disable RDC01002 in every workspace", action.Title);
        Assert.AreEqual(CodeActionKind.QuickFix, action.Kind);
        CollectionAssert.AreEqual(new[] { first, second }, action.Diagnostics!.ToArray());
        Assert.AreEqual(ListSettingValueCommand.CommandName, action.Command!.Name);
        var change = (Newtonsoft.Json.Linq.JObject)action.Command.Arguments!.Single();
        Assert.AreEqual("Analysis.DisabledDiagnostics", (string?)change["key"]);
        Assert.AreEqual("RDC01002", (string?)change["value"]);
        Assert.AreEqual((int)RDCore.SDK.Platform.Protocol.SettingScope.Platform, (int?)change["scope"]);
        Assert.IsFalse((bool?)change["remove"]);
    }

    [TestMethod]
    [DataRow("VBC00013", DisplayName = "a compile error")]
    [DataRow("VBR00013", DisplayName = "a run-time error")]
    [DataRow("VBA01004", DisplayName = "an error the program raises")]
    public async Task ACodeOfTheLanguagesOwnErrors_IsNotOfferedTheActionThatDisablesIt(string code)
    {
        ClientTakesListChanges();

        Assert.IsEmpty(await ActionsAsync(new Range(0, 7, 0, 14), [CodeActionKind.QuickFix], Coded(code, new Range(0, 7, 0, 14))));
    }

    [TestMethod]
    public async Task AClientThatDoesNotTakeAChangeOfItsSettings_IsNotOfferedTheActionThatDisablesACode()
        => Assert.IsEmpty(await ActionsAsync(new Range(0, 7, 0, 14), [CodeActionKind.QuickFix], Coded("RDC01002", new Range(0, 7, 0, 14))));

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

    // a finding of RDC00115 in the shell's program, fixed by two edits of it: the declaration of a local, or the parameter passed ByRef.
    private static Diagnostic FixedByEdits(int? version, bool firstChangesBehavior = false) => new()
    {
        Range = new Range(2, 3, 2, 10),
        Code = "RDC00115",
        Source = "RDCore",
        Message = "Implicit result local",
        Data = new DiagnosticFixes
        {
            Edits =
            [
                new EditFix
                {
                    Title = "Declare a local",
                    Edits = [new TextEdit { Range = new Range(1, 0, 1, 0), NewText = "15 Dim result As Long\r\n" }, new TextEdit { Range = new Range(2, 3, 2, 10), NewText = "result" }],
                    ChangesBehavior = firstChangesBehavior,
                    Version = version,
                },
                new EditFix { Title = "Pass it ByRef", Edits = [new TextEdit { Range = new Range(2, 3, 2, 10), NewText = "x" }], ChangesBehavior = true, Version = version },
            ],
        }.ToData(),
    };

    [TestMethod]
    public async Task ADiagnosticFixedByEdits_IsOfferedEachFix_BestFirst_AsTheAnalyzerMadeIt()
    {
        var diagnostic = FixedByEdits(version: 2);

        var actions = await ActionsAsync(new Range(2, 3, 2, 10), [CodeActionKind.QuickFix], diagnostic);

        CollectionAssert.AreEqual(new[] { "Declare a local", "Pass it ByRef" }, actions.Select(action => action.Title).ToArray());
        CollectionAssert.AreEqual(new bool[] { true, false }, actions.Select(action => action.IsPreferred).ToArray(), "only the first is preferred");
        Assert.IsTrue(actions.All(action => action.Diagnostics!.Single() == diagnostic));
        // a client that has not said it takes documentChanges is sent changes.
        var (document, changes) = actions[0].Edit!.Changes!.Single();
        Assert.AreEqual(Document, document);
        CollectionAssert.AreEqual(new[] { (new Range(1, 0, 1, 0), "15 Dim result As Long\r\n"), (new Range(2, 3, 2, 10), "result") },
            changes.Select(edit => (edit.Range, edit.NewText)).ToArray());
    }

    private Diagnostic FixedByTheProject(string library, params RDCoreReference[] references)
    {
        _projectFiles.Project.Returns(new ProjectFile(Root, new RDCoreProject { References = references, Modules = [new RDCoreModule { RelativeUri = "Main.rdc" }] }));
        return new Diagnostic
        {
            Range = new Range(0, 0, 0, 2),
            Code = "RDC01007",
            Source = "RDCore",
            Message = "host library not referenced",
            Data = new DiagnosticFixes
            {
                Project = new ProjectFix { Title = $"Reference the {library} library", Changes = [ProjectChange.AddReference(library)] },
            }.ToData(),
        };
    }

    [TestMethod]
    public async Task ADiagnosticFixedByAChangeOfTheProject_IsOfferedTheCommandThatMakesIt()
    {
        var diagnostic = FixedByTheProject("Excel", new RDCoreReference { Name = "VBA" });

        var action = (await ActionsAsync(new Range(0, 0, 0, 2), [CodeActionKind.QuickFix], diagnostic)).Single();

        Assert.AreEqual("Reference the Excel library", action.Title);
        Assert.IsTrue(action.IsPreferred);
        Assert.IsNull(action.Edit);
        Assert.AreEqual(ServerCommands.ChangeProject, action.Command!.Name);
        Assert.AreEqual(Document.ToString(), (string?)action.Command.Arguments![0]);
        Assert.AreEqual(ProjectChange.AddReference("Excel"), action.Command.Arguments[1].ToObject<ProjectFix>()!.Changes.Single());
    }

    [TestMethod]
    [DataRow("Word", DisplayName = "a library the platform does not describe")]
    [DataRow("excel", DisplayName = "a library the project references already")]
    public async Task AChangeOfTheProjectThatCannotBeMade_OrChangesNothing_IsNotOffered(string library)
        => Assert.IsEmpty(await ActionsAsync(new Range(0, 0, 0, 2), [CodeActionKind.QuickFix],
            FixedByTheProject(library, new RDCoreReference { Name = "VBA" }, new RDCoreReference { Name = "Excel" })));

    [TestMethod]
    public async Task AFirstFixThatChangesWhatTheCodeDoes_IsNotPreferred()
    {
        var actions = await ActionsAsync(new Range(2, 3, 2, 10), [CodeActionKind.QuickFix], FixedByEdits(version: 2, firstChangesBehavior: true));

        CollectionAssert.AreEqual(new bool[] { false, false }, actions.Select(action => action.IsPreferred).ToArray());
    }

    [TestMethod]
    [DataRow(1, DisplayName = "made of an earlier version")]
    [DataRow(null, DisplayName = "of a version that is not known")]
    public async Task EditFixesOfAnotherVersionOfTheDocument_AreNotOffered(int? version)
        => Assert.IsEmpty(await ActionsAsync(new Range(2, 3, 2, 10), [CodeActionKind.QuickFix], FixedByEdits(version)));

    [TestMethod]
    public async Task EditFixes_AreSentWithTheVersionTheyAreOf_ToAClientThatTakesIt()
    {
        _client = new ClientCapabilities { Workspace = new WorkspaceClientCapabilities { WorkspaceEdit = new WorkspaceEditCapability { DocumentChanges = true } } };

        var action = (await ActionsAsync(new Range(2, 3, 2, 10), [CodeActionKind.QuickFix], FixedByEdits(version: 2)))[0];

        var edit = action.Edit!.DocumentChanges!.Single().TextDocumentEdit!;
        Assert.AreEqual(Document, edit.TextDocument.Uri);
        Assert.AreEqual(2, edit.TextDocument.Version);
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

    [TestMethod]
    public async Task ADiagnosticFixedByARefactoring_IsOfferedTheRefactoring_ForItsRange_Once()
    {
        var document = DocumentUri.From(Path.Combine(Root, "Module1.bas"));
        var range = new Range(2, 0, 6, 0);
        var diagnostic = new Diagnostic
        {
            Range = range,
            Code = "RDC00000",
            Source = "RDCore",
            Message = "Too long",
            Data = new DiagnosticFixes { Refactoring = new RefactoringFix { Kind = RefactoringKinds.ExtractMethod } }.ToData(),
        };

        var atAPosition = await ActionsAsync(document, new Range(1, 0, 1, 0), null, diagnostic);
        var forTheRange = await ActionsAsync(document, range, null, diagnostic);

        var extraction = atAPosition.Single(action => action.Kind == CodeActionHandler.RefactorExtractMethod);
        Assert.AreEqual(diagnostic, extraction.Diagnostics!.Single());
        Assert.IsNotNull(extraction.Edit, "a client with no dialog is sent the edit");
        Assert.ContainsSingle(forTheRange.Where(action => action.Kind == CodeActionHandler.RefactorExtractMethod), "the selection is the range of the diagnostic");
    }

    [TestMethod]
    public async Task ARefactoringFixOfAKindThisServerDoesNotMake_IsNotOffered()
    {
        var document = DocumentUri.From(Path.Combine(Root, "Module1.bas"));
        var diagnostic = new Diagnostic
        {
            Range = new Range(2, 0, 6, 0),
            Message = "Unsorted",
            Data = new DiagnosticFixes { Refactoring = new RefactoringFix { Kind = "refactor.rewrite.unknown" } }.ToData(),
        };

        Assert.IsEmpty(await ActionsAsync(document, new Range(1, 0, 1, 0), [CodeActionKind.Refactor], diagnostic));
    }
}
