using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RDCore.LanguageServer;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Client;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.LanguageServer;

[TestClass]
public sealed class ParsingClientServiceTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-ws");

    private static PlatformJsonEnvelope SampleEnvelope
        => PlatformJsonEnvelope.Of(new ModuleParser().Parse(TestUri.TestModuleUri(), "Public Sub Foo()\r\nEnd Sub"));

    private static (ParsingClientService Sut, IRDCoreClientApp Parser, IWorkspaceDocumentService Documents) Build()
    {
        var parser = Substitute.For<IRDCoreClientApp>();
        parser.WaitForReadyAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        parser.SendRequestAsync<ParseDocumentParams, PlatformJsonEnvelope>(default!, default).ReturnsForAnyArgs(SampleEnvelope);

        var orchestration = Substitute.For<IPlatformOrchestrationService>();
        orchestration.ParsingService.Returns(parser);

        var documents = Substitute.For<IWorkspaceDocumentService>();

        return (new ParsingClientService(orchestration, documents, NullLogger<ParsingClientService>.Instance), parser, documents);
    }

    [TestMethod]
    public async Task ParseDocumentAsync_WaitsForReady_SendsRequest_AndCaches()
    {
        var (sut, parser, _) = Build();
        var uri = new Uri("file:///c:/ws/src/Mod1.bas");

        var result = await sut.ParseDocumentAsync(uri, CancellationToken.None);

        await parser.Received(1).WaitForReadyAsync(Arg.Any<CancellationToken>());
        await parser.Received(1).SendRequestAsync<ParseDocumentParams, PlatformJsonEnvelope>(
            Arg.Is<ParseDocumentParams>(p => p.DocumentUri == uri),
            Arg.Any<CancellationToken>());

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(sut.TryGetCached(uri, out var cached));
        Assert.AreSame(result, cached);
    }

    [TestMethod]
    public async Task ParseWorkspaceAsync_ParsesEachDocument()
    {
        var (sut, parser, documents) = Build();
        var module = new WorkspaceDocument("src/Mod1.bas", Root, "x");
        var klass = new WorkspaceDocument("src/Cls1.cls", Root, "y");
        documents.GetAllDocuments().Returns([module, klass]);

        await sut.ParseWorkspaceAsync(CancellationToken.None);

        await parser.Received(1).SendRequestAsync<ParseDocumentParams, PlatformJsonEnvelope>(
            Arg.Is<ParseDocumentParams>(p => p.DocumentUri == module.Id.Uri.ToUri()),
            Arg.Any<CancellationToken>());
        await parser.Received(1).SendRequestAsync<ParseDocumentParams, PlatformJsonEnvelope>(
            Arg.Is<ParseDocumentParams>(p => p.DocumentUri == klass.Id.Uri.ToUri()),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public void ModuleTypeOf_ReadsTheVersionHeader_NotTheExtension()
    {
        // the header, not the .cls/.bas extension, is the signal (RDCore.SDK.Workspace.ModuleHeader).
        var classWithoutClsExtension = new WorkspaceDocument(
            "src/Weird.txt", Root, "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\nAttribute VB_Name = \"Weird\"\r\n");
        var standardWithClsExtension = new WorkspaceDocument(
            "src/NotReally.cls", Root, "Attribute VB_Name = \"NotReally\"\r\n");

        Assert.AreEqual(ModuleType.ClassModule, ParsingClientService.ModuleTypeOf(classWithoutClsExtension));
        Assert.AreEqual(ModuleType.StdModule, ParsingClientService.ModuleTypeOf(standardWithClsExtension));
    }

    [TestMethod]
    public async Task ParseWorkspaceAsync_DoesNotThrow_WhenAParseRequestFails()
    {
        var (sut, parser, documents) = Build();
        documents.GetAllDocuments().Returns([new WorkspaceDocument("src/Mod1.bas", Root, "x")]);
        parser.SendRequestAsync<ParseDocumentParams, PlatformJsonEnvelope>(default!, default)
            .ThrowsForAnyArgs(new InvalidOperationException("boom"));

        await sut.ParseWorkspaceAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task ParseDocumentAsync_NullEnvelope_CachesAFailedResult()
    {
        var (sut, parser, _) = Build();
        parser.SendRequestAsync<ParseDocumentParams, PlatformJsonEnvelope>(default!, default).ReturnsForAnyArgs((PlatformJsonEnvelope)null!);
        var uri = new Uri("file:///c:/ws/src/Mod1.bas");

        var result = await sut.ParseDocumentAsync(uri, CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(sut.TryGetCached(uri, out var cached));
        Assert.AreSame(result, cached);
    }

    [TestMethod]
    public void ParseDocumentParams_CarriesTheMethodAttribute()
    {
        var method = typeof(ParseDocumentParams)
            .GetCustomAttributes(typeof(OmniSharp.Extensions.JsonRpc.MethodAttribute), false)
            .Cast<OmniSharp.Extensions.JsonRpc.MethodAttribute>()
            .SingleOrDefault();

        Assert.IsNotNull(method, "ParseDocumentParams needs [Method] so the JSON-RPC layer can infer the request method by type.");
        Assert.AreEqual(RDCorePlatformProtocol.ParseFullDocument, method.Method);
    }

    [TestMethod]
    public void PlatformJsonEnvelope_RoundTripsAPolymorphicParseResult()
    {
        var original = new ModuleParser().Parse(TestUri.TestModuleUri(),
            "Public Function Add(ByVal a As Long) As Long\r\nEnd Function");

        var result = PlatformJsonEnvelope.Of(original).Unwrap<ModuleParseResult>();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(original.SyntaxTree!.Children.Length, result.SyntaxTree!.Children.Length);
    }
}
