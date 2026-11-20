using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol;
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
using System.IO.Pipelines;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// <c>textDocument/codeLens</c> and <c>codeLens/resolve</c> over the wire, as a client asks them: a lens above the name of each member a document declares,
/// and once resolved, how many names in the workspace refer to it.
/// </summary>
[TestClass]
public sealed class CodeLensWireTests
{
    // a cap on a hang, not a budget: each wait is on the answer itself, which a loaded machine gives as late as it does.
    private static readonly TimeSpan SafetyCap = TimeSpan.FromMinutes(5);

    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-codelens-wire-ws");

    private const string Helpers = """
        Attribute VB_Name = "Helpers"
        Private mTitle As String
        Public Type Point
            X As Long
        End Type
        Public Enum Kind
            KindA
        End Enum
        Public Property Get Title() As String
            Title = mTitle
        End Property
        Public Property Let Title(ByVal value As String)
            mTitle = value
        End Property
        Public Sub Show()
            Debug.Print Title
        End Sub
        Private Function Unused() As Long
            Unused = 1
        End Function
        """;

    // reads Title, and never assigns it.
    private const string Program = "10 Show\r\n20 Show\r\n30 Debug.Print Title\r\n40 Dim p As Point\r\n";

    private static DocumentUri Document(string fileName) => DocumentUri.From(Path.Combine(Root, fileName));

    private static string Class(string name, params string[] lines)
        => string.Join("\r\n", ["VERSION 1.0 CLASS", "BEGIN", "  MultiUse = -1  'True", "END", $"Attribute VB_Name = \"{name}\"", .. lines]) + "\r\n";

    // an interface, and two classes that implement it.
    private static readonly (string FileName, string Text)[] Shapes =
    [
        ("IShape.cls", Class("IShape", "Public Function Area() As Double", "End Function")),
        ("Square.cls", Class("Square", "Implements IShape", "Private Function IShape_Area() As Double", "End Function")),
        ("Disc.cls", Class("Disc", "Implements IShape", "Private Function IShape_Area() As Double", "End Function")),
    ];

    // the line of a document that has a text.
    private static int LineOf((string FileName, string Text)[] documents, string fileName, string text)
        => Array.FindIndex(documents.Single(document => document.FileName == fileName).Text.Split("\r\n"),
            line => line.Contains(text, StringComparison.Ordinal));

