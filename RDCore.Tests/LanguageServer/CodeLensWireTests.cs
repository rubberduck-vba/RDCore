using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Refactoring;
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
        End Function
        """;

    private const string Program = "10 Show\r\n20 Show\r\n30 Title = \"a\"\r\n40 Dim p As Point\r\n";

    private static DocumentUri Document(string fileName) => DocumentUri.From(Path.Combine(Root, fileName));

    private static RenameService Workspace()
    {
        var documents = Substitute.For<IWorkspaceDocumentService>();
        var parsing = Substitute.For<IParsingClientService>();
        documents.WorkspaceRoot.Returns(Root);
        var helpers = new WorkspaceDocument("Helpers.bas", Root, Helpers, version: 1);
        var program = new WorkspaceDocument("Main.rdc", Root, Program, version: 1);
        documents.GetAllDocuments().Returns([helpers, program]);
        foreach (var document in new[] { helpers, program })
        {
            var uri = document.Id.Uri.ToUri();
            parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ModuleParser().Parse(uri, ParsingClientService.ModuleSourceOf(document))));
        }

        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StdLibSymbolProvider.LibraryName])).ProvideSymbols()]);
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });
        return new RenameService(documents, parsing, new IntrinsicSymbolResolver(), libraries, options, NullLogger<RenameService>.Instance);
    }

    // the lenses of a document, each resolved as a client resolves the lenses it shows.
    private static async Task<(CodeLens[] Answered, CodeLens[] Resolved)> LensesAsync(string fileName)
    {
        // a request that is not answered fails the test, rather than keeping it waiting.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var toServer = new Pipe();
        var toClient = new Pipe();
        var serving = OmniSharpLanguageServer.From(server => server
            .WithInput(toServer.Reader)
            .WithOutput(toClient.Writer)
            .AddHandler(new CodeLensHandler(Workspace())), timeout.Token);
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

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
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
        CollectionAssert.AreEquivalent(new[] { "Helpers.bas 9", "Helpers.bas 15", "Main.rdc 2" }, ReferencesOf(resolved[2]),
            "Title: what its Get assigns, what Show prints, and what the program assigns");
        CollectionAssert.AreEquivalent(ReferencesOf(resolved[2]), ReferencesOf(resolved[3]), "the procedures of a property are one symbol");
        CollectionAssert.AreEqual(new[] { "Main.rdc 0", "Main.rdc 1" }, ReferencesOf(resolved[4]), "Show");
        Assert.IsEmpty(ReferencesOf(resolved[5]), "Unused");

        Assert.IsTrue(resolved.All(lens => lens.Command!.Name == RefactoringCommands.ShowReferences));
        Assert.AreEqual(RefactoringTitles.References(2), resolved[4].Command!.Title);
        Assert.AreEqual(Document("Helpers.bas").ToString(), resolved[4].Command!.Arguments![0].Value<string>());
        Assert.AreEqual(14, resolved[4].Command!.Arguments![1].Value<int>("line"), "the position of the name");
        Assert.AreEqual(11, resolved[4].Command!.Arguments![1].Value<int>("character"));
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
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
