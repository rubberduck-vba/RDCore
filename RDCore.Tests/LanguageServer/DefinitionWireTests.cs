using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Navigation;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;
using System.IO.Pipelines;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// <c>textDocument/definition</c> and <c>textDocument/declaration</c> over the wire, as a client asks them: links for a client that reads them, locations for one that does not, each in the text
/// of the document - a program of the platform's BASIC is its lines.
/// </summary>
[TestClass]
public sealed class DefinitionWireTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-definition-wire-ws");

    private const string Helpers = """
        Attribute VB_Name = "Helpers"
        Public strTitle As String

        Public Sub Show()
            Debug.Print strTitle
        End Sub
        """;

    private const string Program = "5 Dim count As Long\r\n10 count = 1\r\n20 strTitle = \"Hello\"\r\n30 Show\r\n40 MsgBox strTitle\r\n";

    private static Uri Document(string fileName) => DocumentUri.From(Path.Combine(Root, fileName)).ToUri();

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

    // the answers to each position of the program, from a client that does or does not read links.
    private static Task<LocationOrLocationLinks?[]> AskAsync(bool linkSupport, params Position[] positions)
        => AskAsync(linkSupport, (document, position) => new DefinitionParams { TextDocument = document, Position = position }, positions);

    private static async Task<LocationOrLocationLinks?[]> AskAsync(
        bool linkSupport, Func<TextDocumentIdentifier, Position, IRequest<LocationOrLocationLinks>> request, params Position[] positions)
    {
        // a request that is not answered fails the test, rather than keeping it waiting.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var toServer = new Pipe();
        var toClient = new Pipe();
        var serving = OmniSharpLanguageServer.From(server => server
            .WithInput(toServer.Reader)
            .WithOutput(toClient.Writer)
            .AddHandler(new DefinitionHandler(Workspace()))
            .AddHandler(new DeclarationHandler(Workspace())), timeout.Token);
        var connecting = LanguageClient.From(client => client
            .WithInput(toClient.Reader)
            .WithOutput(toServer.Writer)
            .WithCapability(new DefinitionCapability { LinkSupport = linkSupport })
            .WithCapability(new DeclarationCapability { LinkSupport = linkSupport }), timeout.Token);

        await Task.WhenAll(serving, connecting);
        using var server = await serving;
        using var languageClient = await connecting;
        var document = new TextDocumentIdentifier(DocumentUri.From(Document("Main.rdc")));

        var answers = new List<LocationOrLocationLinks?>();
        foreach (var position in positions)
        {
            answers.Add(await languageClient.SendRequest(request(document, position), timeout.Token));
        }

        return [.. answers];
    }

    // one request of the program, to a server of the references and the highlights.
    private static async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var toServer = new Pipe();
        var toClient = new Pipe();
        var serving = OmniSharpLanguageServer.From(server => server
            .WithInput(toServer.Reader)
            .WithOutput(toClient.Writer)
            .AddHandler(new ReferencesHandler(Workspace()))
            .AddHandler(new DocumentHighlightHandler(Workspace())), timeout.Token);
        var connecting = LanguageClient.From(client => client.WithInput(toClient.Reader).WithOutput(toServer.Writer), timeout.Token);

        await Task.WhenAll(serving, connecting);
        using var server = await serving;
        using var languageClient = await connecting;
        return await languageClient.SendRequest(request, timeout.Token);
    }

    private static TextDocumentIdentifier MainProgram() => new(DocumentUri.From(Document("Main.rdc")));

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task TheReferencesOfAName_AreInEveryDocument_InTheirText()
    {
        var references = (await SendAsync(new ReferenceParams
        {
            TextDocument = MainProgram(),
            Position = new Position(2, 5),
            Context = new ReferenceContext { IncludeDeclaration = true },
        }))!.ToArray();

        var helpers = DocumentUri.From(Document("Helpers.bas"));
        var main = DocumentUri.From(Document("Main.rdc"));
        CollectionAssert.AreEquivalent(new[]
        {
            (helpers, new Range(1, 7, 1, 15)), (helpers, new Range(4, 16, 4, 24)), (main, new Range(2, 3, 2, 11)), (main, new Range(4, 10, 4, 18)),
        }, references.Select(reference => (reference.Uri, reference.Range)).ToArray());
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task TheHighlightsOfAName_AreItsReferences_InTheDocument()
    {
        var highlights = (await SendAsync(new DocumentHighlightParams { TextDocument = MainProgram(), Position = new Position(1, 4) }))!.ToArray();

        CollectionAssert.AreEqual(new[] { new Range(0, 6, 0, 11), new Range(1, 3, 1, 8) }, highlights.Select(highlight => highlight.Range).ToArray(),
            "count: its Dim, and the line that assigns it");
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AClientThatReadsLinks_IsAnsweredWithTheName_TheDeclaration_AndTheNameItDeclares()
    {
        var answers = await AskAsync(linkSupport: true, new Position(2, 5), new Position(1, 4));

        var global = answers[0]!.Single().LocationLink!;
        Assert.AreEqual(DocumentUri.From(Document("Helpers.bas")), global.TargetUri);
        Assert.AreEqual(new Range(2, 3, 2, 11), global.OriginSelectionRange, "the name in the text of the program");
        Assert.AreEqual(new Range(1, 7, 1, 15), global.TargetSelectionRange);

        var local = answers[1]!.Single().LocationLink!;
        Assert.AreEqual(DocumentUri.From(Document("Main.rdc")), local.TargetUri);
        Assert.AreEqual(new Range(0, 6, 0, 11), local.TargetSelectionRange, "the Dim, on the program's first line");
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AClientThatDoesNotReadLinks_IsAnsweredWithTheNameItDeclares()
    {
        var answers = await AskAsync(linkSupport: false, new Position(3, 3));

        var location = answers[0]!.Single().Location!;
        Assert.AreEqual(DocumentUri.From(Document("Helpers.bas")), location.Uri);
        Assert.AreEqual(new Range(3, 11, 3, 15), location.Range, "Show, in Public Sub Show()");
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ADeclaration_IsAnsweredWithTheOnePlaceThatDeclaresTheSymbol()
    {
        var answers = await AskAsync(linkSupport: true, (document, position) => new DeclarationParams { TextDocument = document, Position = position },
            new Position(2, 5), new Position(4, 4));

        var declaration = answers[0]!.Single().LocationLink!;
        Assert.AreEqual(DocumentUri.From(Document("Helpers.bas")), declaration.TargetUri);
        Assert.AreEqual(new Range(1, 7, 1, 15), declaration.TargetSelectionRange);
        Assert.IsEmpty(answers[1] ?? new LocationOrLocationLinks(), "MsgBox: no document of the workspace declares it");
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ANameOfTheLibrary_AndNoName_AreAnsweredWithNothing()
    {
        var answers = await AskAsync(linkSupport: true, new Position(4, 4), new Position(1, 9));

        Assert.IsEmpty(answers[0] ?? new LocationOrLocationLinks(), "MsgBox: no document of the workspace declares it");
        Assert.IsEmpty(answers[1] ?? new LocationOrLocationLinks(), "the = of count = 1");
    }
}
