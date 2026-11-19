using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// Encapsulate Field asked of a document: the edit of the field's module, asked from its declaration or from a reference in another module, indented and
/// verified; the refusals, where the reference that is why is; and the calls that may write through a <c>ByRef</c> parameter, which the user accepts first.
/// </summary>
[TestClass]
public sealed class EncapsulateFieldServiceTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-encapsulatefield-ws");

    private const string Fields =
        "Option Explicit\r\n" +
        "Public Type TPoint\r\n" +
        "    X As Long\r\n" +
        "End Type\r\n" +
        "\r\n" +
        "Public count As Long\r\n" +
        "Private hidden As Long\r\n" +
        "Public Origin As TPoint\r\n" +
        "Public total As Currency\r\n" +
        "\r\n" +
        "Public Sub Reset()\r\n" +
        "    count = 0\r\n" +
        "End Sub\r\n";

    private const string Main =
        "Option Explicit\r\n" +
        "Private Sub Bump(ByRef n As Long)\r\n" +
        "    n = n + 1\r\n" +
        "End Sub\r\n" +
        "\r\n" +
        "Public Sub Run()\r\n" +
        "    Bump count\r\n" +
        "    Origin.X = 5\r\n" +
        "End Sub\r\n";

    private const string Widget =
        "VERSION 1.0 CLASS\r\n" +
        "BEGIN\r\n" +
        "  MultiUse = -1  'True\r\n" +
        "END\r\n" +
        "Attribute VB_Name = \"Widget\"\r\n" +
        "Option Explicit\r\n" +
        "Public Size As Long\r\n" +
        "Public Owner As Collection\r\n";

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();
    private readonly EncapsulateFieldService _service;

    public EncapsulateFieldServiceTests()
    {
        _documents.WorkspaceRoot.Returns(Root);
        var documents = new[]
        {
            new WorkspaceDocument("Fields.bas", Root, Fields, version: 3), new WorkspaceDocument("Main.bas", Root, Main, version: 1),
            new WorkspaceDocument("Widget.cls", Root, Widget, version: 1),
        };
        _documents.GetAllDocuments().Returns(documents);
        foreach (var document in documents)
        {
            var uri = document.Id.Uri.ToUri();
            _documents.TryGetDocument(uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = document; return true; });
            _parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new ModuleParser().Parse(uri, document.Text)));
        }

        _parsing.ParseFragmentAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ModuleParser().Parse(call.Arg<Uri>(), call.Arg<string>())));

        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StandardLibrary.LibraryName])).ProvideSymbols()]);
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });
        var rename = new RenameService(_documents, _parsing, new IntrinsicSymbolResolver(), libraries, options, NullLogger<RenameService>.Instance, OneProject.Registry());
        _service = new EncapsulateFieldService(rename, _parsing, options);
    }

    private static Uri Document(string fileName) => DocumentUri.From(Path.Combine(Root, fileName)).ToUri();

    [TestMethod]
    [DataRow("Fields.bas", 5, 8, DisplayName = "from its declaration")]
    [DataRow("Fields.bas", 11, 5, DisplayName = "from a reference in its module")]
    public async Task AField_IsEncapsulatedInItsModule_AtTheVersionOfItsDocument(string document, int line, int character)
    {
        var position = new SourcePosition(line, character);

        var answer = await _service.ApplyAsync(Document(document), position, [], [EncapsulateFieldService.PassedByRefCode], CancellationToken.None);

        Assert.IsNull(answer.Refusal, answer.Refusal);
        var edit = answer.Edit!.Documents.Single();
        Assert.AreEqual(Document("Fields.bas"), edit.Uri);
        Assert.AreEqual(3, edit.Version);
        var text = DeclarationEdits.ApplyTo(Fields, edit.Edits);
        StringAssert.Contains(text, "Private Type TFields\r\n    count As Long\r\nEnd Type\r\n\r\nPrivate this As TFields\r\n");
        StringAssert.Contains(text, "Public Sub Reset()\r\n    this.count = 0\r\nEnd Sub\r\n");
        StringAssert.Contains(text, "Public Property Let count(ByVal value As Long)\r\n    this.count = value\r\nEnd Property\r\n");
        Assert.DoesNotContain("Public count As Long", text);
    }

    [TestMethod]
    public async Task AField_IsEncapsulatedInItsModule_WhenAskedFromAReferenceInAnotherModule()
    {
        var answer = await _service.ApplyAsync(Document("Main.bas"), new SourcePosition(6, 10), [], [EncapsulateFieldService.PassedByRefCode], CancellationToken.None);

        Assert.IsNull(answer.Refusal, answer.Refusal);
        Assert.AreEqual(Document("Fields.bas"), answer.Edit!.Documents.Single().Uri);
    }

    [TestMethod]
    public async Task ACallThatMayWriteThroughAByRefParameter_IsAChange_ThatIsMadeOnlyWhenAccepted()
    {
        var prepared = await _service.PrepareAsync(Document("Fields.bas"), new SourcePosition(5, 8), CancellationToken.None);
        var declined = await _service.ApplyAsync(Document("Fields.bas"), new SourcePosition(5, 8), [], [], CancellationToken.None);

        var change = prepared.Notices.Single(notice => notice.Item is null);
        Assert.AreEqual(RefactorNoticeKind.Change, change.Kind);
        Assert.AreEqual(EncapsulateFieldService.PassedByRefCode, change.Code);
        Assert.AreEqual(new SourceLocation(Document("Main.bas"), new SourceRange(6, 9, 6, 14)), change.Location);
        Assert.IsNull(declined.Edit);
        StringAssert.Contains(declined.Refusal, change.Message);
    }

    [TestMethod]
    public async Task APrivateField_IsRefused()
    {
        var prepared = await _service.PrepareAsync(Document("Fields.bas"), new SourcePosition(6, 9), CancellationToken.None);
        var applied = await _service.ApplyAsync(Document("Fields.bas"), new SourcePosition(6, 9), [], [], CancellationToken.None);

        var refusal = prepared.Notices.Single();
        Assert.AreEqual(RefactorNoticeKind.Refusal, refusal.Kind);
        Assert.AreEqual("encapsulateField.notPublic", refusal.Code);
        StringAssert.Contains(refusal.Message, "'hidden'");
        Assert.AreEqual(refusal.Message, applied.Refusal);
    }

    [TestMethod]
    public async Task AUserDefinedTypeFieldWrittenInAnotherModule_IsRefused_WhereItIsWritten()
    {
        var prepared = await _service.PrepareAsync(Document("Fields.bas"), new SourcePosition(7, 8), CancellationToken.None);

        var refusal = prepared.Notices.Single();
        Assert.AreEqual("encapsulateField.writesThroughACopy", refusal.Code);
        Assert.AreEqual(Document("Main.bas"), refusal.Location?.Uri);
        StringAssert.StartsWith(refusal.Message, "Main.bas 8:5 ");
        StringAssert.Contains(refusal.Message, "'Origin'");
    }

    [TestMethod]
    public async Task TheChoices_AreAskedWithTheirDefaults_AndABackingFieldIsDeclaredWithTheNameAnswered()
    {
        var prepared = await _service.PrepareAsync(Document("Fields.bas"), new SourcePosition(5, 8), CancellationToken.None);
        CollectionAssert.AreEqual(
            new[]
            {
                EncapsulateFieldService.ModeChoice, EncapsulateFieldService.BackingFieldChoice, EncapsulateFieldService.ReadOnlyChoice,
                EncapsulateFieldService.ReadAccessChoice, EncapsulateFieldService.WriteAccessChoice, EncapsulateFieldService.InsideChoice,
                EncapsulateFieldService.FieldsChoice,
            },
            prepared.Choices.Select(choice => choice.Id).ToArray());
        Assert.AreEqual("mCount", prepared.Choices.Single(choice => choice.Id == EncapsulateFieldService.BackingFieldChoice).Default);

        RefactorAnswer[] answers =
        [
            new() { Id = EncapsulateFieldService.ModeChoice, Value = EncapsulateFieldService.BackingFieldMode },
            new() { Id = EncapsulateFieldService.BackingFieldChoice, Value = "tally" },
        ];
        var answer = await _service.ApplyAsync(Document("Fields.bas"), new SourcePosition(5, 8), answers, [EncapsulateFieldService.PassedByRefCode], CancellationToken.None);

        Assert.IsNull(answer.Refusal, answer.Refusal);
        var text = DeclarationEdits.ApplyTo(Fields, answer.Edit!.Documents.Single().Edits);
        StringAssert.Contains(text, "Private tally As Long\r\n");
        StringAssert.Contains(text, "    tally = 0\r\n");
        StringAssert.Contains(text, "Public Property Let count(ByVal value As Long)\r\n    tally = value\r\nEnd Property\r\n");
        Assert.DoesNotContain("Private Type", text);
    }

    [TestMethod]
    public async Task TheOtherPublicFields_AreAListOfItems_AndWhatRefusesOneIsOfTheAnswerThatPicksIt()
    {
        var prepared = await _service.PrepareAsync(Document("Fields.bas"), new SourcePosition(5, 8), CancellationToken.None);

        var fields = prepared.Choices.Single(choice => choice.Id == EncapsulateFieldService.FieldsChoice);
        Assert.AreEqual(RefactorChoiceKind.List, fields.Kind);
        CollectionAssert.AreEqual(new[] { "Origin", "total" }, fields.Items.Select(item => item.Id).ToArray());
        var total = fields.Items[1];
        Assert.AreEqual("Public total As Currency", total.Label);
        CollectionAssert.AreEqual(
            new[]
            {
                EncapsulateFieldService.EncapsulateChoice, EncapsulateFieldService.ReadOnlyChoice, EncapsulateFieldService.ReadAccessChoice,
                EncapsulateFieldService.WriteAccessChoice, EncapsulateFieldService.BackingFieldChoice,
            },
            total.Choices.Select(choice => choice.Id).ToArray());
        Assert.AreEqual("false", total.Choices[0].Default);
        Assert.AreEqual("mTotal", total.Choices[4].Default);
        var origin = prepared.Notices.Single(notice => notice.Item == "Origin");
        Assert.AreEqual(RefactorNoticeKind.Refusal, origin.Kind);
        Assert.AreEqual("encapsulateField.writesThroughACopy", origin.Code);
        Assert.AreEqual(EncapsulateFieldService.EncapsulateChoice, origin.Choice);
        Assert.IsFalse(prepared.Notices.Any(notice => notice.Item == "total"));
    }

    // the answer that picks other fields of the list, each with its own answers.
    private static RefactorAnswer Picked(params (string Field, RefactorAnswer[] Answers)[] fields) => new()
    {
        Id = EncapsulateFieldService.FieldsChoice,
        Items =
        [
            .. fields.Select(field => new RefactorItemAnswer
            {
                Id = field.Field,
                Answers = [new RefactorAnswer { Id = EncapsulateFieldService.EncapsulateChoice, Value = "true" }, .. field.Answers],
            }),
        ],
    };

    [TestMethod]
    public async Task TheFieldsPicked_AreEncapsulatedInTheSameEdit_EachWithItsOwnAnswers()
    {
        RefactorAnswer[] answers = [Picked(("total", [new RefactorAnswer { Id = EncapsulateFieldService.ReadOnlyChoice, Value = "true" }]))];

        var answer = await _service.ApplyAsync(Document("Fields.bas"), new SourcePosition(5, 8), answers, [EncapsulateFieldService.PassedByRefCode], CancellationToken.None);

        Assert.IsNull(answer.Refusal, answer.Refusal);
        var text = DeclarationEdits.ApplyTo(Fields, answer.Edit!.Documents.Single().Edits);
        StringAssert.Contains(text, "Private Type TFields\r\n    count As Long\r\n    total As Currency\r\nEnd Type\r\n");
        StringAssert.Contains(text, "Public Property Let count(ByVal value As Long)\r\n");
        StringAssert.Contains(text, "Public Property Get total() As Currency\r\n    total = this.total\r\nEnd Property\r\n");
        Assert.DoesNotContain("Property Let total", text);
        Assert.DoesNotContain("Public total As Currency", text);
    }

    [TestMethod]
    public async Task AFieldPicked_ThatIsRefused_RefusesTheEdit_AndAFieldThatIsNoneOfTheList_Too()
    {
        var origin = await _service.ApplyAsync(Document("Fields.bas"), new SourcePosition(5, 8), [Picked(("Origin", []))], [EncapsulateFieldService.PassedByRefCode], CancellationToken.None);
        var hidden = await _service.ApplyAsync(Document("Fields.bas"), new SourcePosition(5, 8), [Picked(("hidden", []))], [EncapsulateFieldService.PassedByRefCode], CancellationToken.None);

        Assert.IsNull(origin.Edit);
        StringAssert.Contains(origin.Refusal, "'Origin'");
        Assert.IsNull(hidden.Edit);
        StringAssert.Contains(hidden.Refusal, "'hidden'");
    }

    [TestMethod]
    public async Task TheAnswersThatPickAllTheFields_PickEveryItem_AndAreRefusedWhenOneIs()
    {
        var prepared = await _service.PrepareAsync(Document("Fields.bas"), new SourcePosition(5, 8), CancellationToken.None);

        var all = EncapsulateFieldService.AllFields(prepared.Choices);
        var answer = await _service.ApplyAsync(Document("Fields.bas"), new SourcePosition(5, 8), all, [EncapsulateFieldService.PassedByRefCode], CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "Origin", "total" }, all.Single().Items.Select(item => item.Id).ToArray());
        Assert.IsNull(answer.Edit);
        StringAssert.Contains(answer.Refusal, "'Origin'");
        Assert.IsEmpty(EncapsulateFieldService.AllFields([]));
    }

    [TestMethod]
    public async Task ABackingFieldName_TypedForAFieldPicked_IsCheckedAsItIsTyped()
    {
        RefactorAnswer[] answers =
        [
            new() { Id = EncapsulateFieldService.ModeChoice, Value = EncapsulateFieldService.BackingFieldMode },
            Picked(("total", [new RefactorAnswer { Id = EncapsulateFieldService.BackingFieldChoice, Value = "hidden" }])),
        ];

        var checkedName = await _service.CheckIdentifierAsync(Document("Fields.bas"), new SourcePosition(5, 8), answers, CancellationToken.None);

        var notice = checkedName.Notices.Single();
        Assert.AreEqual("encapsulateField.nameTaken", notice.Code);
        Assert.AreEqual(EncapsulateFieldService.BackingFieldChoice, notice.Choice);
        Assert.AreEqual("total", notice.Item);
    }

    [TestMethod]
    [DataRow("hidden", "encapsulateField.nameTaken", DisplayName = "a name of the module")]
    [DataRow("Dim", "encapsulateField.notAName", DisplayName = "a keyword")]
    public async Task ABackingFieldName_ThatCannotBeDeclared_IsRefusedAsItIsTyped(string typed, string code)
    {
        RefactorAnswer[] answers =
        [
            new() { Id = EncapsulateFieldService.ModeChoice, Value = EncapsulateFieldService.BackingFieldMode },
            new() { Id = EncapsulateFieldService.BackingFieldChoice, Value = typed },
        ];

        var checkedName = await _service.CheckIdentifierAsync(Document("Fields.bas"), new SourcePosition(5, 8), answers, CancellationToken.None);
        var thisMode = await _service.CheckIdentifierAsync(Document("Fields.bas"), new SourcePosition(5, 8), [answers[1]], CancellationToken.None);

        var notice = checkedName.Notices.Single();
        Assert.AreEqual(code, notice.Code);
        Assert.AreEqual(EncapsulateFieldService.BackingFieldChoice, notice.Choice);
        Assert.IsEmpty(thisMode.Notices);
    }

    [TestMethod]
    public async Task AnAnswerThatIsNoValueOfItsChoice_IsRefused()
    {
        var answer = await _service.ApplyAsync(Document("Fields.bas"), new SourcePosition(5, 8),
            [new RefactorAnswer { Id = EncapsulateFieldService.InsideChoice, Value = "elsewhere" }], [EncapsulateFieldService.PassedByRefCode], CancellationToken.None);

        Assert.IsNull(answer.Edit);
        StringAssert.Contains(answer.Refusal, "'elsewhere'");
    }

    [TestMethod]
    public async Task APositionThatNamesNoField_IsRefused()
    {
        var prepared = await _service.PrepareAsync(Document("Main.bas"), new SourcePosition(5, 12), CancellationToken.None);

        Assert.AreEqual("encapsulateField.notAField", prepared.Notices.Single().Code);
    }

    private static (string Default, string[] Options) AccessOf(IEnumerable<RefactorChoice> choices, string id)
        => choices.Single(choice => choice.Id == id) is var choice ? (choice.Default!, [.. choice.Options.Select(option => option.Value)]) : default;

    [TestMethod]
    public async Task TheAccessChoices_OfAClassModulesField_OfferFriend_AndTheWritersAreFriendByDefault()
    {
        var prepared = await _service.PrepareAsync(Document("Widget.cls"), new SourcePosition(6, 8), CancellationToken.None);
        var answer = await _service.ApplyAsync(Document("Widget.cls"), new SourcePosition(6, 8), [], [EncapsulateFieldService.PassedByRefCode], CancellationToken.None);

        var (read, readOptions) = AccessOf(prepared.Choices, EncapsulateFieldService.ReadAccessChoice);
        var (write, writeOptions) = AccessOf(prepared.Choices, EncapsulateFieldService.WriteAccessChoice);
        Assert.AreEqual(EncapsulateFieldService.PublicAccess, read);
        Assert.AreEqual(EncapsulateFieldService.FriendAccess, write);
        string[] accesses = [EncapsulateFieldService.PublicAccess, EncapsulateFieldService.FriendAccess, EncapsulateFieldService.PrivateAccess];
        CollectionAssert.AreEqual(accesses, readOptions);
        CollectionAssert.AreEqual(accesses, writeOptions);
        Assert.IsNull(answer.Refusal, answer.Refusal);
        var text = DeclarationEdits.ApplyTo(Widget, answer.Edit!.Documents.Single().Edits);
        StringAssert.Contains(text, "Public Property Get Size() As Long\r\n");
        StringAssert.Contains(text, "Friend Property Let Size(ByVal value As Long)\r\n");
    }

    [TestMethod]
    public async Task TheAccessChoices_OfAStandardModulesField_OfferNoFriend_AndArePublicByDefault()
    {
        var prepared = await _service.PrepareAsync(Document("Fields.bas"), new SourcePosition(5, 8), CancellationToken.None);

        var total = prepared.Choices.Single(choice => choice.Id == EncapsulateFieldService.FieldsChoice).Items[1];
        string[] accesses = [EncapsulateFieldService.PublicAccess, EncapsulateFieldService.PrivateAccess];
        foreach (var choices in new[] { prepared.Choices, total.Choices })
        {
            foreach (var id in new[] { EncapsulateFieldService.ReadAccessChoice, EncapsulateFieldService.WriteAccessChoice })
            {
                var (access, options) = AccessOf(choices, id);
                Assert.AreEqual(EncapsulateFieldService.PublicAccess, access, id);
                CollectionAssert.AreEqual(accesses, options, id);
            }
        }
    }

    [TestMethod]
    [DataRow(EncapsulateFieldService.ReadAccessChoice, DisplayName = "a Friend Get")]
    [DataRow(EncapsulateFieldService.WriteAccessChoice, DisplayName = "a Friend Let")]
    public async Task AFriendAccessAnswered_ForAStandardModulesField_IsRefused(string id)
    {
        var answer = await _service.ApplyAsync(Document("Fields.bas"), new SourcePosition(5, 8),
            [new RefactorAnswer { Id = id, Value = EncapsulateFieldService.FriendAccess }], [EncapsulateFieldService.PassedByRefCode], CancellationToken.None);

        Assert.IsNull(answer.Edit);
        StringAssert.Contains(answer.Refusal, "'count'");
        StringAssert.Contains(answer.Refusal, "Friend");
    }

    [TestMethod]
    public async Task TheAccessAnswered_ForAFieldPicked_DeclaresItsOwnProperties()
    {
        var prepared = await _service.PrepareAsync(Document("Widget.cls"), new SourcePosition(6, 8), CancellationToken.None);
        RefactorAnswer[] answers =
        [
            new() { Id = EncapsulateFieldService.ReadAccessChoice, Value = EncapsulateFieldService.FriendAccess },
            Picked(("Owner", [new RefactorAnswer { Id = EncapsulateFieldService.WriteAccessChoice, Value = EncapsulateFieldService.PrivateAccess }])),
        ];
        var answer = await _service.ApplyAsync(Document("Widget.cls"), new SourcePosition(6, 8), answers, [EncapsulateFieldService.PassedByRefCode], CancellationToken.None);

        var owner = prepared.Choices.Single(choice => choice.Id == EncapsulateFieldService.FieldsChoice).Items.Single();
        Assert.AreEqual(EncapsulateFieldService.FriendAccess, AccessOf(owner.Choices, EncapsulateFieldService.WriteAccessChoice).Default);
        Assert.IsNull(answer.Refusal, answer.Refusal);
        var text = DeclarationEdits.ApplyTo(Widget, answer.Edit!.Documents.Single().Edits);
        StringAssert.Contains(text, "Friend Property Get Size() As Long\r\n");
        StringAssert.Contains(text, "Friend Property Let Size(ByVal value As Long)\r\n");
        StringAssert.Contains(text, "Public Property Get Owner() As Collection\r\n");
        StringAssert.Contains(text, "Private Property Set Owner(ByVal value As Collection)\r\n");
    }
}
