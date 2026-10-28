using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol;
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
/// <c>textDocument/typeDefinition</c> and <c>textDocument/implementation</c> over the wire, as a client asks them.
/// </summary>
[TestClass]
public sealed class TypeNavigationWireTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-type-navigation-wire-ws");

    private const string IShape = "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\nAttribute VB_Name = \"IShape\"\r\n"
        + "Public Function Area() As Double\r\nEnd Function\r\n";

    private const string Square = "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\nAttribute VB_Name = \"Square\"\r\n"
        + "Implements IShape\r\nPrivate Function IShape_Area() As Double\r\nEnd Function\r\n";

    private const string Main = "Attribute VB_Name = \"Main\"\r\nPublic Sub Run()\r\n    Dim s As IShape\r\n    Debug.Print s.Area\r\nEnd Sub\r\n";

    private static DocumentUri Document(string fileName) => DocumentUri.From(Path.Combine(Root, fileName));

    private static RenameService Workspace()
    {
        var documents = Substitute.For<IWorkspaceDocumentService>();
        var parsing = Substitute.For<IParsingClientService>();
        documents.WorkspaceRoot.Returns(Root);
        WorkspaceDocument[] held = [new("IShape.cls", Root, IShape, version: 1), new("Square.cls", Root, Square, version: 1), new("Main.bas", Root, Main, version: 1)];
        documents.GetAllDocuments().Returns(held);
        foreach (var document in held)
        {
            var uri = document.Id.Uri.ToUri();
            parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ModuleParser().Parse(uri, ParsingClientService.ModuleSourceOf(document))));
        }

        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StdLibSymbolProvider.LibraryName])).ProvideSymbols()]);
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });
        return new RenameService(documents, parsing, new IntrinsicSymbolResolver(), libraries, options, NullLogger<RenameService>.Instance, OneProject.Registry());
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ATypeDefinition_AndAnImplementation_AreAnsweredOverTheWire()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var toServer = new Pipe();
        var toClient = new Pipe();
        var serving = OmniSharpLanguageServer.From(server => server
            .WithInput(toServer.Reader)
            .WithOutput(toClient.Writer)
            .AddHandler(new TypeDefinitionHandler(Workspace()))
            .AddHandler(new ImplementationHandler(Workspace())), timeout.Token);
        var connecting = LanguageClient.From(client => client
            .WithInput(toClient.Reader)
            .WithOutput(toServer.Writer)
            .WithCapability(new TypeDefinitionCapability { LinkSupport = true })
            .WithCapability(new ImplementationCapability { LinkSupport = false }), timeout.Token);

        await Task.WhenAll(serving, connecting);
        using var server = await serving;
        using var languageClient = await connecting;
        var main = new TextDocumentIdentifier(Document("Main.bas"));

        var type = (await languageClient.SendRequest(new TypeDefinitionParams { TextDocument = main, Position = new Position(3, 17) }, timeout.Token))!
            .Single().LocationLink!;
        Assert.AreEqual(Document("IShape.cls"), type.TargetUri, "s is declared As IShape");
        Assert.AreEqual(new Range(3, 16, 3, 17), type.OriginSelectionRange);

        var implementation = (await languageClient.SendRequest(new ImplementationParams { TextDocument = main, Position = new Position(3, 19) }, timeout.Token))!
            .Single().Location!;
        Assert.AreEqual(Document("Square.cls"), implementation.Uri);
        Assert.AreEqual(new Range(6, 17, 6, 28), implementation.Range, "IShape_Area, in Private Function IShape_Area() As Double");
    }

    // an item goes to the client and comes back: what the client sends back is enough to find it again.
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task TheCallHierarchy_AndTheTypeHierarchy_AreAnsweredOverTheWire()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var toServer = new Pipe();
        var toClient = new Pipe();
        var serving = OmniSharpLanguageServer.From(server => server
            .WithInput(toServer.Reader)
            .WithOutput(toClient.Writer)
            .AddHandler(new CallHierarchyPrepareHandler(Workspace()))
            .AddHandler(new CallHierarchyIncomingHandler(Workspace()))
            .AddHandler(new CallHierarchyOutgoingHandler(Workspace()))
            .AddHandler(new TypeHierarchyPrepareHandler(Workspace()))
            .AddHandler(new TypeHierarchySupertypesHandler(Workspace()))
            .AddHandler(new TypeHierarchySubtypesHandler(Workspace())), timeout.Token);
        var connecting = LanguageClient.From(client => client.WithInput(toClient.Reader).WithOutput(toServer.Writer), timeout.Token);

        await Task.WhenAll(serving, connecting);
        using var server = await serving;
        using var languageClient = await connecting;
        var main = new TextDocumentIdentifier(Document("Main.bas"));

        // Area, in Debug.Print s.Area: a function of IShape, which Run calls.
        var area = (await languageClient.SendRequest(new CallHierarchyPrepareParams { TextDocument = main, Position = new Position(3, 19) }, timeout.Token))!.Single();
        Assert.AreEqual("Area", area.Name);
        Assert.AreEqual(Document("IShape.cls"), area.Uri);

        var incoming = (await languageClient.SendRequest(new CallHierarchyIncomingCallsParams { Item = area }, timeout.Token))!.Single();
        Assert.AreEqual("Run", incoming.From.Name);
        Assert.AreEqual(new Range(3, 18, 3, 22), incoming.FromRanges.Single());

        var outgoing = (await languageClient.SendRequest(new CallHierarchyOutgoingCallsParams { Item = incoming.From }, timeout.Token))!.Single();
        Assert.AreEqual("Area", outgoing.To.Name);

        // IShape, in Dim s As IShape.
        var shape = (await languageClient.SendRequest(new TypeHierarchyPrepareParams { TextDocument = main, Position = new Position(2, 16) }, timeout.Token))!.Single();
        var subtypes = (await languageClient.SendRequest(new TypeHierarchySubtypesParams { Item = shape }, timeout.Token))!.ToArray();
        CollectionAssert.AreEqual(new[] { "Square" }, subtypes.Select(type => type.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "IShape" },
            (await languageClient.SendRequest(new TypeHierarchySupertypesParams { Item = subtypes[0] }, timeout.Token))!.Select(type => type.Name).ToArray());
    }
}
