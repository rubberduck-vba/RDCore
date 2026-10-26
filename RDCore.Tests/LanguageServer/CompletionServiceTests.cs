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
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(source, references, [StdLibSymbolProvider.LibraryName])).ProvideSymbols()]);
        libraries.DescribedSymbolsOf(Arg.Any<Uri>(), Arg.Any<string>()).Returns(call => LibrarySymbolProvider.ReadDescribed(source, call.Arg<Uri>(), call.Arg<string>()));
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = language } });
        return new CompletionService(_documents, _parsing, new IntrinsicSymbolResolver(), libraries, source, _verbs, options, NullLogger<CompletionService>.Instance);
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
                Dim w As Wi|
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
}
