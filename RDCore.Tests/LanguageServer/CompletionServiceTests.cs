using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Completion;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Platform.Commands;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;
using System.IO.Abstractions;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The completion list at a position of a module: what is typed on the line need not parse, the rest of the module and the workspace say what the names are, and
/// the type of what a member is accessed on says what its members are.
/// </summary>
[TestClass]
public sealed class CompletionServiceTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-completion-ws");

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();

    private const string Widget = """
        VERSION 1.0 CLASS
        BEGIN
          MultiUse = -1  'True
        END
        Attribute VB_Name = "Widget"
        Public Name As String
        Private Secret As Long

        Public Function Grow(ByVal By As Long) As Widget
        End Function

        Public Property Get Parts() As Collection
        End Property
        """;

    private const string Helpers = """
        Attribute VB_Name = "Helpers"
        Public Function Twice(ByVal Value As Long) As Long
        Attribute Twice.VB_Description = "Doubles a ""value""."
            Twice = Value * 2
        End Function
        """;

    public CompletionServiceTests()
    {
        _parsing.ParseFragmentAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ModuleParser().Parse(call.Arg<Uri>(), call.Arg<string>())));

        var widget = new WorkspaceDocument("Widget.cls", Root, Widget);
        var helpers = new WorkspaceDocument("Helpers.bas", Root, Helpers);
        _documents.WorkspaceRoot.Returns(Root);
        WorkspaceHas(widget, helpers);
    }

    private void WorkspaceHas(params WorkspaceDocument[] documents)
    {
        _documents.GetAllDocuments().Returns(documents);
        foreach (var document in documents)
        {
            var uri = document.Id.Uri.ToUri();
            _documents.TryGetDocument(uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = document; return true; });
            var parse = new ModuleParser().Parse(uri, document.Text);
            _parsing.TryGetCached(uri, out Arg.Any<ModuleParseResult>()).Returns(call => { call[1] = parse; return true; });
        }
    }

    // a library of the environment, whose class is created by name.
    private static readonly LibraryDescription Lab = new()
    {
        Name = "Lab",
        Classes =
        [
            new ClassDescription
            {
                Name = "Shelf",
                ProgId = "Lab.Shelf",
                IsCreatable = true,
                Members =
                [
                    new MemberDescription { Name = "Label", Kind = MemberKind.PropertyGet, Type = "String" },
                    new MemberDescription { Name = "Fill", Kind = MemberKind.Method },
                ],
            },
            new ClassDescription
            {
                Name = "Burner",
                ProgId = "Lab.Burner",
                IsCreatable = true,
                Members = [new MemberDescription { Name = "Ignite", Kind = MemberKind.Method }],
            },
        ],
    };

    private CompletionService Sut(string language = "vba", ILibrarySource? source = null, params string[] references)
    {
        source ??= new InMemoryLibrarySource(Lab);
        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(source, references.Select(name => new LibraryIdentity(name)), [StandardLibrary.LibraryName])).ProvideSymbols()]);
        libraries.DescribedSymbolsOf(Arg.Any<Uri>(), Arg.Any<string>()).Returns(call => LibrarySymbolProvider.ReadDescribed(source, call.Arg<Uri>(), call.Arg<string>()));
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = language } });
        return new CompletionService(_documents, _parsing, new IntrinsicSymbolResolver(), libraries, source, _verbs, options, NullLogger<CompletionService>.Instance, OneProject.Registry());
    }

    private readonly ClientVerbRegistry _verbs = new();

    // the module with the cursor where the `|` is, which is taken out of the source.
    private static (string Source, Position Position) At(string source)
    {
        var lines = source.ReplaceLineEndings("\r\n").Split("\r\n");
        var line = Array.FindIndex(lines, text => text.Contains('|'));
        var position = new Position(line, lines[line].IndexOf('|'));
        lines[line] = lines[line].Replace("|", string.Empty);
        return (string.Join("\r\n", lines), position);
    }

    private async Task<string[]> LabelsAsync(string module, ILibrarySource? libraries = null, params string[] references)
    {
        var (source, position) = At(module);
        var list = await Sut(source: libraries, references: references).CompleteFragmentAsync("Main", source, position, CancellationToken.None);
        return [.. list.Items.Select(item => item.Label)];
    }

    // the module of a document the client holds; what it completes at the caret.
    private async Task<string[]> DocumentLabelsAsync(WorkspaceDocument document, Position position)
    {
        var uri = document.Id.Uri.ToUri();
        _documents.TryGetDocument(uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = document; return true; });
        var list = await Sut().CompleteDocumentAsync(uri, position, CancellationToken.None);
        return [.. list.Items.Select(item => item.Label)];
    }

    private const string Untitled = "Public Sub Run()\r\n    Dim w As \r\nEnd Sub\r\n";

    // LSP 3.17 §URI: a document need not be a file. One that is not is a project of its own: the workspace's classes are no types of it.
    [TestMethod]
    public async Task ADocumentThatIsNoFile_IsCompleted_AsAProjectOfItsOwn()
    {
        var labels = await DocumentLabelsAsync(new WorkspaceDocument(new Uri("untitled:Untitled-1"), Root, Untitled), new Position(1, 13));

        CollectionAssert.Contains(labels, "Long");
        CollectionAssert.DoesNotContain(labels, "Widget");
    }

    // a client may start a server on no folder (rootUri null): a document it opens is completed all the same, where it was an internal error.
    [TestMethod]
    public async Task ADocumentOfAServerWithNoWorkspace_IsCompleted()
    {
        _documents.WorkspaceRoot.Returns(string.Empty);
        var alone = new WorkspaceDocument(Path.Combine(Path.GetTempPath(), "rdcore-alone", "Module1.bas"), string.Empty, Untitled);

        var labels = await DocumentLabelsAsync(alone, new Position(1, 13));

        CollectionAssert.Contains(labels, "Long");
    }

    // ---- an argument whose parameter is an Enum: its constants first ----

    private const string Painter = """
        Attribute VB_Name = "Painter"
        Public Enum Shade
            Light
            Dark
        End Enum
        Public Enum Hue
            DeepRed
            Pale
        End Enum
        Public Sub Paint(ByVal Colour As Shade)
        End Sub
        Public Sub Brush(ByVal Width As Long, Optional ByVal Colour As Shade = Light)
        End Sub
        Public Sub Stroke(ByVal Width As Long)
        End Sub
        Public Sub Tint(ByVal Colour As Hue)
        End Sub
        Public Function Drum() As Long
        End Function
        """;

    private async Task<CompletionList> ListAsync(string line, string? triggerCharacter = null)
    {
        WorkspaceHas(new WorkspaceDocument("Painter.bas", Root, Painter));
        var (source, position) = At($"Attribute VB_Name = \"Main\"\r\nPublic Sub Main()\r\n    {line}\r\nEnd Sub\r\n");
        return await Sut().CompleteFragmentAsync("Main", source, position, CancellationToken.None, triggerCharacter);
    }

    [TestMethod]
    public async Task AnArgumentOfMsgBoxsButtons_IsOfferedTheConstantsOfVbMsgBoxStyle_First_TheFirstSelected_ThenTheUsualList()
    {
        var items = (await ListAsync("MsgBox \"Hello\", |")).Items.ToList();

        Assert.AreEqual("vbAbortRetryIgnore", items[0].Label, "VbMsgBoxStyle's first constant by name");
        Assert.IsTrue(items[0].Preselect);
        var constants = items.TakeWhile(IsConstantFirst).Select(item => item.Label).ToList();
        // names in any case, as VBA reads them: the standard library spells two of them vbOkOnly and vbOkCancel.
        CollectionAssert.IsSubsetOf(new[] { "VBOKONLY", "VBOKCANCEL", "VBYESNO", "VBCRITICAL", "VBEXCLAMATION" }, constants.Select(constant => constant.ToUpperInvariant()).ToArray());
        Assert.IsTrue(constants.All(constant => items.Count(item => item.Label == constant) == 1), "a constant is offered once, first");
        CollectionAssert.Contains(items.Skip(constants.Count).Select(item => item.Label).ToArray(), "Len", "then the usual list");
        Assert.IsTrue(items.Skip(1).All(item => !item.Preselect));
    }

    [TestMethod]
    [DataRow("Paint |", DisplayName = "a call statement's first argument")]
    [DataRow("Paint(|", DisplayName = "in parentheses")]
    [DataRow("Brush 1, |", DisplayName = "an Optional parameter")]
    [DataRow("Brush Colour:=|", DisplayName = "a named argument")]
    [DataRow("Painter.Paint |", DisplayName = "a call qualified by its module")]
    public async Task AnArgumentOfAUserEnumParameter_IsOfferedTheEnumsConstants_First(string line)
    {
        var items = (await ListAsync(line)).Items.ToList();

        CollectionAssert.AreEqual(new[] { "Dark", "Light" }, items.Take(2).Select(item => item.Label).ToArray(),
            $"by name: {string.Join(", ", items.Take(5).Select(item => item.Label + "/" + item.SortText))}");
        Assert.IsTrue(items[0].Preselect);
        Assert.IsTrue(items.Count > 2, "then the usual list");
    }

    [TestMethod]
    public async Task ConstantsBeingTyped_AreNarrowedLikeAnyName()
    {
        var items = (await ListAsync("Paint Da|")).Items.ToList();

        CollectionAssert.AreEqual(new[] { "Dark" }, items.Where(IsConstantFirst).Select(item => item.Label).ToArray(),
            string.Join(", ", items.Take(8).Select(item => item.Label + "/" + item.SortText)));
    }

    [TestMethod]
    public async Task AnArgumentOfAParameterThatIsNoEnum_IsOfferedTheUsualList()
    {
        var items = (await ListAsync("Stroke |")).Items.ToList();

        Assert.IsFalse(items.Any(IsConstantFirst));
        CollectionAssert.Contains(items.Select(item => item.Label).ToArray(), "Len");
    }

    // the constants of the Enum are a tier before every other name: the word's best match among them is selected, though it only matches a constant's humps
    // and starts another name.
    [TestMethod]
    public async Task AConstantWhoseHumpsTheWordMatches_RanksAndIsSelected_BeforeANameTheWordStarts()
    {
        var items = (await ListAsync("Tint DR|")).Items.ToList();

        Assert.AreEqual("DeepRed", items[0].Label, string.Join(", ", items.Take(5).Select(item => item.Label + "/" + item.SortText)));
        Assert.IsTrue(items[0].Preselect);
        CollectionAssert.Contains(items.Select(item => item.Label).ToArray(), "Drum");
    }

    // the sort text of a constant ranked first: a `!` before any other item's, and the first group.
    private static bool IsConstantFirst(CompletionItem item) => item.SortText is ['!', '0', ..];

    // a ( , or space a client asks on lists an Enum argument's constants where a value of it starts, and nothing anywhere else: not a whole list at each comma,
    // nor after an operand.
    [TestMethod]
    [DataRow("Paint |", " ", true)]
    [DataRow("Brush 1,|", ",", true)]
    [DataRow("Paint(|", "(", true)]
    [DataRow("Paint Light + |", " ", true)]
    [DataRow("Paint Light |", " ", false)]
    [DataRow("Stroke |", " ", false)]
    [DataRow("Dim x As Long: x = 1 |", " ", false)]
    public async Task AnArgumentTriggerCharacter_IsAnsweredOnlyForAnEnumOrAnOptionalArgument(string line, string trigger, bool listed)
    {
        var items = (await ListAsync(line, trigger)).Items.ToList();

        Assert.AreEqual(listed, items.Count > 0, string.Join(", ", items.Take(5).Select(item => item.Label)));
        if (listed)
        {
            Assert.AreEqual("Dark", items[0].Label);
        }
    }

    // ---- named arguments: the parameters not given yet, as `Name:=` (MS-VBAL §5.6.13.1, §5.3.1.11) ----

    private const string Printer = """
        Attribute VB_Name = "Printer"
        Public Declare PtrSafe Sub Ring Lib "bell" (ByVal Tone As Long, Optional ByVal Length As Long)
        Public Enum Tray
            Upper
            Lower
        End Enum
        Public Sub Send(ByVal Text As String, Optional ByVal Heading As String, Optional ByVal Copies As Long = 1, Optional ByVal Source As Tray)
        End Sub
        Public Function Count(ByVal Text As String, Optional ByVal Copies As Long) As Long
        End Function
        Public Sub Gather(Optional ByVal Title As String, ParamArray Items() As Variant)
        End Sub
        """;

    private const string Spooler = """
        VERSION 1.0 CLASS
        BEGIN
          MultiUse = -1  'True
        END
        Attribute VB_Name = "Spooler"
        Public Sub Queue(ByVal Text As String, Optional ByVal Copies As Long)
        End Sub
        """;

    // a class whose default member takes arguments, and one whose default member takes none and returns the first (MS-VBAL §5.6.13).
    private const string Picker = """
        VERSION 1.0 CLASS
        BEGIN
          MultiUse = -1  'True
        END
        Attribute VB_Name = "Picker"
        Public Function Pick(Optional ByVal A As Long, Optional ByVal B As Long) As Long
        Attribute Pick.VB_UserMemId = 0
        End Function
        Public Property Get Items() As Picker
        End Property
        """;

    private const string Holder = """
        VERSION 1.0 CLASS
        BEGIN
          MultiUse = -1  'True
        END
        Attribute VB_Name = "Holder"
        Public Property Get Inner() As Picker
        Attribute Inner.VB_UserMemId = 0
        End Property
        """;

    private const string NamedMain = "Attribute VB_Name = \"Main\"\r\nPublic Sub Main()\r\n    Dim s As New Spooler\r\n    Dim p As New Picker\r\n    Dim h As New Holder\r\n"
        + "    Dim c As New Collection\r\n    Dim arr(3) As Long\r\n    {0}\r\nEnd Sub\r\n";

    private void WorkspaceHasThePrinter()
        => WorkspaceHas(new WorkspaceDocument("Printer.bas", Root, Printer), new WorkspaceDocument("Spooler.cls", Root, Spooler),
            new WorkspaceDocument("Picker.cls", Root, Picker), new WorkspaceDocument("Holder.cls", Root, Holder));

    private async Task<List<CompletionItem>> NamedAsync(string line, string? triggerCharacter = null)
    {
        WorkspaceHasThePrinter();
        var (source, position) = At(string.Format(NamedMain, line));
        return [.. (await Sut().CompleteFragmentAsync("Main", source, position, CancellationToken.None, triggerCharacter)).Items];
    }

    private async Task<SignatureHelp?> CallSignatureAsync(string line)
    {
        WorkspaceHasThePrinter();
        var (source, position) = At(string.Format(NamedMain, line));
        return await Sut().SignatureHelpFragmentAsync("Main", source, position, CancellationToken.None);
    }

    private static string[] NamesIn(IEnumerable<CompletionItem> items) => [.. items.Select(item => item.Label).Where(label => label.EndsWith(":=", StringComparison.Ordinal))];

    private static string Show(IEnumerable<CompletionItem> items) => string.Join(", ", items.Take(8).Select(item => item.Label + "/" + item.SortText));

    [TestMethod]
    [DataRow("Send \"a\", |", "Heading:=;Copies:=;Source:=", DisplayName = "a call statement")]
    [DataRow("x = Count(\"a\", |", "Copies:=", DisplayName = "a call in parentheses")]
    [DataRow("s.Queue \"a\", |", "Copies:=", DisplayName = "a member call")]
    [DataRow("Gather |", "Title:=", DisplayName = "a ParamArray is never named")]
    [DataRow("Ring 1, |", "Length:=", DisplayName = "a Declare'd procedure")]
    [DataRow("Send \"a\", Co|", "Copies:=", DisplayName = "narrowed by the word typed")]
    [DataRow("Send |", "", DisplayName = "a required parameter")]
    [DataRow("Send \"a\", Copies:=|", "", DisplayName = "the value of a named argument")]
    [DataRow("Send \"a\", 1 + |", "", DisplayName = "inside an argument")]
    public async Task WhereAnOptionalArgumentStarts_TheParametersNotGivenYetAreOffered_ByName(string line, string names)
    {
        var items = await NamedAsync(line);

        CollectionAssert.AreEqual(names.Split(';', StringSplitOptions.RemoveEmptyEntries), NamesIn(items), Show(items));
    }

    [TestMethod]
    public async Task ANamedArgument_IsItsParametersName_InsertedWithItsToken_AndSaysWhatTheParameterIs()
    {
        var item = (await NamedAsync("x = Count(\"a\", |")).Single(item => item.Label == "Copies:=");

        Assert.IsNull(item.InsertText, "the label is what is inserted");
        Assert.AreEqual("Optional ByVal Copies As Long", item.Detail);
    }

    [TestMethod]
    public async Task AnArgumentOfTheStandardLibrary_IsOfferedItsParametersNames_AfterTheEnumsConstants()
    {
        var items = await NamedAsync("MsgBox \"Hi\", |");
        var names = NamesIn(items).Select(name => name.ToUpperInvariant()).ToArray();

        CollectionAssert.IsSubsetOf(new[] { "BUTTONS:=", "TITLE:=" }, names, Show(items));
        CollectionAssert.DoesNotContain(names, "PROMPT:=");
        Assert.AreEqual("vbAbortRetryIgnore", items[0].Label, "the constants first");
    }

    [TestMethod]
    public async Task TheNames_RankAfterTheEnumsConstants_AndBeforeTheNamesInScope()
    {
        var labels = (await NamedAsync("Send \"a\", , , |")).Select(item => item.Label).ToList();

        CollectionAssert.AreEqual(new[] { "Lower", "Upper", "Source:=" }, labels.Take(3).ToArray(), string.Join(", ", labels.Take(6)));
        Assert.IsTrue(labels.IndexOf("Source:=") < labels.IndexOf("Len"));
    }

    [TestMethod]
    public async Task AParameterGivenByPosition_IsNotOfferedByName()
    {
        var items = await NamedAsync("Send \"a\", \"b\", |");

        CollectionAssert.AreEqual(new[] { "Copies:=", "Source:=" }, NamesIn(items), Show(items));
    }

    // MS-VBAL §5.6.13.1: the named arguments come after the positional ones, so nothing but a name is written after one.
    [TestMethod]
    public async Task AParameterNamedBeforeTheCursor_IsNotOfferedByName_AndNothingButNamesIsOfferedAfterIt()
    {
        var items = await NamedAsync("Send \"a\", copies:=2, |");

        CollectionAssert.AreEqual(new[] { "Heading:=", "Source:=" }, items.Select(item => item.Label).ToArray(), Show(items));
    }

    [TestMethod]
    public async Task AParameterNamedAfterTheCursor_IsNotOfferedByName()
    {
        var items = await NamedAsync("Send \"a\", |, Copies:=2");

        CollectionAssert.AreEqual(new[] { "Heading:=", "Source:=" }, NamesIn(items), Show(items));
        CollectionAssert.Contains(items.Select(item => item.Label).ToArray(), "Len", "a value is still written by position there");
    }

    [TestMethod]
    public async Task AParameterGivenByPosition_NamedBefore_OrNamedAfter_IsNotOfferedByName_InAnyCase()
    {
        var items = await NamedAsync("Send \"a\", heading:=\"h\", |, COPIES:=2");

        CollectionAssert.AreEqual(new[] { "Source:=" }, items.Select(item => item.Label).ToArray(), Show(items));
    }

    [TestMethod]
    public async Task TheCommaBeforeAnOptionalArgument_AsksForTheNames()
    {
        var items = await NamedAsync("x = Count(\"a\",|", ",");

        CollectionAssert.AreEqual(new[] { "Copies:=" }, NamesIn(items), Show(items));
    }

    // ---- an argument an array is given to: LBound, UBound (MS-VBAL §3.3.5.2), and a parameter declared as an array (MS-VBAL §5.3.1.11) ----

    private const string Shelves = """
        Attribute VB_Name = "Shelves"
        Public Type Rack
            Slots() As Long
            Label As String
        End Type
        Public Books() As String
        Public Fixed(1 To 3) As Long
        Public Anything As Variant
        Public Total As Long
        Public Holder As Collection
        Public Const Limit As Long = 3
        Public Enum Side
            LeftSide
            RightSide
        End Enum
        Public Enum Hand
            OneHand
        End Enum
        Public Weights() As Long
        Public Prices() As Double
        Public FixedTexts(1 To 2) As String * 3
        Public Sides() As Side
        Public Hands() As Hand
        Public SpanMod() As Long
        Public AbsMod() As Long
        Public Function Titles() As String()
        End Function
        Public Function Counts() As Long()
        End Function
        Public Function Loose() As Variant
        End Function
        Public Function Size() As Long
        End Function
        Public Function SpanFn() As Long()
        End Function
        Public Function SpanVar() As Variant
        End Function
        Public Sub Tidy()
        End Sub
        Public Sub Weigh(ByRef Amounts() As Long, Optional ByVal Scale As Long)
        End Sub
        Public Sub TakeTexts(ByRef Texts() As String)
        End Sub
        Public Sub TakeSides(ByRef Choices() As Side)
        End Sub
        """;

    // an object that leads to an array through a member of a member.
    private const string Bin = """
        VERSION 1.0 CLASS
        BEGIN
          MultiUse = -1  'True
        END
        Attribute VB_Name = "Bin"
        Public Property Get Slots() As Long()
        End Property
        """;

    private const string Crate = """
        VERSION 1.0 CLASS
        BEGIN
          MultiUse = -1  'True
        END
        Attribute VB_Name = "Crate"
        Public Property Get Inner() As Bin
        End Property
        """;

    private async Task<List<CompletionItem>> ArrayArgumentItemsAsync(string line, string declarations)
    {
        WorkspaceHas(new WorkspaceDocument("Shelves.bas", Root, Shelves), new WorkspaceDocument("Bin.cls", Root, Bin), new WorkspaceDocument("Crate.cls", Root, Crate));
        var (source, position) = At($"Attribute VB_Name = \"Main\"\r\n{declarations}\r\n    {line}\r\nEnd Sub\r\n");
        return [.. (await Sut().CompleteFragmentAsync("Main", source, position, CancellationToken.None)).Items];
    }

    private async Task<string[]> ArrayArgumentAsync(string line)
        => [.. (await ArrayArgumentItemsAsync(line,
            "Public Sub Main()\r\n    Dim r As Rack\r\n    Dim o As New Crate\r\n    Dim Bytes() As Byte\r\n    Dim Total2 As Long")).Select(item => item.Label)];

    // what is not an array, nor a Variant that may hold one, nor leads to an array, is "Expected array" there: a scalar, an object, a constant, an Enum and
    // its members, a Sub, a module, a keyword.
    private static readonly string[] NoArray = ["Total", "Total2", "Holder", "Limit", "Side", "LeftSide", "Size", "Tidy", "Weigh", "Shelves", "Main", "Not", "New", "True"];

    [TestMethod]
    [DataRow("x = UBound(|", DisplayName = "UBound")]
    [DataRow("x = LBound(|", DisplayName = "LBound")]
    [DataRow("x = UBound( |", DisplayName = "after a space")]
    [DataRow("If LBound(| Then", DisplayName = "in an expression")]
    public async Task TheFirstArgumentOfLBoundOrUBound_IsOfferedTheArraysAndTheVariantsInScope_AndNothingElse(string line)
    {
        var labels = await ArrayArgumentAsync(line);

        CollectionAssert.IsSubsetOf(new[] { "Books", "Fixed", "Anything", "Weights", "Prices", "Titles", "Counts", "Loose", "Bytes" }, labels, string.Join(", ", labels.Take(20)));
        Assert.IsFalse(labels.Intersect(NoArray).Any(), string.Join(", ", labels.Intersect(NoArray)));
    }

    [TestMethod]
    public async Task TheArraysOfferedToUBound_AreNarrowedByTheWordTyped()
        => CollectionAssert.AreEqual(new[] { "Bytes", "Books" }, await ArrayArgumentAsync("x = UBound(B|"));

    [TestMethod]
    public async Task TheSecondArgumentOfUBound_IsOfferedTheUsualList()
        => CollectionAssert.IsSubsetOf(new[] { "Total", "Total2", "Limit", "Size" }, await ArrayArgumentAsync("x = UBound(Books, |"));

    [TestMethod]
    public async Task TheMembersOfferedToUBound_AreTheArrayFieldsOfAUserDefinedType()
        => CollectionAssert.AreEqual(new[] { "Slots" }, await ArrayArgumentAsync("x = UBound(r.|"));

    // a parameter declared as an array is ByRef, and an argument passed ByRef is of its parameter's declared type exactly (MS-VBAL §5.3.1.11): an array of
    // the same element type, not a Variant.
    [TestMethod]
    public async Task AParameterDeclaredAsAnArray_IsOfferedTheArraysOfItsElementType_AndNothingElse()
    {
        var labels = await ArrayArgumentAsync("Weigh |");

        CollectionAssert.IsSubsetOf(new[] { "Weights", "Counts", "Fixed" }, labels, string.Join(", ", labels.Take(20)));
        var others = new[] { "Prices", "Books", "Bytes", "Titles", "Anything", "Loose" }.Concat(NoArray).ToArray();
        Assert.IsFalse(labels.Intersect(others).Any(), string.Join(", ", labels.Intersect(others)));
    }

    [TestMethod]
    public async Task TheArgumentAfterTheArray_IsOfferedTheUsualList()
        => CollectionAssert.IsSubsetOf(new[] { "Total", "Limit", "Scale:=" }, await ArrayArgumentAsync("Weigh Weights, |"));

    [TestMethod]
    public async Task ANamedArgumentDeclaredAsAnArray_IsOfferedTheArraysOfItsElementType()
    {
        var labels = await ArrayArgumentAsync("Weigh Amounts:=|");

        CollectionAssert.IsSubsetOf(new[] { "Weights", "Counts" }, labels, string.Join(", ", labels.Take(20)));
        Assert.IsFalse(labels.Intersect(new[] { "Prices", "Total", "Anything" }).Any(), string.Join(", ", labels.Take(20)));
    }

    // the element types are the compiler's (ArgumentListStaticSemantics): a String * N is no String, and an array of any Enum is given to an Enum's.
    [TestMethod]
    [DataRow("TakeTexts |", "Books,Titles", "FixedTexts", DisplayName = "a fixed-length String is another type")]
    [DataRow("TakeSides |", "Sides,Hands", "Weights", DisplayName = "an array of another Enum")]
    public async Task TheArraysOfferedToAParameter_AreTheOnesItsArgumentCanBe(string line, string offered, string refused)
    {
        var labels = await ArrayArgumentAsync(line);

        CollectionAssert.IsSubsetOf(offered.Split(','), labels, string.Join(", ", labels.Take(20)));
        Assert.IsFalse(labels.Intersect(refused.Split(',')).Any(), string.Join(", ", labels.Take(20)));
    }

    // the local arrays, the procedure's own included, then the other arrays, then the Variants: in each, the names the word starts, then the ones whose
    // humps it matches, by name. The best of the first that has any is selected.
    [TestMethod]
    [DataRow("x = UBound(|", "AbsLoc,Own,SpanLocal,AbsMod", DisplayName = "nothing typed")]
    [DataRow("x = UBound(Ab|", "AbsLoc,AbsMod,Abs", DisplayName = "Ab")]
    [DataRow("x = UBound(Sp|", "SpanLocal,SpanFn,SpanMod,Space,SpanVar,Split,SpVar", DisplayName = "Sp")]
    [DataRow("x = UBound(W|", "Weights,Weekday", DisplayName = "W")]
    [DataRow("x = UBound(O|", "Own,Oct", DisplayName = "the procedure's own array")]
    public async Task TheArraysOfferedToUBound_RankLocalOnesFirst_ThenTheOthers_ThenTheVariants(string line, string order)
    {
        var items = await ArrayArgumentItemsAsync(line,
            "Public Sub Main(ByRef Own() As Long)\r\n    Dim SpanLocal() As Long\r\n    Dim AbsLoc() As Long\r\n    Dim SpVar As Variant");
        var expected = order.Split(',');

        CollectionAssert.AreEqual(expected, items.Select(item => item.Label).Where(expected.Contains).ToArray(), Show(items));
        Assert.AreEqual(expected[0], items.Single(item => item.Preselect).Label, Show(items));
    }

    // a record or an object is offered where an array is, after the arrays and the Variants, when a member of it, or of a member of it, is an array the
    // argument can be: what is written after it is its member.
    [TestMethod]
    public async Task WhatLeadsToAnArray_IsOfferedAfterTheArraysAndTheVariants()
    {
        var labels = await ArrayArgumentAsync("x = UBound(|");

        CollectionAssert.IsSubsetOf(new[] { "r", "o" }, labels, string.Join(", ", labels));
        Assert.IsTrue(Array.IndexOf(labels, "r") > Array.IndexOf(labels, "Anything") && Array.IndexOf(labels, "o") > Array.IndexOf(labels, "Year"),
            string.Join(", ", labels));
    }

    // a type of one name in each of two modules, the one Private to the module being written: two types, each looked into on its own, whichever is met
    // first.
    [TestMethod]
    [DataRow("Arr() As Long", "X As Long", true, DisplayName = "the other module's type leads to an array, the local one does not")]
    [DataRow("X As Long", "Arr() As Long", false, DisplayName = "the local type leads to an array, the other module's does not")]
    public async Task TwoTypesOfOneName_AreEachLookedIntoOnTheirOwn(string otherField, string localField, bool offered)
    {
        var other = $"Attribute VB_Name = \"Shelves2\"\r\nPublic Type Pair\r\n    {otherField}\r\nEnd Type\r\nPublic sp As Pair\r\n";
        WorkspaceHas(new WorkspaceDocument("Shelves2.bas", Root, other));
        var (source, position) = At($"Attribute VB_Name = \"Main\"\r\nPrivate Type Pair\r\n    {localField}\r\nEnd Type\r\nPublic Sub Main()\r\n    Dim p As Pair\r\n"
            + "    x = UBound(|\r\nEnd Sub\r\n");
        var labels = (await Sut().CompleteFragmentAsync("Main", source, position, CancellationToken.None)).Items.Select(item => item.Label).ToArray();

        Assert.AreEqual(offered, labels.Contains("sp"), string.Join(", ", labels));
        Assert.AreEqual(!offered, labels.Contains("p"), string.Join(", ", labels));
    }

    // a name the word typed is, in any case, is what the user means to write: it is selected, though the tiers before it keep their order.
    [TestMethod]
    public async Task ANameTheWordTypedIs_IsSelectedOverTheTiersBeforeIt()
    {
        var items = await ArrayArgumentItemsAsync("x = UBound(r|", "Public Sub Main()\r\n    Dim r As Rack");

        Assert.AreEqual("r", items.Single(item => item.Preselect).Label, Show(items));
        Assert.AreNotEqual("r", items[0].Label, Show(items));
    }

    [TestMethod]
    [DataRow("x = UBound(o.|", "Inner", DisplayName = "a member that leads to one")]
    [DataRow("x = UBound(o.Inner.|", "Slots", DisplayName = "the array")]
    public async Task TheMembersOfferedToUBound_AreTheOnesThatLeadToAnArray(string line, string member)
        => CollectionAssert.AreEqual(new[] { member }, await ArrayArgumentAsync(line));

    [TestMethod]
    [DataRow("Weigh |", true, DisplayName = "an array of its element type")]
    [DataRow("TakeTexts |", false, DisplayName = "no array of its element type")]
    public async Task WhatLeadsToAnArrayOfAParametersElementType_IsOfferedToIt(string line, bool offered)
    {
        var labels = await ArrayArgumentAsync(line);

        Assert.AreEqual(offered, labels.Contains("r") && labels.Contains("o"), string.Join(", ", labels));
        Assert.IsTrue(offered || !labels.Contains("r") && !labels.Contains("o"), string.Join(", ", labels));
    }

    // a positional argument written after the cursor maps to its parameter, and would follow a named one: no name is written where one comes after
    // (MS-VBAL §5.6.13.1).
    [TestMethod]
    [DataRow("Send \"a\", |, 2", DisplayName = "a positional argument after the cursor")]
    [DataRow("Send \"a\", |, , Upper", DisplayName = "an omitted one, then a positional one")]
    [DataRow("Call Send(\"a\", |, 2)", DisplayName = "in parentheses")]
    public async Task WhereAPositionalArgumentFollows_NoNameIsOffered(string line)
    {
        var items = await NamedAsync(line);

        Assert.AreEqual(0, NamesIn(items).Length, Show(items));
    }

    // a statement is a logical line: its physical lines are joined where a line continuation ends one (MS-VBAL §3.2.2).
    [TestMethod]
    public async Task ANameNamedOnTheNextPhysicalLine_IsNotOfferedByName()
    {
        var items = await NamedAsync("Send \"a\", |, _\r\n        Copies:=2");

        CollectionAssert.AreEqual(new[] { "Heading:=", "Source:=" }, NamesIn(items), Show(items));
    }

    [TestMethod]
    public async Task AnArgumentThatStartsOnTheNextPhysicalLine_IsOfferedTheNames()
    {
        var items = await NamedAsync("Send \"a\", _\r\n        |");

        CollectionAssert.AreEqual(new[] { "Heading:=", "Copies:=", "Source:=" }, NamesIn(items), Show(items));
    }

    [TestMethod]
    public async Task TheSignatureOfACallContinuedOnTheNextPhysicalLine_IsTheCalls()
    {
        var help = await CallSignatureAsync("Send \"a\", _\r\n        |");

        Assert.IsNotNull(help, "a signature");
        StringAssert.StartsWith(help.Signatures.Single().Label, "Sub Send(");
        Assert.AreEqual(1, help.ActiveParameter);
    }

    [TestMethod]
    public async Task TheArgumentsOfACallInAnElseClause_AreCountedFromTheCall()
    {
        var items = await NamedAsync("If True Then Send \"x\", 1 Else Send |");

        Assert.AreEqual(0, NamesIn(items).Length, $"Text is required: {Show(items)}");
    }

    // the Then clause of a single-line If ends at its Else (MS-VBAL §5.4.2.9): what the Else clause writes is no argument of the call in the Then clause.
    [TestMethod]
    [DataRow("If True Then Send \"a\", | Else Send 1, 2", new[] { "Heading:=", "Copies:=", "Source:=" }, DisplayName = "a positional argument after the Else")]
    [DataRow("If True Then Send \"a\", | Else Send 1, Copies:=2", new[] { "Heading:=", "Copies:=", "Source:=" }, DisplayName = "a named argument after the Else")]
    [DataRow("If True Then Send \"a\", |, Copies:=2 Else Send 1, Heading:=2", new[] { "Heading:=", "Source:=" }, DisplayName = "a named argument before the Else")]
    public async Task WhatTheElseClauseOfASingleLineIfWrites_IsNoArgumentOfTheCallBeforeIt(string line, string[] expected)
    {
        var items = await NamedAsync(line);

        CollectionAssert.AreEqual(expected, NamesIn(items), Show(items));
    }

    // what is typed of a name with its `:=` already written after it is completed with the name alone.
    [TestMethod]
    public async Task ANameWhoseTokenIsWrittenAlready_IsInsertedWithoutIt()
    {
        var item = (await NamedAsync("Send \"a\", Cop|:=2")).Single(item => item.Label == "Copies:=");

        Assert.AreEqual("Copies", item.InsertText);
    }

    // the cursor in the middle of a name: the whole name is replaced, what is after the cursor of it too, so that no part of it is written twice.
    [TestMethod]
    [DataRow("Send \"a\", Co|pies:=2", "Copies", DisplayName = "its token written already")]
    [DataRow("Send \"a\", Co|pies", "Copies:=", DisplayName = "its token not written")]
    public async Task ANameTheCursorIsInTheMiddleOf_IsReplacedWhole(string line, string text)
    {
        var item = (await NamedAsync(line)).Single(item => item.Label == "Copies:=");

        Assert.IsNotNull(item.TextEdit, "an edit of the whole word");
        var edit = item.TextEdit.TextEdit!;
        Assert.AreEqual(text, edit.NewText);
        Assert.AreEqual(new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(7, 14, 7, 20), edit.Range);
    }

    // a name the word typed is, in any case, is what the user means to write: it is selected, and the names of the arguments still rank first.
    [TestMethod]
    [DataRow("Dim heading As String\r\n    Send \"a\", heading|", "heading", DisplayName = "a variable named as a parameter")]
    [DataRow("Dim Title As String\r\n    MsgBox \"x\", , Title|", "Title", DisplayName = "MsgBox's Title")]
    public async Task ANameTheWordTypedIs_IsSelectedBeforeTheNamesOfTheArguments(string line, string expected)
    {
        var items = await NamedAsync(line);

        Assert.AreEqual(expected, items.Single(item => item.Preselect).Label, Show(items));
        StringAssert.EndsWith(items[0].Label, ":=", Show(items));
    }

    // the specification's InStr and InStrB name their parameters Arg1, Arg2 and Arg3 (MS-VBAL §6.1.2.11.1.14), which MS-VBA does not take as names.
    [TestMethod]
    [DataRow("x = InStr(|")]
    [DataRow("x = InStrB(1, |")]
    public async Task TheStandardLibrarysPlaceholderNames_AreNotOffered(string line)
    {
        var items = await NamedAsync(line);

        Assert.AreEqual(0, NamesIn(items).Length, Show(items));
    }

    // a name is selected where the word typed matches it; where nothing is typed, a value is as likely as a name.
    [TestMethod]
    public async Task WhereNothingIsTyped_NoNameIsSelected()
    {
        var items = await NamedAsync("x = Count(\"a\", |");

        Assert.AreEqual(1, items.Count(item => item.Preselect), Show(items));
        Assert.IsFalse(items.Single(item => item.Preselect).Label.EndsWith(":=", StringComparison.Ordinal), Show(items));
    }

    [TestMethod]
    public async Task WhereTheWordTypedMatchesAName_ItIsSelected()
    {
        var items = await NamedAsync("x = Count(\"a\", Co|");

        Assert.AreEqual("Copies:=", items.Single(item => item.Preselect).Label, Show(items));
    }

    // ---- a call of a default member (MS-VBAL §5.6.13): the signature, and the names, are the default member's ----

    [TestMethod]
    [DataRow("x = p(1, |", "Function Pick(", 1, DisplayName = "a variable of a class")]
    [DataRow("x = p.Items(|", "Function Pick(", 0, DisplayName = "a member that returns one")]
    [DataRow("x = h(|", "Function Pick(", 0, DisplayName = "a chain of two default members")]
    [DataRow("x = c(|", "Item(", 0, DisplayName = "a Collection")]
    public async Task ACallOfAnObject_IsACallOfItsDefaultMember(string line, string label, int active)
    {
        var help = await CallSignatureAsync(line);

        Assert.IsNotNull(help, "a signature");
        StringAssert.Contains(help.Signatures.Single().Label, label);
        Assert.AreEqual(active, help.ActiveParameter);
    }

    [TestMethod]
    [DataRow("x = arr(|", DisplayName = "an array")]
    [DataRow("x = s(|", DisplayName = "an object whose class has no default member")]
    public async Task AnIndexOfWhatHasNoDefaultMember_HasNoSignature(string line)
        => Assert.IsNull(await CallSignatureAsync(line));

    [TestMethod]
    [DataRow("x = p(1, |", DisplayName = "a variable of a class")]
    [DataRow("x = p.Items(1, |", DisplayName = "a member that returns one")]
    public async Task ACallOfAnObject_IsOfferedItsDefaultMembersNames(string line)
        => CollectionAssert.AreEqual(new[] { "B:=" }, NamesIn(await NamedAsync(line)));

    // a library whose class returns itself from a member: the member is read before the class is, and names it.
    private static readonly LibraryDescription Grid = new()
    {
        Name = "XL",
        Classes =
        [
            new ClassDescription
            {
                Name = "Range",
                Members =
                [
                    new MemberDescription
                    {
                        Name = "_Default", Kind = MemberKind.PropertyGet, DispId = 0,
                        Parameters = [new ParameterDescription { Name = "RowIndex", IsOptional = true }, new ParameterDescription { Name = "ColumnIndex", IsOptional = true }],
                    },
                    new MemberDescription { Name = "Cells", Kind = MemberKind.PropertyGet, Type = "Range" },
                    new MemberDescription { Name = "Row", Kind = MemberKind.PropertyGet, Type = "Long" },
                ],
            },
        ],
    };

    private const string GridMain = "Attribute VB_Name = \"Main\"\r\nPublic Sub Main()\r\n    Dim r As XL.Range\r\n    {0}\r\nEnd Sub\r\n";

    // what a member of a library returns is a class of the library as much as what a variable is declared as: its default member is called, and its members
    // are listed.
    [TestMethod]
    [DataRow("x = r(1, |", DisplayName = "a variable")]
    [DataRow("x = r.Cells(1, |", DisplayName = "a member that returns one")]
    [DataRow("x = r.Cells.Cells(1, |", DisplayName = "a member of what a member returns")]
    public async Task ACallOfWhatALibraryMemberReturns_IsACallOfItsDefaultMember(string line)
    {
        var (source, position) = At(string.Format(GridMain, line));
        var service = Sut(source: new InMemoryLibrarySource(Lab, Grid), references: "XL");
        var help = await service.SignatureHelpFragmentAsync("Main", source, position, CancellationToken.None);
        var names = NamesIn((await service.CompleteFragmentAsync("Main", source, position, CancellationToken.None)).Items);

        Assert.IsNotNull(help, "a signature");
        StringAssert.Contains(help.Signatures.Single().Label, "_Default(");
        Assert.AreEqual(1, help.ActiveParameter);
        CollectionAssert.AreEqual(new[] { "ColumnIndex:=" }, names);
    }

    [TestMethod]
    [DataRow("x = r.Cells.|", DisplayName = "what a member returns")]
    [DataRow("x = r.Cells.Cells.|", DisplayName = "what a member of it returns")]
    public async Task TheMembersOfWhatALibraryMemberReturns_AreItsClasss(string line)
    {
        var labels = await LabelsAsync(string.Format(GridMain, line), new InMemoryLibrarySource(Lab, Grid), "XL");

        CollectionAssert.IsSubsetOf(new[] { "Cells", "Row" }, labels, string.Join(", ", labels));
    }

    // a library member's type that the library does not qualify is no class the library declares: not the workspace's Widget, whose members are not listed.
    [TestMethod]
    public async Task TheMembersOfWhatALibraryMemberReturns_AreNotThoseOfAWorkspaceClassOfTheSameName()
    {
        var orphan = new LibraryDescription
        {
            Name = "Orphan",
            Classes = [new ClassDescription { Name = "Holder", Members = [new MemberDescription { Name = "Thing", Kind = MemberKind.PropertyGet, Type = "Widget" }] }],
        };

        var labels = await LabelsAsync("Attribute VB_Name = \"Main\"\r\nPublic Sub Main()\r\n    Dim h As Orphan.Holder\r\n    x = h.Thing.|\r\nEnd Sub\r\n",
            new InMemoryLibrarySource(Lab, orphan), "Orphan");

        CollectionAssert.DoesNotContain(labels, "Grow", string.Join(", ", labels));
    }

    [TestMethod]
    public async Task MeCalled_IsACallOfItsClasssDefaultMember()
    {
        var (source, position) = At("""
            VERSION 1.0 CLASS
            BEGIN
              MultiUse = -1  'True
            END
            Attribute VB_Name = "Main"
            Public Function Pick(Optional ByVal A As Long, Optional ByVal B As Long) As Long
            Attribute Pick.VB_UserMemId = 0
            End Function
            Public Sub Run()
                x = Me(1, |
            End Sub
            """);
        var service = Sut();
        var help = await service.SignatureHelpFragmentAsync("Main", source, position, CancellationToken.None);
        var names = NamesIn((await service.CompleteFragmentAsync("Main", source, position, CancellationToken.None)).Items);

        StringAssert.StartsWith(help?.Signatures.Single().Label, "Function Pick(");
        CollectionAssert.AreEqual(new[] { "B:=" }, names);
    }

    [TestMethod]
    public async Task TheMembersOfAVariable_AreTheMembersOfItsDeclaredType_ThoughTheLineDoesNotParse()
    {
        var labels = await LabelsAsync("""
            Attribute VB_Name = "Main"
            Public Sub Run()
                Dim w As New Widget
                Debug.Print w.|
            End Sub
            """);

        CollectionAssert.AreEquivalent(new[] { "Name", "Grow", "Parts" }, labels);
    }

    [TestMethod]
    public async Task TheMembersOfACall_AreTheMembersOfWhatItReturns()
    {
        var labels = await LabelsAsync("""
            Attribute VB_Name = "Main"
            Public Sub Run()
                Dim w As New Widget
                w.Grow(2).Parts.|
            End Sub
            """);

        CollectionAssert.IsSubsetOf(new[] { "Add", "Count", "Item", "Remove" }, labels);
    }

    [TestMethod]
    public async Task TheMembersOfAWithBlocksObject_AreOfferedAfterADotWithNothingBeforeIt()
    {
        var labels = await LabelsAsync("""
            Attribute VB_Name = "Main"
            Public Sub Run()
                With New Widget
                    .|
                End With
            End Sub
            """);

        CollectionAssert.AreEquivalent(new[] { "Name", "Grow", "Parts" }, labels);
    }

    // the object of a With block nested past the limit (VBC00060) is still evaluated for the list, on a thread as small as one the server answers on: the static
    // semantics run on a large-stack thread of their own.
    [TestMethod]
    [DataRow(3)]
    [DataRow(40_000)]
    public void TheMembersOfAWithBlocksObjectNestedPastTheLimit_AreOfferedOnASmallStack(int terms)
    {
        var deep = $"w.Grow({Parser.ExpressionNestingTests.Chain(terms)})";
        string[] labels = [];
        // the request is answered on the small stack, as the server answers it on a thread of its own: that thread waits for the answer.
#pragma warning disable VSTHRD002
        var thrown = SmallStack.Run(() => labels = LabelsAsync($"""
            Attribute VB_Name = "Main"
            Public Sub Run()
                Dim w As New Widget
                With {deep}
                    .|
                End With
            End Sub
            """).GetAwaiter().GetResult());
#pragma warning restore VSTHRD002

        Assert.IsNull(thrown, thrown?.ToString());
        CollectionAssert.AreEquivalent(new[] { "Name", "Grow", "Parts" }, labels);
    }

    [TestMethod]
    public async Task TheMembersOfAModule_AreItsPublicOnes()
        => CollectionAssert.AreEqual(new[] { "Twice" }, await LabelsAsync("""
            Attribute VB_Name = "Main"
            Public Sub Run()
                x = Helpers.Tw|
            End Sub
            """));

    [TestMethod]
    public async Task ANameWhereAStatementStarts_IsOfferedTheNamesInScope_AndTheStatementKeywords()
    {
        var labels = await LabelsAsync("""
            Attribute VB_Name = "Main"
            Private Total As Long
            Public Sub Run(ByVal Amount As Long)
                |
            End Sub
            """);

        CollectionAssert.IsSubsetOf(new[] { "Amount", "Total", "Run", "Twice", "Helpers", "MsgBox", "Dim", "For" }, labels);
        CollectionAssert.DoesNotContain(labels, "Widget", "a class is no value");
    }

    [TestMethod]
    public async Task AsIsOfferedTypes_AndNewTheClassesThatCanBeCreated()
    {
        var types = await LabelsAsync("""
            Attribute VB_Name = "Main"
            Public Sub Run()
                Dim w As |
            End Sub
            """);
        CollectionAssert.IsSubsetOf(new[] { "Widget", "Long", "Collection" }, types);

        var classes = await LabelsAsync("""
            Attribute VB_Name = "Main"
            Public Sub Run()
                Set w = New |
            End Sub
            """);
        CollectionAssert.Contains(classes, "Widget");
        CollectionAssert.DoesNotContain(classes, "Long");
    }

    [TestMethod]
    public async Task TheNamesTypedOnOneLine_AreCompletedInOneComposition_UntilAModuleChanges()
    {
        // the platform's parser answers a text it parsed last with the parse it was (ParsingClientService), and its libraries are read once.
        var parses = new Dictionary<string, ModuleParseResult>();
        _parsing.ParseFragmentAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var text = call.Arg<string>();
            if (!parses.TryGetValue(text, out var parse))
            {
                parses[text] = parse = new ModuleParser().Parse(call.Arg<Uri>(), text);
            }

            return Task.FromResult(parse);
        });

        var source = new InMemoryLibrarySource(Lab);
        IReadOnlyList<RDCore.SDK.Model.Symbols.Abstract.Symbol> symbols = [.. new LibrarySymbolProvider(new Uri(Root), ReferencedLibraries.Load(source, [], [StandardLibrary.LibraryName])).ProvideSymbols()];
        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(symbols);
        var compositions = new WorkspaceCompositionCache();
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });
        var sut = new CompletionService(_documents, _parsing, new IntrinsicSymbolResolver(), libraries, source, _verbs, options, NullLogger<CompletionService>.Instance, OneProject.Registry(), compositions);

        async Task<string[]> CompleteAsync(string module)
        {
            var (text, position) = At(module);
            return [.. (await sut.CompleteFragmentAsync("Main", text, position, CancellationToken.None)).Items.Select(item => item.Label)];
        }

        CollectionAssert.Contains(await CompleteAsync("Attribute VB_Name = \"Main\"\r\nPublic Sub Run()\r\n    x = Tw|\r\nEnd Sub\r\n"), "Twice");
        CollectionAssert.Contains(await CompleteAsync("Attribute VB_Name = \"Main\"\r\nPublic Sub Run()\r\n    x = Twice(1) + vbWh|\r\nEnd Sub\r\n"), "vbWhite");
        Assert.AreEqual(1, compositions.Composed, "the line is taken out of the module the list is composed of: the rest of it is what it was");

        WorkspaceHas(new WorkspaceDocument("Helpers.bas", Root, Helpers.Replace("Twice", "Thrice")));
        CollectionAssert.Contains(await CompleteAsync("Attribute VB_Name = \"Main\"\r\nPublic Sub Run()\r\n    x = Th|\r\nEnd Sub\r\n"), "Thrice");
        Assert.AreEqual(2, compositions.Composed, "a module that changed is another parse, and the workspace is composed again");
    }

    // HexIDE selects the first label the word starts: Pr offered CLngPtr, DatePart and NPer, and none of them starts with it.
    [TestMethod]
    public async Task AWord_OffersTheNamesItStarts_AndNoneThatOnlyHaveItsLetters()
    {
        var (source, position) = At("Attribute VB_Name = \"Main\"\r\nPublic Sub Run()\r\n    x = Pr|\r\nEnd Sub\r\nPublic Function Progress() As Long\r\nEnd Function\r\n");
        var items = (await Sut().CompleteFragmentAsync("Main", source, position, CancellationToken.None)).Items.OrderBy(item => item.SortText, StringComparer.Ordinal).ToList();
        var labels = items.Select(item => item.Label).ToArray();

        CollectionAssert.Contains(labels, "Progress");
        CollectionAssert.DoesNotContain(labels, "CLngPtr");
        CollectionAssert.DoesNotContain(labels, "DatePart");
        CollectionAssert.DoesNotContain(labels, "NPer");
        Assert.IsTrue(items[0].Label.StartsWith("Pr", StringComparison.OrdinalIgnoreCase), items[0].Label);
    }

    [TestMethod]
    public async Task TheNamesAWordStarts_RankBeforeTheOnesItMatchesTheHumpsOf_ThoughTheseAreNearer()
    {
        var (source, position) = At("""
            Attribute VB_Name = "Main"
            Private Const MPEG As Long = 1
            Public Sub Run()
                Dim MultiPlayer As Long
                x = MP|
            End Sub
            """);
        var items = (await Sut().CompleteFragmentAsync("Main", source, position, CancellationToken.None)).Items.OrderBy(item => item.SortText, StringComparer.Ordinal).ToList();

        CollectionAssert.AreEqual(new[] { "MPEG", "MultiPlayer" }, items.Select(item => item.Label).Take(2).ToArray(),
            string.Join(", ", items.Take(5).Select(item => item.Label + "/" + item.SortText)));
    }

    // LSP 3.17 §Completion Request: a list that is narrowed by the word typed is not the list of a longer word, which the client asks for again.
    [TestMethod]
    public async Task AList_IsIncomplete_AndItsBestItemIsPreselected()
    {
        var (source, position) = At("Attribute VB_Name = \"Main\"\r\nPublic Sub Run()\r\n    x = Le|\r\nEnd Sub\r\n");
        var list = await Sut().CompleteFragmentAsync("Main", source, position, CancellationToken.None);
        var items = list.Items.OrderBy(item => item.SortText, StringComparer.Ordinal).ToList();

        Assert.IsTrue(list.IsIncomplete);
        Assert.IsTrue(items[0].Preselect, items[0].Label);
        Assert.AreEqual(1, items.Count(item => item.Preselect));
    }

    // ---- a dictionary access (MS-VBAL §5.6.14): the name after a `!` is no declaration's, and nothing true can be offered for it ----

    private async Task<string[]> BangAsync(string line, string? triggerCharacter = null)
    {
        var (source, position) = At($"Attribute VB_Name = \"Main\"\r\nPublic Sub Main()\r\n    Dim rs As New Collection\r\n    Dim total!\r\n    {line}\r\nEnd Sub\r\n");
        return [.. (await Sut().CompleteFragmentAsync("Main", source, position, CancellationToken.None, triggerCharacter)).Items.Select(item => item.Label)];
    }

    [TestMethod]
    [DataRow("x = rs!|", null, DisplayName = "after the bang")]
    [DataRow("x = rs!|", "!", DisplayName = "asked for by the bang")]
    [DataRow("x = rs!Co|", null, DisplayName = "a name after the bang")]
    [DataRow("Debug.Print total!|", null, DisplayName = "after a type suffix")]
    public async Task AfterABang_NothingIsOffered(string line, string? triggerCharacter)
    {
        var labels = await BangAsync(line, triggerCharacter);

        Assert.AreEqual(0, labels.Length, string.Join(", ", labels.Take(8)));
    }

    [TestMethod]
    public async Task AfterADot_TheMembersAreOffered()
        => CollectionAssert.IsSubsetOf(new[] { "Add", "Count", "Item" }, await BangAsync("x = rs.|"));

    [TestMethod]
    public async Task AVariableDeclaredWithATypeSuffix_IsOfferedLikeAnyOther()
        => CollectionAssert.Contains(await BangAsync("x = to|"), "total");

    [TestMethod]
    public async Task NothingIsOffered_InAComment()
        => Assert.AreEqual(0, (await LabelsAsync("""
            Attribute VB_Name = "Main"
            Public Sub Run()
                ' w.|
            End Sub
            """)).Length);

    [TestMethod]
    public async Task AProgramOfTheBasic_IsCompletedWhereItsLinesAreInTheText()
    {
        var program = new WorkspaceDocument("hello.rdc", Root, "100 DIM W AS NEW Widget\r\n110 W.\r\n");
        WorkspaceHas(new WorkspaceDocument("Widget.cls", Root, Widget), program);

        var list = await Sut("basic").CompleteDocumentAsync(program.Id.Uri.ToUri(), new Position(1, 6), CancellationToken.None);

        CollectionAssert.AreEquivalent(new[] { "Name", "Grow", "Parts" }, list.Items.Select(item => item.Label).ToArray());
    }

    // what an item replaces is where the client's document has it, not where the module the program is the body of has it.
    [TestMethod]
    public async Task WhatAnItemOfAProgramReplaces_IsInTheProgramsOwnLines()
    {
        var program = new WorkspaceDocument("hello.rdc", Root, "100 Send \"a\", Co\r\n110 Send \"a\", Copies:=2\r\n");
        WorkspaceHas(new WorkspaceDocument("Printer.bas", Root, Printer), program);

        var list = await Sut("basic").CompleteDocumentAsync(program.Id.Uri.ToUri(), new Position(1, 16), CancellationToken.None);
        var item = list.Items.Single(item => item.Label == "Copies:=");

        Assert.AreEqual(new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(1, 14, 1, 20), item.TextEdit?.TextEdit?.Range, Show(list.Items));
    }

    [TestMethod]
    public async Task AnItem_SaysWhatItIs()
    {
        var (source, position) = At("""
            Attribute VB_Name = "Main"
            Public Sub Run()
                x = Helpers.|
            End Sub
            """);
        var item = (await Sut().CompleteFragmentAsync("Main", source, position, CancellationToken.None)).Items.Single();

        Assert.AreEqual(CompletionItemKind.Function, item.Kind);
        Assert.AreEqual("Function Twice(ByVal Value As Long) As Long", item.Detail);
    }

    // an Object Set to what CreateObject creates is known, by the list, to be of the class the identifier names: whether or not the project references its library.
    [TestMethod]
    [DataRow(new string[0], DisplayName = "not referenced")]
    [DataRow(new[] { "Lab" }, DisplayName = "referenced")]
    public async Task TheMembersOfAnObjectCreatedByName_AreTheMembersOfItsClass(string[] references)
        => CollectionAssert.AreEquivalent(new[] { "Label", "Fill" }, await LabelsAsync("""
            Attribute VB_Name = "Main"
            Public Sub Run()
                Dim o As Object
                Set o = CreateObject("Lab.Shelf")
                o.|
            End Sub
            """, references: references));

    [TestMethod]
    public async Task TheMembersOfADictionaryCreatedByName_AreTheMembersTheScriptingLibraryDescribes()
    {
        var labels = await LabelsAsync("""
            Attribute VB_Name = "Main"
            Public Sub Run()
                Dim o As Object
                Set o = CreateObject("Scripting.Dictionary")
                o.|
            End Sub
            """, new DirectoryLibrarySource(new FileSystem(), ShippedLibrariesTests.Symbols));

        CollectionAssert.IsSubsetOf(new[] { "Add", "Exists", "Item", "Items", "Keys", "Count", "Remove", "RemoveAll" }, labels);
    }

    [TestMethod]
    public async Task AVariantModuleVariable_InAWithBlock_IsKnownFromWhereverTheModuleSetsIt()
        => CollectionAssert.AreEquivalent(new[] { "Label", "Fill" }, await LabelsAsync("""
            Attribute VB_Name = "Main"
            Private Shelf

            Public Sub Init()
                Set Shelf = CreateObject("Lab.Shelf")
            End Sub

            Public Sub Run()
                With Shelf
                    .|
                End With
            End Sub
            """));

    // what a variable may hold is every class it is Set to: which one it holds is the run's to say.
    [TestMethod]
    public async Task AVariableSetToObjectsOfTwoClasses_IsOfferedTheMembersOfBoth()
        => CollectionAssert.AreEquivalent(new[] { "Label", "Fill", "Ignite" }, await LabelsAsync("""
            Attribute VB_Name = "Main"
            Public Sub Run(ByVal Hot As Boolean)
                Dim o As Object
                If Hot Then
                    Set o = CreateObject("Lab.Burner")
                Else
                    Set o = CreateObject("lab.shelf")
                    Set o = CreateObject("Lab.Shelf")
                End If
                o.|
            End Sub
            """));

    [TestMethod]
    public async Task ALocalOfAnotherProcedure_OrAnIdentifierTheCodeDoesNotSay_SaysNothingOfTheVariable()
        => Assert.AreEqual(0, (await LabelsAsync("""
            Attribute VB_Name = "Main"
            Public Sub Other()
                Dim o As Object
                Set o = CreateObject("Lab.Shelf")
            End Sub

            Public Sub Run(ByVal Id As String)
                Dim o As Object
                Set o = CreateObject(Id)
                o.|
            End Sub
            """)).Length);

    // ---- what is offered where: the module is the attribute, the lines given, and the cursor where the `|` is ----

    private const string Header = "Attribute VB_Name = \"Main\"\r\n";

    private const string ClassHeader = "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\nAttribute VB_Name = \"Main\"\r\n";

    [TestMethod]
    // the declarations section: what can be declared there, and no statement; Option only before any declaration.
    [DataRow("|\r\nPublic Sub Run()\r\nEnd Sub", "Option;Dim;Private;Sub;Function;Const;Type;Enum;Declare", "MsgBox;If;For;Implements;Event;End Sub")]
    [DataRow("Private Total As Long\r\n|", "Dim;Sub;Option", "")]
    [DataRow("Option Explicit\r\nOption |", "Base;Compare;Private", "Explicit")]
    [DataRow("Option |\r\nPrivate Total As Long\r\nOption Explicit", "Base;Compare;Private", "Explicit")]
    [DataRow("Option Explicit\r\nOption Base 1\r\nOption Compare Text\r\nOption Private Module\r\n|", "Dim;Sub", "Option")]
    // a statement in a procedure: the statements, the names, and what the blocks the line is in have - and the blocks are open though they are not closed yet.
    [DataRow("Public Sub Run()\r\n    For i = 1 To 3\r\n        |", "Next;Exit For;Exit Sub;End Sub;MsgBox;If;Dim", "Loop;Wend;End If;Exit Do;Option;Sub;Private;Exit Function")]
    [DataRow("Public Function Run()\r\n    Do\r\n        With New Collection\r\n            |", "Loop;Exit Do;End With;Exit Function;End Function", "Next;End Sub;Exit For")]
    [DataRow("Public Sub Run()\r\n    If x Then\r\n        |\r\n    End If\r\nEnd Sub", "Else;ElseIf;End If", "Case;End Select")]
    [DataRow("Public Sub Run()\r\n    For i = 1 To 3\r\n    Next\r\n    |\r\nEnd Sub", "If", "Next;Exit For")]
    [DataRow("Public Sub Run()\r\n    Select Case x\r\n        |", "Case;End Select", "")]
    // what a statement says comes next.
    [DataRow("Public Sub Run()\r\n    Exit |", "Sub", "For;Do;Function")]
    [DataRow("Public Function Run()\r\n    Do\r\n        Exit |", "Do;Function", "For;Sub")]
    [DataRow("Public Sub Run()\r\n    With New Collection\r\n        End |", "With;Sub", "If;Select;Function")]
    [DataRow("Public Sub Run()\r\n    If a |", "Then;And;Or;Mod", "MsgBox;Twice;Dim")]
    [DataRow("Public Sub Run()\r\n    x = |", "Twice;MsgBox;Not;Nothing;New;True", "Dim;Then;And;Next")]
    [DataRow("Public Sub Run()\r\n    x = a |", "And;Mod;Like;Is", "Twice;Not;Then")]
    [DataRow("Public Sub Run()\r\n    MsgBox |", "Twice;Not;AddressOf", "And;Dim")]
    [DataRow("Public Sub Run()\r\n    For |", "Each;Twice", "Dim")]
    [DataRow("Public Sub Run()\r\n    For Each item |", "In", "Twice;To")]
    [DataRow("Public Sub Run()\r\n    For i = 1 |", "To;Mod", "Step;Twice")]
    [DataRow("Public Sub Run()\r\n    For i = 1 To 9 |", "Step", "To;Twice")]
    [DataRow("Public Sub Run()\r\n    On Error |", "GoTo;Resume", "Twice")]
    [DataRow("Public Sub Run()\r\n    On Error Resume |", "Next", "Twice")]
    [DataRow("Public Sub Run()\r\nRetry:\r\n    x = 1\r\n    GoTo |\r\nDone:\r\nEnd Sub", "Retry;Done", "Twice;Run")]
    [DataRow("Public Sub Run()\r\n    Resume |\r\n10  x = 1\r\nEnd Sub", "Next;10", "Twice")]
    [DataRow("Public Sub Run()\r\n    Do |", "While;Until", "Twice")]
    [DataRow("Public Sub Run()\r\n    Select |", "Case", "Twice")]
    [DataRow("Public Sub Run()\r\n    ReDim |", "Preserve;Twice", "Dim")]
    [DataRow("Public Sub Run()\r\n    Open \"f\" For |", "Input;Output;Append;Binary;Random", "Twice")]
    // a declaration: nothing where its name is written, As after it, and the modifiers of a parameter.
    [DataRow("Public Sub Run()\r\n    Dim |", "", "Twice;MsgBox;As;WithEvents")]
    [DataRow("Public Sub Run()\r\n    Dim total |", "As", "Twice;MsgBox")]
    [DataRow("Public Sub Run()\r\n    Dim total As Long, |", "", "As;Twice")]
    [DataRow("Public Sub Run()\r\n    Dim items() |", "As", "Twice")]
    [DataRow("Public Sub Run()\r\n    Const Limit |", "As", "Twice")]
    [DataRow("Public Sub Run()\r\n    Const Limit = |", "Twice;Not", "As")]
    [DataRow("Private |", "Sub;Function;Const;Type;Enum;Declare", "Twice;Option;Event;WithEvents")]
    [DataRow("Public Sub |", "", "Twice;MsgBox;As")]
    [DataRow("Public Sub Run(|", "ByVal;ByRef;Optional;ParamArray", "Twice;As")]
    [DataRow("Public Sub Run(Optional |", "ByVal;ByRef", "ParamArray;Twice")]
    [DataRow("Public Sub Run(ByVal amount |", "As", "Twice;ByVal")]
    [DataRow("Public Sub Run(ByVal amount As Long, |", "ByVal;Optional", "As")]
    [DataRow("Public Function Run() |", "As", "Twice")]
    [DataRow("Public Sub Run() |", "", "As")]
    [DataRow("Public Property |", "Get;Let;Set", "Twice")]
    [DataRow("Declare |", "PtrSafe;Function;Sub", "Twice")]
    [DataRow("Declare PtrSafe Function Beep |", "Lib", "Twice")]
    [DataRow("Option |", "Explicit;Base;Compare;Private", "Twice")]
    [DataRow("Option Compare |", "Binary;Text;Database", "Twice")]
    // the types: no keyword but New, no module, no local.
    [DataRow("Public Sub Run(ByVal Amount As Long)\r\n    Dim w As |", "New;Long;String;Widget;Collection;VBA", "Helpers;Amount;Dim;Strings;Twice")]
    [DataRow("Public Sub Run()\r\n    Dim w As New |", "Widget;Collection", "Long;New;Helpers")]
    [DataRow("Public Sub Run()\r\n    If TypeOf x Is |", "Widget;Collection", "Long;Twice;Helpers")]
    public async Task WhatIsOffered_IsWhatCanBeWrittenWhereTheCursorIs(string lines, string offered, string absent)
    {
        var labels = await LabelsAsync(Header + lines);

        CollectionAssert.IsSubsetOf(Split(offered), labels, $"offered: {string.Join(", ", labels.Take(40))}");
        foreach (var label in Split(absent))
        {
            CollectionAssert.DoesNotContain(labels, label);
        }
    }

    [TestMethod]
    [DataRow("|", "Implements;Event;Friend", "")]
    [DataRow("Private |", "WithEvents;Event", "")]
    [DataRow("Implements |", "Widget;Collection", "Long;Helpers")]
    [DataRow("Public Event Changed()\r\nPublic Sub Run()\r\n    RaiseEvent |", "Changed", "Twice;Run")]
    [DataRow("Public Sub Run()\r\n    |", "Me;RaiseEvent", "Implements")]
    [DataRow("Public Sub Run()\r\n    x = |", "Me", "")]
    public async Task AClassModule_HasWhatOnlyAClassCanHave(string lines, string offered, string absent)
    {
        var labels = await LabelsAsync(ClassHeader + lines);

        CollectionAssert.IsSubsetOf(Split(offered), labels, $"offered: {string.Join(", ", labels.Take(40))}");
        foreach (var label in Split(absent))
        {
            CollectionAssert.DoesNotContain(labels, label);
        }
    }

    [TestMethod]
    public async Task AStandardModule_HasNoneOfIt()
    {
        CollectionAssert.DoesNotContain(await LabelsAsync(Header + "Public Sub Run()\r\n    x = |"), "Me");
        CollectionAssert.DoesNotContain(await LabelsAsync(Header + "Public Sub Run()\r\n    |"), "RaiseEvent");
    }

    private static string[] Split(string labels) => labels.Length == 0 ? [] : labels.Split(';');

    // ---- the arguments of a client's command, as its verb describes them ----

    private static readonly VerbDescriptor Break = new()
    {
        Name = "BREAK",
        Forms =
        [
            [new VerbArgument { Kind = VerbArgumentKind.Keyword, Values = [new() { Value = "CLEAR", Description = "Removes every breakpoint" }] }],
            [new VerbArgument { Kind = VerbArgumentKind.Keyword, Values = [new() { Value = "TOGGLE" }] }, new VerbArgument { Kind = VerbArgumentKind.LineNumber }],
            [new VerbArgument { Name = "line", Kind = VerbArgumentKind.LineNumber }, new VerbArgument { Kind = VerbArgumentKind.Expression, EvaluatedAt = "line" }],
        ],
    };

    private const string Program = "Public Sub Main()\r\n10 Dim C As New Collection\r\n30 Print C.Count\r\nEnd Sub\r\n";

    private async Task<CompletionList> CommandAsync(string arguments, string source = Program, VerbDescriptor? verb = null)
    {
        _verbs.Register([verb ?? Break]);
        return await Sut("basic").CompleteCommandAsync("Program", source, new SessionCommandCompletion { Verb = (verb ?? Break).Name.ToLowerInvariant(), Arguments = arguments }, CancellationToken.None);
    }

    private static readonly VerbDescriptor Fold = new()
    {
        Name = "FOLD",
        Forms = [[new VerbArgument { Name = "line", Kind = VerbArgumentKind.LineNumber, LineFilter = VerbLineFilter.BlockStarts }]],
    };

    private const string Blocks = "Public Sub Main()\r\n10 x = 1\r\n20 If x > 0 Then\r\n30 Print x\r\n40 End If\r\n50 For i = 1 To 2\r\n60 Print i\r\n70 Next\r\n80 If x Then Print 1\r\nEnd Sub\r\n";

    [TestMethod]
    public async Task ALineNumberFilteredToBlocks_IsCompletedWithTheLinesABlockStartsAt_AndNoOther()
    {
        var list = await CommandAsync(" ", Blocks, Fold);

        CollectionAssert.AreEqual(new[] { "20", "50" }, list.Items.Select(item => item.Label).ToArray(), "a single-line If folds nothing, and neither does a statement");
    }

    [TestMethod]
    public async Task ALineNumberWithNoFilter_IsCompletedWithEveryNumberedLine()
    {
        var list = await CommandAsync(" ", Blocks, Fold with { Forms = [[new VerbArgument { Name = "line", Kind = VerbArgumentKind.LineNumber }]] });

        CollectionAssert.AreEqual(new[] { "10", "20", "30", "40", "50", "60", "70", "80" }, list.Items.Select(item => item.Label).ToArray());
    }

    [TestMethod]
    public async Task AVerbTheClientDidNotRegister_IsOfferedNothing()
    {
        var list = await Sut("basic").CompleteCommandAsync("Program", Program, new SessionCommandCompletion { Verb = "BREAK", Arguments = " " }, CancellationToken.None);
        Assert.AreEqual(0, list.Items.Count());
    }

    private static readonly VerbDescriptor Config = new()
    {
        Name = "CONFIG",
        Forms =
        [
            [
                new VerbArgument { Name = "reset", Kind = VerbArgumentKind.Keyword, Values = [new() { Value = "RESET" }] },
                new VerbArgument { Name = "setting", Kind = VerbArgumentKind.Setting, Settings = SettingsSchema.Platform.Settings.Where(setting => setting.IsSettable).ToList() },
            ],
            [
                new VerbArgument { Name = "setting", Kind = VerbArgumentKind.Setting, Settings = SettingsSchema.Platform.Settings.Where(setting => setting.IsSettable).ToList() },
                new VerbArgument { Name = "value", Kind = VerbArgumentKind.SettingValue, ValueOf = "setting" },
            ],
        ],
    };

    // CONFIG SERVER.VERBOSE FALSE, completed all the way through: a section, a setting of it, a value of that.
    [TestMethod]
    public async Task ASetting_IsCompletedAPartAtATime_ThenItsValue()
    {
        var sections = (await CommandAsync(" ", verb: Config)).Items.ToList();
        CollectionAssert.IsSubsetOf(new[] { "RESET", "Server", "Environment" }, sections.Select(item => item.Label).ToArray());
        Assert.AreEqual(CompletionItemKind.Module, sections.Single(item => item.Label == "Server").Kind);

        var server = (await CommandAsync(" SERVER.", verb: Config)).Items.ToList();
        var verbose = server.Single(item => item.Label == "Verbose");
        Assert.AreEqual(CompletionItemKind.Property, verbose.Kind);
        Assert.IsFalse(string.IsNullOrEmpty(verbose.Detail), "what it does");

        CollectionAssert.AreEqual(new[] { "True", "False" }, (await CommandAsync(" SERVER.VERBOSE F", verb: Config)).Items.Select(item => item.Label).ToArray());
        Assert.AreEqual(0, (await CommandAsync(" Workspace.WorkspaceUri ", verb: Config)).Items.Count(), "any text: nothing to offer");
    }

    // a module of an editor, not a program: two procedures, and lines numbered in the second.
    private const string Module = """
        Private Total As Long

        Public Sub First()
            Dim Scratch As String
        End Sub

        Public Sub Second()
            Dim Items As New Collection
        10  Items.Add 1
        20  Total = Items.Count
        End Sub
        """;

    private static readonly VerbDescriptor Watch = new()
    {
        Name = "WATCH",
        Forms = [[new VerbArgument { Kind = VerbArgumentKind.Expression }]],
    };

    [TestMethod]
    public async Task AnExpression_IsCompletedWhereItIsEvaluated_BeforeItsLine_InThatLinesProcedure()
    {
        var labels = (await CommandAsync(" 20 ", Module)).Items.Select(item => item.Label).ToArray();

        CollectionAssert.IsSubsetOf(new[] { "Items", "Total", "First" }, labels, "the procedure's locals, and the module's names");
        CollectionAssert.DoesNotContain(labels, "Scratch", "a local of another procedure");

        var members = (await CommandAsync(" 20 Items.", Module)).Items.Select(item => item.Label).ToArray();
        CollectionAssert.IsSubsetOf(new[] { "Add", "Count" }, members);
    }

    [TestMethod]
    public async Task AnExpressionEvaluatedAtNoLine_IsCompletedInTheModule()
    {
        var labels = (await CommandAsync(" ", Module, Watch)).Items.Select(item => item.Label).ToArray();

        CollectionAssert.IsSubsetOf(new[] { "Total", "First", "Second", "MsgBox" }, labels);
        CollectionAssert.DoesNotContain(labels, "Items", "no procedure's local");
        CollectionAssert.DoesNotContain(labels, "Scratch");
        Assert.IsFalse(labels.Any(label => label.StartsWith("RDCore", StringComparison.Ordinal)), "the scope it is completed in is no name of the module");
    }

    [TestMethod]
    [DataRow(" ", "CLEAR;TOGGLE;10;30", "C;Main")]
    [DataRow(" 3", "CLEAR;TOGGLE;10;30", "")]
    [DataRow(" TOGGLE ", "10;30", "CLEAR;TOGGLE")]
    [DataRow(" 30 ", "C;Not;Nothing", "CLEAR;10;Dim")]
    [DataRow(" 30 C.", "Add;Count;Item;Remove", "C;CLEAR")]
    [DataRow(" 30 C.Count ", "And;Mod", "C")]
    [DataRow(" CLEAR ", "", "CLEAR;10;C")]
    public async Task ACommand_IsCompletedAsItsVerbDescribesIt_InTheProgramItActsOn(string arguments, string offered, string absent)
    {
        var labels = (await CommandAsync(arguments)).Items.Select(item => item.Label).ToArray();

        CollectionAssert.IsSubsetOf(Split(offered), labels, $"offered: {string.Join(", ", labels.Take(40))}");
        foreach (var label in Split(absent))
        {
            CollectionAssert.DoesNotContain(labels, label);
        }
    }

    [TestMethod]
    public async Task ALine_IsShownWithItsStatement_AndAKeywordWithWhatItDoes()
    {
        var items = (await CommandAsync(" ")).Items;

        Assert.AreEqual("Print C.Count", items.Single(item => item.Label == "30").Detail);
        Assert.AreEqual("Removes every breakpoint", items.Single(item => item.Label == "CLEAR").Detail);
    }

    // ---- what the call being written takes ----

    private async Task<SignatureHelp?> SignatureAsync(string module, ILibrarySource? libraries = null, params string[] references)
    {
        var (source, position) = At(module);
        return await Sut(source: libraries, references: references).SignatureHelpFragmentAsync("Main", source, position, CancellationToken.None);
    }

    private static string ActiveParameterOf(SignatureHelp help)
    {
        var signature = help.Signatures.Single();
        var label = signature.Parameters!.ElementAt(help.ActiveParameter!.Value).Label.Range;
        return signature.Label[label.start..label.end];
    }

    [TestMethod]
    public async Task AProcedureOfAModule_IsShownWithItsSignature_AndWhatItsDescriptionSays()
    {
        var help = await SignatureAsync("""
            Attribute VB_Name = "Main"
            Public Sub Run()
                x = Helpers.Twice(|
            End Sub
            """);

        var signature = help!.Signatures.Single();
        Assert.AreEqual("Function Twice(ByVal Value As Long) As Long", signature.Label);
        Assert.AreEqual("Doubles a \"value\".", signature.Documentation?.String);
        Assert.AreEqual("ByVal Value As Long", ActiveParameterOf(help));
    }

    [TestMethod]
    public async Task TheArgumentAtTheCursor_IsTheParameterItIsGivenTo_InTheInnermostCall()
    {
        var mid = await SignatureAsync("""
            Attribute VB_Name = "Main"
            Public Sub Run()
                x = Left(Mid("abc", |
            End Sub
            """);
        Assert.AreEqual("Mid", mid!.Signatures.Single().Label.Split('(')[0].Split(' ')[1]);
        Assert.AreEqual(1, mid.ActiveParameter);

        var left = await SignatureAsync("""
            Attribute VB_Name = "Main"
            Public Sub Run()
                x = Left(Mid("abc", 2), |
            End Sub
            """);
        StringAssert.StartsWith(left!.Signatures.Single().Label, "Function Left");
        Assert.AreEqual(1, left.ActiveParameter);
    }

    [TestMethod]
    public async Task ACallStatement_TakesItsArgumentsWithoutParentheses_AndANamedArgumentIsItsParameters()
    {
        var positional = await SignatureAsync("""
            Attribute VB_Name = "Main"
            Public Sub Run()
                MsgBox "Hello", |
            End Sub
            """);
        StringAssert.StartsWith(ActiveParameterOf(positional!), "Optional ByVal Buttons");

        var named = await SignatureAsync("""
            Attribute VB_Name = "Main"
            Public Sub Run()
                MsgBox "Hello", Title:=|
            End Sub
            """);
        StringAssert.Contains(ActiveParameterOf(named!), "Title");
    }

    [TestMethod]
    public async Task AMemberOfAnObject_HasNoImplicitMe_AmongItsParameters()
    {
        var help = await SignatureAsync("""
            Attribute VB_Name = "Main"
            Public Sub Run()
                Dim w As New Widget
                w.Grow(|
            End Sub
            """);

        StringAssert.StartsWith(help!.Signatures.Single().Label, "Function Grow(ByVal By As Long)");
        Assert.AreEqual(1, help.Signatures.Single().Parameters!.Count());
    }

    [TestMethod]
    public async Task AMemberOfAnObjectCreatedByName_IsTheMemberItsLibraryDescribes()
    {
        var help = await SignatureAsync("""
            Attribute VB_Name = "Main"
            Public Sub Run()
                Dim o As Object
                Set o = CreateObject("Scripting.Dictionary")
                o.Add "key", |
            End Sub
            """, new DirectoryLibrarySource(new FileSystem(), ShippedLibrariesTests.Symbols));

        var signature = help!.Signatures.Single();
        StringAssert.StartsWith(signature.Label, "Sub Add(");
        Assert.IsFalse(string.IsNullOrEmpty(signature.Documentation?.String), "what the library says it does");
        Assert.AreEqual(1, help.ActiveParameter);
    }

    [TestMethod]
    [DataRow("    x = 1 + (|", DisplayName = "the parentheses of an expression")]
    [DataRow("    x = Left(\"a, (|", DisplayName = "in a string literal")]
    [DataRow("    x = Twice(1) ' Twice(|", DisplayName = "in a comment")]
    [DataRow("    x = |", DisplayName = "no call")]
    [DataRow("    Dim y As Long, |", DisplayName = "no procedure")]
    public async Task WhereNoCallOfAProcedureIsWritten_ThereIsNoSignature(string line)
        => Assert.IsNull(await SignatureAsync($"Attribute VB_Name = \"Main\"\r\nPublic Sub Run()\r\n{line}\r\nEnd Sub\r\n"));

    // ---- names in brackets ----

    // a name that is no identifier, written in brackets (MS-VBAL §3.3.5.3), is offered in its brackets, which is how it is written; the word typed is matched
    // against the name inside them, typed with its opening bracket or without.
    [TestMethod]
    [DataRow("    |", DisplayName = "nothing typed")]
    [DataRow("    x = |", DisplayName = "nothing typed, an operand")]
    [DataRow("    My|", DisplayName = "the start of the name")]
    [DataRow("    x = my|", DisplayName = "the start of the name, in another letter case")]
    [DataRow("    [My|", DisplayName = "the opening bracket and the start of the name")]
    [DataRow("    x = [|", DisplayName = "the opening bracket alone")]
    public async Task ANameInBrackets_IsOfferedInItsBrackets(string line)
    {
        var labels = await LabelsAsync($"Attribute VB_Name = \"Main\"\r\nPrivate [My Field] As Long\r\nPublic Sub Run()\r\n{line}\r\nEnd Sub\r\n");

        CollectionAssert.Contains(labels, "[My Field]");
        CollectionAssert.DoesNotContain(labels, "My Field");
    }

    // what is typed of it, its opening bracket included, is what the item replaces.
    [TestMethod]
    public async Task ANameInBrackets_TypedWithItsOpeningBracket_ReplacesTheBracketToo()
    {
        var (source, position) = At("Attribute VB_Name = \"Main\"\r\nPrivate [My Field] As Long\r\nPublic Sub Run()\r\n    x = [My|\r\nEnd Sub\r\n");
        var list = await Sut().CompleteFragmentAsync("Main", source, position, CancellationToken.None);

        var item = list.Items.Single(candidate => candidate.Label == "[My Field]");
        var edit = item.TextEdit?.TextEdit;
        Assert.IsNotNull(edit);
        Assert.AreEqual(new Position(3, 8), edit.Range.Start, "from the opening bracket");
        Assert.AreEqual(position, edit.Range.End);
        Assert.AreEqual("[My Field]", edit.NewText);
    }

    // an identifier is offered as it is, with a bracket typed before it too.
    [TestMethod]
    public async Task AnIdentifier_TypedWithAnOpeningBracket_IsOffered()
        => CollectionAssert.Contains(await LabelsAsync("Attribute VB_Name = \"Main\"\r\nPrivate Total As Long\r\nPublic Sub Run()\r\n    x = [To|\r\nEnd Sub\r\n"), "Total");
}
