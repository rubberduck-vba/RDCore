using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using NSubstitute;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Refactoring;
using RDCore.LanguageServer.Services.Refactoring.MoveMember;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// Move Member Up and Move Member Down as a client asks them of a range of a document: which moves are offered, the module each leaves, and the code actions
/// that offer them.
/// </summary>
[TestClass]
public sealed class MoveMemberServiceTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-movemember-ws");

    // First has a comment above it; the Property Get and Let of Value move together; Second and Third are in a branch of an #If block, which Last is not in.
    private const string Module =
        "Option Explicit\r\n" +
        "Private count As Long\r\n" +
        "\r\n" +
        "' runs first\r\n" +
        "Public Sub First()\r\n" +
        "    count = 1\r\n" +
        "End Sub\r\n" +
        "\r\n" +
        "Public Property Get Value() As Long\r\n" +
        "    Value = count\r\n" +
        "End Property\r\n" +
        "\r\n" +
        "Public Property Let Value(ByVal v As Long)\r\n" +
        "    count = v\r\n" +
        "End Property\r\n" +
        "\r\n" +
        "#If VBA7 Then\r\n" +
        "Public Sub Second()\r\n" +
        "End Sub\r\n" +
        "\r\n" +
        "Public Sub Third()\r\n" +
        "End Sub\r\n" +
        "#End If\r\n" +
        "\r\n" +
        "Public Sub Last()\r\n" +
        "End Sub\r\n";

    private const string Declarations = "Option Explicit\r\nPrivate count As Long\r\n\r\n";
    private const string FirstText = "' runs first\r\nPublic Sub First()\r\n    count = 1\r\nEnd Sub\r\n";
    private const string ValueText = "Public Property Get Value() As Long\r\n    Value = count\r\nEnd Property\r\n\r\n" +
        "Public Property Let Value(ByVal v As Long)\r\n    count = v\r\nEnd Property\r\n";
    private const string BlockAndLast = "#If VBA7 Then\r\nPublic Sub Second()\r\nEnd Sub\r\n\r\nPublic Sub Third()\r\nEnd Sub\r\n#End If\r\n\r\nPublic Sub Last()\r\nEnd Sub\r\n";

    // the property comes before the function; a comment of its own is between the function and the Subs, which are not in the order of their names.
    private const string Unsorted =
        "Option Explicit\r\n" +
        "\r\n" +
        "Public Property Get Name() As String\r\n" +
        "    Name = \"x\"\r\n" +
        "End Property\r\n" +
        "Public Property Let Name(ByVal v As String)\r\n" +
        "End Property\r\n" +
        "\r\n" +
        "Public Function Twice(ByVal n As Long) As Long\r\n" +
        "    Twice = n * 2\r\n" +
        "End Function\r\n" +
        "\r\n" +
        "' a comment of its own\r\n" +
        "\r\n" +
        "Public Sub Zeta()\r\n" +
        "End Sub\r\n" +
        "\r\n" +
        "Public Sub Alpha()\r\n" +
        "End Sub\r\n";

    private const string Sorted =
        "Option Explicit\r\n" +
        "\r\n" +
        "Public Function Twice(ByVal n As Long) As Long\r\n" +
        "    Twice = n * 2\r\n" +
        "End Function\r\n" +
        "\r\n" +
        "Public Property Get Name() As String\r\n" +
        "    Name = \"x\"\r\n" +
        "End Property\r\n" +
        "Public Property Let Name(ByVal v As String)\r\n" +
        "End Property\r\n" +
        "\r\n" +
        "' a comment of its own\r\n" +
        "\r\n" +
        "Public Sub Alpha()\r\n" +
        "End Sub\r\n" +
        "\r\n" +
        "Public Sub Zeta()\r\n" +
        "End Sub\r\n";

    private const int Version = 3;

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();
    private readonly RenameService _rename;

    public MoveMemberServiceTests()
    {
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });
        _documents.WorkspaceRoot.Returns(Root);
        var module = new WorkspaceDocument("Main.bas", Root, Module, version: Version);
        var unsorted = new WorkspaceDocument("Unsorted.bas", Root, Unsorted, version: Version);
        _documents.GetAllDocuments().Returns([module, unsorted]);
        var uri = module.Id.Uri.ToUri();
        _documents.TryGetDocument(uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = module; return true; });
        _parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new ModuleParser().Parse(uri, Module)));
        var unsortedUri = unsorted.Id.Uri.ToUri();
        _documents.TryGetDocument(unsortedUri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = unsorted; return true; });
        _parsing.ParseDocumentAsync(unsortedUri, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new ModuleParser().Parse(unsortedUri, Unsorted)));
        _parsing.ParseFragmentAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ModuleParser().Parse(call.Arg<Uri>(), call.Arg<string>())));
        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StandardLibrary.LibraryName])).ProvideSymbols()]);
        _rename = new RenameService(_documents, _parsing, new IntrinsicSymbolResolver(), libraries, options, NullLogger<RenameService>.Instance, OneProject.Registry());
    }

    private static Uri Document => DocumentUri.From(Path.Combine(Root, "Main.bas")).ToUri();

    private MoveMemberService Sut() => new(_rename, _parsing);

    private static SourceRange At(int line, int character) => new(line, character, line, character);

    private async Task<string> MovedAsync(SourceRange range, MemberMoveDirection direction)
    {
        var answer = await Sut().MoveAsync(Document, range, direction, CancellationToken.None);
        Assert.IsNull(answer.Refusal, answer.Refusal);
        Assert.AreEqual(Version, answer.Version);
        return MemberMoves.Applied(Module, answer.Edits!);
    }

    [TestMethod]
    public async Task AProcedureMovedDown_GoesBelowTheNextOne_WithTheCommentAboveIt()
    {
        var moved = await MovedAsync(At(5, 4), MemberMoveDirection.Down);

        Assert.AreEqual(Declarations + ValueText + "\r\n" + FirstText + "\r\n" + BlockAndLast, moved);
    }

    [TestMethod]
    [DataRow(9, 4, DisplayName = "from the Property Get")]
    [DataRow(13, 4, DisplayName = "from the Property Let")]
    public async Task APropertyMoved_TakesItsOtherHalfWithIt(int line, int character)
    {
        var moved = await MovedAsync(At(line, character), MemberMoveDirection.Up);

        Assert.AreEqual(Declarations + ValueText + "\r\n" + FirstText + "\r\n" + BlockAndLast, moved);
    }

    [TestMethod]
    public async Task AProcedureInABranch_MovesWithinIt()
    {
        var moved = await MovedAsync(At(17, 0), MemberMoveDirection.Down);

        Assert.AreEqual(
            Declarations + FirstText + "\r\n" + ValueText + "\r\n" +
            "#If VBA7 Then\r\nPublic Sub Third()\r\nEnd Sub\r\n\r\nPublic Sub Second()\r\nEnd Sub\r\n#End If\r\n\r\nPublic Sub Last()\r\nEnd Sub\r\n",
            moved);
    }

    [TestMethod]
    public async Task TheFirstProcedure_IsNotMovedIntoTheDeclarations_AndTheLastIsNotOfferedDown()
    {
        var first = await Sut().OffersAsync(Document, At(4, 0), CancellationToken.None);
        var last = await Sut().OffersAsync(Document, At(24, 0), CancellationToken.None);

        CollectionAssert.AreEqual(new[] { MemberMoveDirection.Down }, first.Select(offer => offer.Direction).ToArray());
        Assert.AreEqual("Move 'First' down", first[0].Title);
        CollectionAssert.AreEqual(new[] { MemberMoveDirection.Up }, last.Select(offer => offer.Direction).ToArray());
    }

    [TestMethod]
    [DataRow(1, 0, 1, 0, DisplayName = "the declarations")]
    [DataRow(7, 0, 7, 0, DisplayName = "a blank line between two procedures")]
    [DataRow(5, 0, 9, 0, DisplayName = "a selection of two procedures")]
    public async Task ARangeInNoProcedure_IsOfferedNoMove(int startLine, int startCharacter, int endLine, int endCharacter)
    {
        Assert.IsEmpty(await Sut().OffersAsync(Document, new SourceRange(startLine, startCharacter, endLine, endCharacter), CancellationToken.None));
    }

    [TestMethod]
    public async Task AMoveIntoABranch_OrOutOfOne_IsRefused_WithWhy()
    {
        var second = await Sut().OffersAsync(Document, At(17, 0), CancellationToken.None);
        var third = await Sut().OffersAsync(Document, At(20, 0), CancellationToken.None);
        var last = await Sut().OffersAsync(Document, At(24, 0), CancellationToken.None);

        StringAssert.StartsWith(second.Single(offer => offer.Direction is MemberMoveDirection.Up).Refusal, "'Second' is in a branch of an #If block that 'Value' is not in");
        Assert.IsNull(second.Single(offer => offer.Direction is MemberMoveDirection.Down).Refusal);
        StringAssert.StartsWith(third.Single(offer => offer.Direction is MemberMoveDirection.Down).Refusal, "'Third' is in a branch of an #If block that 'Last' is not in");
        StringAssert.StartsWith(last.Single().Refusal, "'Third' is in a branch of an #If block that 'Last' is not in: moved past it, 'Last' would go into the branch");

        var refused = await Sut().MoveAsync(Document, At(24, 0), MemberMoveDirection.Up, CancellationToken.None);
        Assert.IsNull(refused.Edits);
        Assert.AreEqual(last.Single().Refusal, refused.Refusal);
    }

    [TestMethod]
    public void TheVerification_TakesOnlyTheModuleAsTheMoveLeavesIt()
    {
        var parse = new ModuleParser().Parse(Document, Module);
        var plan = MemberMoves.Plan(parse, Module, At(5, 4), MemberMoveDirection.Down)!;
        var moved = MemberMoves.Applied(Module, MemberMoves.Edits(Module, plan));
        var intoTheBlock = Declarations + ValueText + "\r\n#If VBA7 Then\r\n" + FirstText + "\r\nPublic Sub Second()\r\nEnd Sub\r\n\r\nPublic Sub Third()\r\nEnd Sub\r\n#End If\r\n\r\nPublic Sub Last()\r\nEnd Sub\r\n";

        Assert.IsTrue(MemberMoves.Verify(plan, new ModuleParser().Parse(Document, moved)));
        Assert.IsFalse(MemberMoves.Verify(plan, parse), "not moved");
        Assert.IsFalse(MemberMoves.Verify(plan, new ModuleParser().Parse(Document, intoTheBlock)), "moved into a branch");
        Assert.IsFalse(MemberMoves.Verify(plan, new ModuleParser().Parse(Document, moved.Replace("End Sub\r\n\r\n#If", "End Sub\r\n\r\n#If (", StringComparison.Ordinal))), "does not parse");
    }

    [TestMethod]
    public async Task AClientWithNoDialog_IsSentTheMoveWithItsEdit()
    {
        var actions = await ActionsAsync(new Range(5, 4, 5, 4));

        var action = actions.Single();
        Assert.AreEqual(CodeActionHandler.RefactorMoveMemberDown, action.Kind);
        Assert.AreEqual("Move 'First' down", action.Title);
        var edits = action.Edit!.DocumentChanges?.Single().TextDocumentEdit!.Edits ?? action.Edit.Changes![DocumentUri.From(Document)];
        Assert.HasCount(2, edits);
    }

    [TestMethod]
    [DataRow(true, DisplayName = "a client that shows an action it cannot take")]
    [DataRow(false, DisplayName = "a client that does not")]
    public async Task ARefusedMove_IsOfferedDisabled_WithWhy_ToAClientThatShowsSo(bool showsDisabled)
    {
        var actions = await ActionsAsync(new Range(17, 0, 17, 0), showsDisabled: showsDisabled);

        Assert.IsNotNull(actions.SingleOrDefault(action => action.Kind == CodeActionHandler.RefactorMoveMemberDown)?.Edit);
        var up = actions.SingleOrDefault(action => action.Kind == CodeActionHandler.RefactorMoveMemberUp);
        if (!showsDisabled)
        {
            Assert.IsNull(up);
            return;
        }

        Assert.IsNull(up!.Edit);
        StringAssert.StartsWith(up.Disabled!.Reason, "'Second' is in a branch of an #If block");
    }

    [TestMethod]
    public async Task AClientThatResolvesEdits_IsSentTheMoveWithoutItsEdit_AndResolvesIt()
    {
        var handler = Handler(resolvesEdits: true);
        var action = (await ActionsAsync(handler, new Range(5, 4, 5, 4))).Single();
        Assert.IsNull(action.Edit);

        var resolved = await handler.Handle(action, CancellationToken.None);

        var edits = resolved.Edit!.DocumentChanges?.Single().TextDocumentEdit!.Edits ?? resolved.Edit.Changes![DocumentUri.From(Document)];
        Assert.HasCount(2, edits);
    }

    [TestMethod]
    public async Task AMoveResolvedAfterItsDocumentChanged_IsRefused()
    {
        var handler = Handler(resolvesEdits: true);
        var action = (await ActionsAsync(handler, new Range(5, 4, 5, 4))).Single();
        ((JObject)action.Data!)["version"] = Version - 1;

        var refused = await Assert.ThrowsExactlyAsync<RpcErrorException>(() => handler.Handle(action, CancellationToken.None));

        Assert.AreEqual(MoveMemberService.DocumentChanged(RefactoringKinds.MoveMemberDown), refused.Message);
    }

    private CodeActionHandler Handler(bool showsDisabled = false, bool resolvesEdits = false)
    {
        var handler = new CodeActionHandler(_rename, _documents, new System.IO.Abstractions.TestingHelpers.MockFileSystem(), new ExtractMethodService(_rename, _parsing,
            Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } })), Sut(),
            Substitute.For<IPlatformClientCapabilitiesService>(), new OmniSharp.Extensions.LanguageServer.Protocol.Serialization.LspSerializer());
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
        });
        return handler;
    }

    private Task<IReadOnlyList<CodeAction>> ActionsAsync(Range range, bool showsDisabled = false) => ActionsAsync(Handler(showsDisabled), range);

    // the moves a client is offered for a range, among the code actions of the document; the source actions, when asked for those.
    private static async Task<IReadOnlyList<CodeAction>> ActionsAsync(CodeActionHandler handler, Range range, Uri? document = null, string only = "refactor.move")
    {
        var actions = await handler.Handle(new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier(DocumentUri.From(document ?? Document)),
            Range = range,
            Context = new CodeActionContext { Diagnostics = new Container<Diagnostic>(), Only = new Container<CodeActionKind>(new CodeActionKind(only)) },
        }, CancellationToken.None);
        return [.. actions!.Select(action => action.CodeAction!)];
    }

    private static Uri UnsortedDocument => DocumentUri.From(Path.Combine(Root, "Unsorted.bas")).ToUri();

    [TestMethod]
    public async Task TheProceduresOfAModule_AreSortedByKind_ThenByName_NoneOfThemPastACommentOfItsOwn()
    {
        var answer = await Sut().SortAsync(UnsortedDocument, CancellationToken.None);

        Assert.IsNull(answer.Refusal, answer.Refusal);
        Assert.AreEqual(Version, answer.Version);
        Assert.AreEqual(Sorted, MemberMoves.Applied(Unsorted, answer.Edits!));
    }

    [TestMethod]
    public async Task AModuleWhoseProceduresAreSorted_IsOfferedNoSort()
    {
        Assert.IsNull(await Sut().SortOfferAsync(Document, CancellationToken.None));
        Assert.AreEqual("Sort procedures by kind, then by name", await Sut().SortOfferAsync(UnsortedDocument, CancellationToken.None));

        var refused = await Sut().SortAsync(Document, CancellationToken.None);
        Assert.IsNull(refused.Edits);
        Assert.AreEqual("The procedures of this module are sorted already.", refused.Refusal);
    }

    [TestMethod]
    public void NoProcedureIsSortedIntoABranch_OrOutOfOne()
    {
        const string text = "Option Explicit\r\n#If VBA7 Then\r\nPublic Sub Beta()\r\nEnd Sub\r\n#End If\r\n\r\nPublic Sub Alpha()\r\nEnd Sub\r\n";

        Assert.IsEmpty(MemberMoves.Sort(new ModuleParser().Parse(Document, text), text));
    }

    [TestMethod]
    public void TheVerificationOfASort_TakesOnlyTheModuleSorted()
    {
        var unsorted = new ModuleParser().Parse(UnsortedDocument, Unsorted);

        Assert.IsTrue(MemberMoves.VerifySort(unsorted, new ModuleParser().Parse(UnsortedDocument, Sorted), Sorted));
        Assert.IsFalse(MemberMoves.VerifySort(unsorted, unsorted, Unsorted), "not sorted");
        var lost = Sorted.Replace("Public Sub Alpha()\r\nEnd Sub\r\n\r\n", string.Empty, StringComparison.Ordinal);
        Assert.IsFalse(MemberMoves.VerifySort(unsorted, new ModuleParser().Parse(UnsortedDocument, lost), lost), "a procedure lost");
    }

    [TestMethod]
    [DataRow(false, DisplayName = "a client that does not resolve edits")]
    [DataRow(true, DisplayName = "a client that resolves them")]
    public async Task AClient_IsOfferedTheSort_AsASourceAction_WithItsEdit(bool resolvesEdits)
    {
        var handler = Handler(resolvesEdits: resolvesEdits);
        var action = (await ActionsAsync(handler, new Range(0, 0, 0, 0), UnsortedDocument, only: "source")).Single();
        Assert.AreEqual(CodeActionHandler.SourceSortMembers, action.Kind);
        Assert.AreEqual(resolvesEdits, action.Edit is null);

        var resolved = resolvesEdits ? await handler.Handle(action, CancellationToken.None) : action;

        var edits = resolved.Edit!.DocumentChanges?.Single().TextDocumentEdit!.Edits ?? resolved.Edit.Changes![DocumentUri.From(UnsortedDocument)];
        Assert.HasCount(2, edits);
    }

    [TestMethod]
    public async Task ASortResolvedAfterItsDocumentChanged_IsRefused()
    {
        var handler = Handler(resolvesEdits: true);
        var action = (await ActionsAsync(handler, new Range(0, 0, 0, 0), UnsortedDocument, only: "source")).Single();
        ((JObject)action.Data!)["version"] = Version - 1;

        var refused = await Assert.ThrowsExactlyAsync<RpcErrorException>(() => handler.Handle(action, CancellationToken.None));

        Assert.AreEqual(MoveMemberService.DocumentChanged(RefactoringKinds.SortMembers), refused.Message);
    }
}