    // the workspace of the two documents and of others, the program's text as it is when the workspace is composed.
    private static RenameService Workspace(Func<string>? programText = null, params (string FileName, string Text)[] others)
    {
        var documents = Substitute.For<IWorkspaceDocumentService>();
        var parsing = Substitute.For<IParsingClientService>();
        documents.WorkspaceRoot.Returns(Root);
        var helpers = new WorkspaceDocument("Helpers.bas", Root, Helpers, version: 1);
        var added = others.Select(other => new WorkspaceDocument(other.FileName, Root, other.Text, version: 1)).ToArray();
        var version = 0;
        WorkspaceDocument Program() => new("Main.rdc", Root, programText?.Invoke() ?? CodeLensWireTests.Program, version: ++version);
        documents.GetAllDocuments().Returns(_ => [helpers, Program(), .. added]);
        foreach (var fileName in new[] { "Helpers.bas", "Main.rdc" }.Concat(others.Select(other => other.FileName)))
        {
            var uri = new Uri(Path.Combine(Root, fileName));
            parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    var document = fileName switch
                    {
                        "Main.rdc" => Program(),
                        "Helpers.bas" => helpers,
                        _ => added.Single(other => other.Id.Uri == Document(fileName)),
                    };
                    return Task.FromResult(new ModuleParser().Parse(document.Id.Uri.ToUri(), ParsingClientService.ModuleSourceOf(document)));
                });
        }

        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StandardLibrary.LibraryName])).ProvideSymbols()]);
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });
        return new RenameService(documents, parsing, new IntrinsicSymbolResolver(), libraries, options, NullLogger<RenameService>.Instance, OneProject.Registry());
    }

    // the lenses of a document, each resolved as a client resolves the lenses it shows.
    private static async Task<(CodeLens[] Answered, CodeLens[] Resolved)> LensesAsync(string fileName, params (string FileName, string Text)[] others)
    {
        // a request that is not answered fails the test, rather than keeping it waiting.
        using var timeout = new CancellationTokenSource(SafetyCap);
        var toServer = new Pipe();
        var toClient = new Pipe();
        var serving = OmniSharpLanguageServer.From(server => server
            .WithInput(toServer.Reader)
            .WithOutput(toClient.Writer)
            .AddHandler(new CodeLensHandler(Workspace(others: others))), timeout.Token);
        var connecting = LanguageClient.From(client => client.WithInput(toClient.Reader).WithOutput(toServer.Writer), timeout.Token);

        await Task.WhenAll(serving, connecting);
        using var server = await serving;
        using var languageClient = await connecting;
        Assert.IsTrue(languageClient.ServerSettings.Capabilities.CodeLensProvider?.ResolveProvider, "a lens is resolved");

        var answered = (await languageClient.SendRequest(new CodeLensParams { TextDocument = new TextDocumentIdentifier(Document(fileName)) }, timeout.Token))!.ToArray();
        var resolved = new List<CodeLens>();
        foreach (var lens in answered)
        {
            resolved.Add(await languageClient.SendRequest(lens, timeout.Token));
        }

        return (answered, [.. resolved]);
    }

    private static string[] ReferencesOf(CodeLens lens)
        => [.. ((JArray)lens.Command!.Arguments![2]).Select(reference => $"{Path.GetFileName(DocumentUri.From(reference.Value<string>("uri")!).GetFileSystemPath())} {reference["range"]!["start"]!.Value<int>("line")}")];

    // the one place a lens goes to: the file of its document, and the line where its range starts.
    private static string PlaceOf(CodeLens lens)
    {
        Assert.AreEqual(RefactoringCommands.GoToLocation, lens.Command!.Name);
        Assert.HasCount(2, lens.Command.Arguments!, "the URI of the document and the range");
        var range = lens.Command.Arguments![1];
        Assert.IsGreaterThan(range["start"]!.Value<int>("character"), range["end"]!.Value<int>("character"), "the range of the name");
        return $"{Path.GetFileName(DocumentUri.From(lens.Command.Arguments![0].Value<string>()!).GetFileSystemPath())} {range["start"]!.Value<int>("line")}";
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task EachMemberOfAModule_HasALens_AtItsName_ThatIsResolvedWithItsReferences()
    {
        var (answered, resolved) = await LensesAsync("Helpers.bas");

        CollectionAssert.AreEqual(new[]
        {
            new Range(2, 12, 2, 17), new Range(5, 12, 5, 16), new Range(8, 20, 8, 25), new Range(11, 20, 11, 25), new Range(14, 11, 14, 15), new Range(17, 17, 17, 23),
        }, answered.Select(lens => lens.Range).ToArray(), "Point, Kind, the Get and the Let of Title, Show, Unused: not mTitle, X or KindA");
        Assert.IsTrue(answered.All(lens => lens.Command is null), "the references are found when a lens is resolved");

        CollectionAssert.AreEqual(new[] { "Main.rdc 3" }, ReferencesOf(resolved[0]), "Point: the Dim of the program, in its text");
        Assert.IsEmpty(ReferencesOf(resolved[1]), "Kind");
        CollectionAssert.AreEquivalent(new[] { "Helpers.bas 15", "Main.rdc 2" }, ReferencesOf(resolved[2]),
            "the Get of Title: what Show prints and what the program prints - not the result its body assigns");
        Assert.IsEmpty(ReferencesOf(resolved[3]), "the Let of Title: nothing assigns the property");
        Assert.AreEqual(RefactoringTitles.References(0), resolved[3].Command!.Title);
        CollectionAssert.AreEqual(new[] { "Main.rdc 0", "Main.rdc 1" }, ReferencesOf(resolved[4]), "Show");
        Assert.IsEmpty(ReferencesOf(resolved[5]), "Unused: the result its body assigns is no reference");

        Assert.IsTrue(resolved.All(lens => lens.Command!.Name == RefactoringCommands.ShowReferences));
        Assert.AreEqual(RefactoringTitles.References(2), resolved[4].Command!.Title);
        Assert.AreEqual(Document("Helpers.bas").ToString(), resolved[4].Command!.Arguments![0].Value<string>());
        Assert.AreEqual(14, resolved[4].Command!.Arguments![1].Value<int>("line"), "the position of the name");
        Assert.AreEqual(11, resolved[4].Command!.Arguments![1].Value<int>("character"));
    }

    // the count of a lens is found of the text as it is when the lens is resolved, whatever was composed before: a refresh of the client's lenses is all a
    // change of what another module's code refers to needs (DocumentLifecycleServiceTests, ALineNumberedTypedInABody_DefinesNothingAgain_...).
    [TestMethod]
    public async Task TheCountOfALens_IsOfTheTextAsItIsWhenItIsResolved()
    {
        var program = "10 Show\r\n";
        var workspace = Workspace(() => program);
        var show = new Position(14, 11);

        var before = (await workspace.ComposeAsync(CancellationToken.None))!.LensReferences(Document("Helpers.bas").ToUri(), show).Count;
        program = "10 Show\r\n20 Show\r\n30 Show\r\n";
        var after = (await workspace.ComposeAsync(CancellationToken.None))!.LensReferences(Document("Helpers.bas").ToUri(), show).Count;

        Assert.AreEqual(1, before);
        Assert.AreEqual(3, after);
    }

    // MS-VBAL §5.2.4.2: a member of a class other classes implement has a lens of the procedures that implement it, which lists them as Go to
    // Implementation does; each procedure that implements it has a lens that names it, answered with the command that goes to its declaration.
    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task AnInterfaceMember_HasALensOfItsImplementations_AndEachImplementation_ALensOfTheMemberItImplements()
    {
        var (answered, resolved) = await LensesAsync("IShape.cls", Shapes);

        Assert.HasCount(2, answered, "the references of Area, and its implementations");
        Assert.AreEqual(answered[0].Range, answered[1].Range);
        Assert.AreEqual(RefactoringTitles.References(0), resolved[0].Command!.Title, "nothing calls Area");
        Assert.AreEqual(RefactoringTitles.Implementations(2), resolved[1].Command!.Title);
        Assert.AreEqual(RefactoringCommands.ShowReferences, resolved[1].Command!.Name);
        var implementation = LineOf(Shapes, "Square.cls", "Private Function IShape_Area");
        CollectionAssert.AreEquivalent(new[] { $"Disc.cls {implementation}", $"Square.cls {implementation}" }, ReferencesOf(resolved[1]));

        var (square, _) = await LensesAsync("Square.cls", Shapes);
        Assert.HasCount(2, square, "the references of IShape_Area, and the member it implements");
        var implements = Assert.ContainsSingle(square.Where(lens => lens.Command is not null), "the lens of the member is answered with its command");
        Assert.AreEqual(RefactoringTitles.Implements("IShape", "Area"), implements.Command!.Title);
        Assert.AreEqual($"IShape.cls {LineOf(Shapes, "IShape.cls", "Public Function Area")}", PlaceOf(implements));
    }

    // a lens that leads to one place goes to it: with one implementation there is nothing to choose from.
    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task AnInterfaceMember_ThatOneProcedureImplements_HasALensThatGoesToIt()
    {
        (string FileName, string Text)[] shapes = [Shapes[0], Shapes[1]];

        var (_, resolved) = await LensesAsync("IShape.cls", shapes);

        Assert.HasCount(2, resolved, "the references of Area, and its implementation");
        Assert.AreEqual(RefactoringCommands.ShowReferences, resolved[0].Command!.Name, "the references are listed, however many there are");
        Assert.AreEqual(RefactoringTitles.Implementations(1), resolved[1].Command!.Title);
        Assert.AreEqual($"Square.cls {LineOf(shapes, "Square.cls", "Private Function IShape_Area")}", PlaceOf(resolved[1]));
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task AProgram_HasNoLens()
        => Assert.IsEmpty((await LensesAsync("Main.rdc")).Answered, "its procedure is the one its lines are the body of");

    [TestMethod]
    [DataRow("en", 0, "0 references")]
    [DataRow("en", 1, "1 reference")]
    [DataRow("en", 2, "2 references")]
    [DataRow("fr", 0, "0 référence")]
    [DataRow("fr", 1, "1 référence")]
    [DataRow("fr", 2, "2 références")]
    public void TheTitleOfALens_IsTheCount_InTheLanguageOfTheReader(string language, int count, string expected)
        => Assert.AreEqual(expected, RefactoringTitles.References(count, CultureInfo.GetCultureInfo(language)));
}
