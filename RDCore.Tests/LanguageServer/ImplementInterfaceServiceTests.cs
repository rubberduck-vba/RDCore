using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.Runtime.LanguageCore.Errors;
using RDCore.Runtime.LanguageCore.Static;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Cli;
using RDCore.Tests.Runtime.Libraries;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// Implement Interface asked of a range of a document: the procedures an <c>Implements</c> directive or a <c>WithEvents</c> variable calls for, written after the
/// class's last procedure, with the interface's parameters and types, a call to the class's own member where it has one, and error 5 where it has none; what is
/// not offered, and what is refused; the code action that offers it; and the program, run in RDCore's runtime, as the procedures leave it.
/// </summary>
[TestClass]
public sealed class ImplementInterfaceServiceTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-implementinterface-ws");

    private const string ClassHeader = "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\n";

    private const string IShape = ClassHeader +
        "Attribute VB_Name = \"IShape\"\r\n" +
        "Option Explicit\r\n" +
        "Public Label As String\r\n" +
        "Public Owner As Object\r\n" +
        "\r\n" +
        "Public Function Area(ByVal factor As Double, Optional ByVal digits As Long = 2) As Double\r\n" +
        "End Function\r\n" +
        "\r\n" +
        "Public Sub Draw(canvas As Collection)\r\n" +
        "End Sub\r\n" +
        "\r\n" +
        "Public Property Get Tag() As Variant\r\n" +
        "End Property\r\n" +
        "\r\n" +
        "Public Property Let Tag(ByVal value As Variant)\r\n" +
        "End Property\r\n";

    // line 6 is the directive; Area is the class's own, and implements IShape.Area by a call.
    private const string Square = ClassHeader +
        "Attribute VB_Name = \"Square\"\r\n" +
        "Option Explicit\r\n" +
        "Implements IShape\r\n" +
        "\r\n" +
        "Public Function Area(ByVal factor As Double, Optional ByVal digits As Long = 2) As Double\r\n" +
        "    Area = 4 * factor\r\n" +
        "End Function\r\n";

    private const string Todo = "    Err.Raise 5 'TODO implement interface member\r\n";

    private const string SquareStubs =
        "Private Property Get IShape_Label() As String\r\n" + Todo + "End Property\r\n" +
        "\r\n" +
        "Private Property Let IShape_Label(ByVal value As String)\r\n" + Todo + "End Property\r\n" +
        "\r\n" +
        "Private Property Get IShape_Owner() As Object\r\n" + Todo + "End Property\r\n" +
        "\r\n" +
        "Private Property Set IShape_Owner(ByVal value As Object)\r\n" + Todo + "End Property\r\n" +
        "\r\n" +
        "Private Function IShape_Area(ByVal factor As Double, Optional ByVal digits As Long = 2) As Double\r\n" +
        "    IShape_Area = Area(factor, digits)\r\n" +
        "End Function\r\n" +
        "\r\n" +
        "Private Sub IShape_Draw(ByRef canvas As Collection)\r\n" + Todo + "End Sub\r\n" +
        "\r\n" +
        "Private Property Get IShape_Tag() As Variant\r\n" + Todo + "End Property\r\n" +
        "\r\n" +
        "Private Property Let IShape_Tag(ByVal value As Variant)\r\n" + Todo + "End Property\r\n";

    private const string Button = ClassHeader +
        "Attribute VB_Name = \"Button\"\r\n" +
        "Option Explicit\r\n" +
        "Public Event Clicked(ByVal x As Long)\r\n" +
        "Public Event Closing(Cancel As Boolean)\r\n" +
        "\r\n" +
        "Public Sub Click(ByVal x As Long)\r\n" +
        "    RaiseEvent Clicked(x)\r\n" +
        "    Dim cancelled As Boolean\r\n" +
        "    RaiseEvent Closing(cancelled)\r\n" +
        "    Debug.Print \"closing cancelled: \" & cancelled\r\n" +
        "End Sub\r\n";

    // line 6 is the WithEvents variable; Clicked has its handler, and Closing has none.
    private const string Panel = ClassHeader +
        "Attribute VB_Name = \"Panel\"\r\n" +
        "Option Explicit\r\n" +
        "Private WithEvents btn As Button\r\n" +
        "\r\n" +
        "Public Sub Init()\r\n" +
        "    Set btn = New Button\r\n" +
        "End Sub\r\n" +
        "\r\n" +
        "Public Sub Press()\r\n" +
        "    btn.Click 7\r\n" +
        "End Sub\r\n" +
        "\r\n" +
        "Private Sub btn_Clicked(ByVal x As Long)\r\n" +
        "    Debug.Print \"clicked \" & x\r\n" +
        "End Sub\r\n";

    private const string IGreet = ClassHeader +
        "Attribute VB_Name = \"IGreet\"\r\n" +
        "Option Explicit\r\n" +
        "Public Sub Hello()\r\n" +
        "End Sub\r\n";

    // implements IGreet already.
    private const string Greeter = ClassHeader +
        "Attribute VB_Name = \"Greeter\"\r\n" +
        "Option Explicit\r\n" +
        "Implements IGreet\r\n" +
        "\r\n" +
        "Private Sub IGreet_Hello()\r\n" +
        "End Sub\r\n";

    // its only procedure is in a branch of an #If block, and so is the end of its declarations.
    private const string Branchy = ClassHeader +
        "Attribute VB_Name = \"Branchy\"\r\n" +
        "Option Explicit\r\n" +
        "Implements IGreet\r\n" +
        "#If VBA7 Then\r\n" +
        "Public Sub Other()\r\n" +
        "End Sub\r\n" +
        "#End If\r\n";

    // an interface of two members.
    private const string IGreeting = ClassHeader +
        "Attribute VB_Name = \"IGreeting\"\r\n" +
        "Option Explicit\r\n" +
        "Public Sub Hello()\r\n" +
        "End Sub\r\n" +
        "\r\n" +
        "Public Sub Bye()\r\n" +
        "End Sub\r\n";

    // implements IGreeting (line 6), and declares in a branch of an #If block what the interface's Hello is implemented as.
    private static string Hiding(string name, string condition, string declaration) => ClassHeader +
        $"Attribute VB_Name = \"{name}\"\r\n" +
        "Option Explicit\r\n" +
        "Implements IGreeting\r\n" +
        $"#If {condition} Then\r\n" +
        declaration +
        "#End If\r\n" +
        "\r\n" +
        "Public Sub Other()\r\n" +
        "End Sub\r\n";

    private static readonly string DeadSub = Hiding("DeadSub", "0", "Private Sub IGreeting_Hello()\r\nEnd Sub\r\n");
    private static readonly string LiveSub = Hiding("LiveSub", "1", "Private Sub IGreeting_Hello()\r\nEnd Sub\r\n");
    private static readonly string DeadVariable = Hiding("DeadVariable", "0", "Private IGreeting_Hello As Long\r\n");

    private const int Version = 4;

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();
    private readonly IOptions<SdkAppOptions> _options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });
    private readonly Dictionary<Uri, ModuleParseResult> _parses = [];
    private readonly ModuleLiveBranches _liveBranches;
    private readonly RenameService _rename;
    private readonly ImplementInterfaceService _service;

    public ImplementInterfaceServiceTests()
    {
        _documents.WorkspaceRoot.Returns(Root);
        WorkspaceDocument[] documents =
        [
            new("IShape.cls", Root, IShape, version: 1), new("Square.cls", Root, Square, version: Version), new("Button.cls", Root, Button, version: 1),
            new("Panel.cls", Root, Panel, version: Version), new("IGreet.cls", Root, IGreet, version: 1), new("Greeter.cls", Root, Greeter, version: 1),
            new("Branchy.cls", Root, Branchy, version: 1), new("IGreeting.cls", Root, IGreeting, version: 1), new("DeadSub.cls", Root, DeadSub, version: 1),
            new("LiveSub.cls", Root, LiveSub, version: 1),
            new("DeadVariable.cls", Root, DeadVariable, version: 1),
        ];
        _documents.GetAllDocuments().Returns(documents);
        foreach (var document in documents)
        {
            var uri = document.Id.Uri.ToUri();
            var parse = _parses[uri] = new ModuleParser().Parse(uri, document.Text);
            _documents.TryGetDocument(uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = document; return true; });
            _parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>()).Returns(Task.FromResult(parse));
            _parsing.TryGetCached(uri, document.Version, out Arg.Any<ModuleParseResult>()).Returns(call => { call[2] = parse; return true; });
        }

        _parsing.ParseFragmentAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ModuleParser().Parse(call.Arg<Uri>(), call.Arg<string>())));

        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StandardLibrary.LibraryName])).ProvideSymbols()]);
        _liveBranches = new ModuleLiveBranches(_documents, _parsing);
        _rename = new RenameService(
            _documents, _parsing, new IntrinsicSymbolResolver(), libraries, _options, NullLogger<RenameService>.Instance, OneProject.Registry(),
            liveBranches: _liveBranches);
        _service = new ImplementInterfaceService(_rename, _parsing, _options);
    }

    private static Uri Document(string fileName) => DocumentUri.From(Path.Combine(Root, fileName)).ToUri();

    private static SourceRange At(int line, int character) => new(line, character, line, character);

    [TestMethod]
    [DataRow(0, DisplayName = "at the start of the directive")]
    [DataRow(14, DisplayName = "on the interface's name")]
    public async Task AnImplementsDirective_IsImplemented_AfterTheClassesLastProcedure(int character)
    {
        var answer = await _service.ImplementAsync(Document("Square.cls"), At(6, character), CancellationToken.None);

        Assert.IsNull(answer.Refusal, answer.Refusal);
        Assert.AreEqual(Version, answer.Version);
        Assert.AreEqual(Square + "\r\n" + SquareStubs, DeclarationEdits.ApplyTo(Square, answer.Edits!));
    }

    [TestMethod]
    public async Task AWithEventsVariable_HasAHandlerToWrite_ForEachEventItHasNone()
    {
        var answer = await _service.ImplementAsync(Document("Panel.cls"), At(6, 20), CancellationToken.None);

        Assert.IsNull(answer.Refusal, answer.Refusal);
        Assert.AreEqual(Panel + "\r\nPrivate Sub btn_Closing(ByRef Cancel As Boolean)\r\n    Exit Sub 'TODO handle the event\r\nEnd Sub\r\n", DeclarationEdits.ApplyTo(Panel, answer.Edits!));
    }

    [TestMethod]
    public async Task TheOffer_IsTitledForTheInterface_OrForTheVariable()
    {
        var implements = await _service.OfferAsync(Document("Square.cls"), At(6, 3), CancellationToken.None);
        var withEvents = await _service.OfferAsync(Document("Panel.cls"), At(6, 3), CancellationToken.None);

        Assert.AreEqual("Implement interface 'IShape'", implements?.Title);
        Assert.IsNull(implements?.Refusal);
        Assert.AreEqual("Handle the events of 'btn'", withEvents?.Title);
    }

    [TestMethod]
    [DataRow("Square.cls", 8, DisplayName = "a line with no directive and no WithEvents variable")]
    [DataRow("Greeter.cls", 6, DisplayName = "a directive whose interface is implemented")]
    [DataRow("IShape.cls", 6, DisplayName = "a standard member of an interface")]
    public async Task NothingIsOffered_Where_(string document, int line)
    {
        Assert.IsNull(await _service.OfferAsync(Document(document), At(line, 0), CancellationToken.None));
    }

    [TestMethod]
    public async Task WhereTheProceduresWouldBeInABranchOfAnIfBlock_TheImplementationIsRefused()
    {
        var offer = await _service.OfferAsync(Document("Branchy.cls"), At(6, 0), CancellationToken.None);
        var answer = await _service.ImplementAsync(Document("Branchy.cls"), At(6, 0), CancellationToken.None);

        Assert.IsNotNull(offer?.Refusal);
        StringAssert.Contains(offer.Refusal, "#If");
        Assert.AreEqual(offer.Refusal, answer.Refusal);
        Assert.IsNull(answer.Edits);
    }

    // ---- a branch of an #If block that is not live, as the environment host evaluated it (MS-VBAL §3.4) ----

    // what the host says of a document's #If block when the module's symbols are defined: its #If branch dead, or no branch.
    private void HostEvaluated(string fileName, bool ifBranchIsDead)
    {
        var parse = _parses[Document(fileName)];
        var ifBranch = parse.PrecompilerTrivia.SelectMany(node => node.Descendants().Prepend(node)).OfType<PrecompilerIfBlockStatementNode>().First()
            .Children.OfType<PrecompilerTriviaNode>().First();
        _liveBranches.Record(parse, ifBranchIsDead ? [ifBranch.SourceLocation] : []);
        Assert.IsNotNull(_liveBranches.DeadTestFor(parse), "the host's evaluation is known of the document as it reads");
    }

    private const string HelloStub = "\r\nPrivate Sub IGreeting_Hello()\r\n" + Todo + "End Sub\r\n";
    private const string ByeStub = "\r\nPrivate Sub IGreeting_Bye()\r\n" + Todo + "End Sub\r\n";

    [TestMethod]
    [DataRow("DeadSub", DisplayName = "a procedure of the stub's name")]
    [DataRow("DeadVariable", DisplayName = "a variable of the stub's name")]
    public async Task WhatADeadBranchDeclares_IsNoMemberOfTheClass_AndTheStubIsWritten(string name)
    {
        var text = name == "DeadSub" ? DeadSub : DeadVariable;
        HostEvaluated(name + ".cls", ifBranchIsDead: true);

        var answer = await _service.ImplementAsync(Document(name + ".cls"), At(6, 0), CancellationToken.None);

        Assert.IsNull(answer.Refusal, answer.Refusal);
        Assert.AreEqual(text + HelloStub + ByeStub, DeclarationEdits.ApplyTo(text, answer.Edits!));
    }

    [TestMethod]
    public async Task WhatALiveBranchDeclares_IsAMemberOfTheClass_AndOnlyTheOtherStubIsWritten()
    {
        HostEvaluated("LiveSub.cls", ifBranchIsDead: false);

        var answer = await _service.ImplementAsync(Document("LiveSub.cls"), At(6, 0), CancellationToken.None);

        Assert.IsNull(answer.Refusal, answer.Refusal);
        Assert.AreEqual(LiveSub + ByeStub, DeclarationEdits.ApplyTo(LiveSub, answer.Edits!));
    }

    // the stub is written in live code: once the constant makes the branch live, the class declares the procedure twice, as when the stub is written by hand.
    [TestMethod]
    public async Task TheDeadBranchMadeLive_DeclaresTheProcedureTheStubDeclares_ASecondTime()
    {
        HostEvaluated("DeadSub.cls", ifBranchIsDead: true);
        var answer = await _service.ImplementAsync(Document("DeadSub.cls"), At(6, 0), CancellationToken.None);
        Assert.IsNull(answer.Refusal, answer.Refusal);

        var turnedOn = DeclarationEdits.ApplyTo(DeadSub, answer.Edits!).Replace("#If 0 Then", "#If 1 Then");
        var parse = new ModuleParser().Parse(Document("DeadSub.cls"), turnedOn);
        var errors = DeclarationStaticSemanticsEvaluator.CheckSyntax(parse.SyntaxTree!, ConditionalCompilationBlocks.Of(parse.PrecompilerTrivia));

        var ambiguous = Assert.ContainsSingle(errors.Where(error => error.ErrorId == (int)VBCompileErrorId.AmbiguousName));
        Assert.AreEqual(DeadSub.Split("\r\n").Length + 1, ambiguous.Location.Range.Start.Line + 1, "the stub, after the class's last line and the blank one");
    }

    // ---- the code action ----

    [TestMethod]
    public async Task TheCodeAction_IsPreferred_HasItsEdit_AndFixesTheDiagnosticOfTheDirective()
    {
        var notImplemented = new Diagnostic { Code = "VBC09329", Range = new Range(6, 0, 6, 17), Message = "not implemented" };
        var elsewhere = new Diagnostic { Code = "VBC09329", Range = new Range(9, 0, 9, 4), Message = "elsewhere" };

        var action = (await ActionsAsync(Handler(), Document("Square.cls"), new Range(6, 2, 6, 2), notImplemented, elsewhere)).Single();

        Assert.AreEqual(RefactoringKinds.ImplementInterface, action.Kind.ToString());
        Assert.IsTrue(action.IsPreferred);
        Assert.AreSame(notImplemented, action.Diagnostics!.Single());
        var edits = action.Edit!.DocumentChanges!.Single().TextDocumentEdit!;
        Assert.AreEqual(Version, edits.TextDocument.Version);
    }

    [TestMethod]
    public async Task TheCodeAction_IsResolvedToItsEdit_ForAClientThatResolvesIt()
    {
        var handler = Handler(resolvesEdits: true);
        var offered = (await ActionsAsync(handler, Document("Panel.cls"), new Range(6, 0, 6, 0))).Single();

        var resolved = await handler.Handle(offered, CancellationToken.None);

        Assert.IsNull(offered.Edit);
        Assert.AreEqual("Private Sub btn_Closing(ByRef Cancel As Boolean)\r\n    Exit Sub 'TODO handle the event\r\nEnd Sub", resolved.Edit!.DocumentChanges!.Single().TextDocumentEdit!.Edits.Single().NewText.Trim());
    }

    [TestMethod]
    public async Task ARefusedImplementation_IsOfferedDisabled_ToAClientThatShowsIt()
    {
        var shown = (await ActionsAsync(Handler(showsDisabled: true), Document("Branchy.cls"), new Range(6, 0, 6, 0))).Single();
        var hidden = await ActionsAsync(Handler(), Document("Branchy.cls"), new Range(6, 0, 6, 0));

        StringAssert.Contains(shown.Disabled?.Reason, "#If");
        Assert.IsEmpty(hidden);
    }

    private CodeActionHandler Handler(bool showsDisabled = false, bool resolvesEdits = false)
    {
        var handler = new CodeActionHandler(_rename, _documents, new System.IO.Abstractions.TestingHelpers.MockFileSystem(), new ExtractMethodService(_rename, _parsing, _options),
            new MoveMemberService(_rename, _parsing), Substitute.For<IPlatformClientCapabilitiesService>(), new OmniSharp.Extensions.LanguageServer.Protocol.Serialization.LspSerializer(),
            implementInterface: _service);
        ((IRegistration<CodeActionRegistrationOptions, CodeActionCapability>)handler).GetRegistrationOptions(new(), new ClientCapabilities
        {
            TextDocument = new()
            {
                CodeAction = new CodeActionCapability
                {
                    DisabledSupport = showsDisabled,
                    DataSupport = resolvesEdits,
                    ResolveSupport = resolvesEdits ? new CodeActionCapabilityResolveSupportOptions { Properties = new Container<string>("edit") } : null,
                },
            },
            Workspace = new WorkspaceClientCapabilities { WorkspaceEdit = new WorkspaceEditCapability { DocumentChanges = true } },
        });
        return handler;
    }

    // the implementations a client is offered for a range, among the code actions of the document.
    private static async Task<IReadOnlyList<CodeAction>> ActionsAsync(CodeActionHandler handler, Uri document, Range range, params Diagnostic[] diagnostics)
    {
        var actions = await handler.Handle(new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier(DocumentUri.From(document)),
            Range = range,
            Context = new CodeActionContext
            {
                Diagnostics = new Container<Diagnostic>(diagnostics),
                Only = new Container<CodeActionKind>(new CodeActionKind(RefactoringKinds.ImplementInterface)),
            },
        }, CancellationToken.None);
        return [.. actions!.Select(action => action.CodeAction!)];
    }

    // ---- the program, before and after ----

    [TestMethod]
    public async Task ThroughTheInterface_AMemberTheClassHas_IsCalled_AndAnyOtherRaisesError5()
    {
        var answer = await _service.ImplementAsync(Document("Square.cls"), At(6, 0), CancellationToken.None);
        var implemented = DeclarationEdits.ApplyTo(Square, answer.Edits!);
        const string program =
            "Attribute VB_Name = \"Program\"\r\n" +
            "Option Explicit\r\n" +
            "Public Sub Main()\r\n" +
            "    Dim s As IShape\r\n" +
            "    Set s = New Square\r\n" +
            "    Debug.Print s.Area(2)\r\n" +
            "    On Error Resume Next\r\n" +
            "    s.Label = \"x\"\r\n" +
            "    Debug.Print Err.Number\r\n" +
            "End Sub\r\n";

        var output = await ModuleWorkspace.RunAsync([("IShape", IShape), ("Square", implemented)], program);

        CollectionAssert.AreEqual(new[] { "8", "5" }, output.Select(line => line.Trim()).ToArray(), string.Join(" | ", output));
    }

    [TestMethod]
    public async Task TheHandlersWritten_ChangeNothingTheProgramDoes()
    {
        var answer = await _service.ImplementAsync(Document("Panel.cls"), At(6, 0), CancellationToken.None);
        var handled = DeclarationEdits.ApplyTo(Panel, answer.Edits!);
        const string program =
            "Attribute VB_Name = \"Program\"\r\n" +
            "Option Explicit\r\n" +
            "Public Sub Main()\r\n" +
            "    Dim p As Panel\r\n" +
            "    Set p = New Panel\r\n" +
            "    p.Init\r\n" +
            "    p.Press\r\n" +
            "End Sub\r\n";

        var before = await ModuleWorkspace.RunAsync([("Button", Button), ("Panel", Panel)], program);
        var after = await ModuleWorkspace.RunAsync([("Button", Button), ("Panel", handled)], program);

        CollectionAssert.AreEqual(new[] { "clicked 7", "closing cancelled: False" }, before.Select(line => line.Trim()).ToArray(), string.Join(" | ", before));
        CollectionAssert.AreEqual(before, after);
    }

    // ---- the diagnostics, before and after ----

    [TestMethod]
    [DataRow("Square", 6, DisplayName = "an Implements directive")]
    [DataRow("Panel", 6, DisplayName = "a WithEvents variable")]
    public async Task AcceptingTheImplementation_LeavesNoNewDiagnostic(string module, int line)
    {
        var sources = new Dictionary<string, string> { ["IShape"] = IShape, ["Square"] = Square, ["Button"] = Button, ["Panel"] = Panel };
        var answer = await _service.ImplementAsync(Document(module + ".cls"), At(line, 0), CancellationToken.None);
        Assert.IsNull(answer.Refusal, answer.Refusal);

        var before = await FindingsAsync(sources, module);
        sources[module] = DeclarationEdits.ApplyTo(sources[module], answer.Edits!);
        var after = await FindingsAsync(sources, module);

        // a finding is new when there are more of its code than there were.
        var added = after.Where(finding => after.Count(other => other.Code == finding.Code) > before.Count(other => other.Code == finding.Code)).ToArray();
        Assert.IsEmpty(added, string.Join("; ", added.Select(finding => $"{finding.Code} at line {finding.Range.Start.Line}: {finding.Message}")));
    }

    // every finding of every analyzer of the platform, with the default options, for one class module of a workspace as the host models it.
    private static async Task<AnalyzerFinding[]> FindingsAsync(IReadOnlyDictionary<string, string> classes, string module)
    {
        var payload = await ModuleWorkspace.SemanticsAsync([.. classes.Select(each => (each.Key, each.Value))], "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n");
        var semantics = payload.Modules.Single(each => each.Module.Fragment.TrimStart('#') == module);
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, module + ".cls"));
        var context = new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, classes[module]), semantics);
        return [.. ModuleAnalyzerHost.Run(Analyzers(), context, AnalysisPhase.All, NullLogger.Instance)];
    }

    private static IEnumerable<IModuleAnalyzer> Analyzers()
    {
        var options = Options.Create(new RDCore.Diagnostics.DiagnosticsOptions());
        return typeof(RDCore.Diagnostics.CoreDiagnosticsApp).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IModuleAnalyzer).IsAssignableFrom(type))
            .Select(type => (IModuleAnalyzer)(type.GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .Single().GetParameters().Length == 0 ? Activator.CreateInstance(type, nonPublic: true)! : Activator.CreateInstance(type, options)!));
    }
}
