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

    private static ModuleParseResult SampleResult
        => new ModuleParser().Parse(TestUri.TestModuleUri(), ModuleType.StdModule, "Public Sub Foo()\r\nEnd Sub");

    private static (ParsingClientService Sut, IRDCoreClientApp Parser, IWorkspaceDocumentService Documents) Build()
    {
        var parser = Substitute.For<IRDCoreClientApp>();
        parser.WaitForReadyAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        parser.SendRequestAsync<ParseDocumentParams, ModuleParseResult>(default!, default).ReturnsForAnyArgs(SampleResult);

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

        var result = await sut.ParseDocumentAsync(uri, ModuleType.StdModule, CancellationToken.None);

        await parser.Received(1).WaitForReadyAsync(Arg.Any<CancellationToken>());
        await parser.Received(1).SendRequestAsync<ParseDocumentParams, ModuleParseResult>(
            Arg.Is<ParseDocumentParams>(p => p.DocumentUri == uri && p.ModuleType == ModuleType.StdModule),
            Arg.Any<CancellationToken>());

        Assert.IsTrue(sut.TryGetCached(uri, out var cached));
        Assert.AreSame(result, cached);
    }

    [TestMethod]
    public async Task ParseWorkspaceAsync_ParsesEachDocument_WithModuleTypeFromExtension()
    {
        var (sut, parser, documents) = Build();
        var module = new WorkspaceDocument("src/Mod1.bas", Root, "x");
        var klass = new WorkspaceDocument("src/Cls1.cls", Root, "y");
        documents.GetAllDocuments().Returns([module, klass]);

        await sut.ParseWorkspaceAsync(CancellationToken.None);

        await parser.Received(1).SendRequestAsync<ParseDocumentParams, ModuleParseResult>(
            Arg.Is<ParseDocumentParams>(p => p.DocumentUri == module.Id.Uri.ToUri() && p.ModuleType == ModuleType.StdModule),
            Arg.Any<CancellationToken>());
        await parser.Received(1).SendRequestAsync<ParseDocumentParams, ModuleParseResult>(
            Arg.Is<ParseDocumentParams>(p => p.DocumentUri == klass.Id.Uri.ToUri() && p.ModuleType == ModuleType.ClassModule),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ParseWorkspaceAsync_DoesNotThrow_WhenAParseRequestFails()
    {
        var (sut, parser, documents) = Build();
        documents.GetAllDocuments().Returns([new WorkspaceDocument("src/Mod1.bas", Root, "x")]);
        parser.SendRequestAsync<ParseDocumentParams, ModuleParseResult>(default!, default)
            .ThrowsForAnyArgs(new InvalidOperationException("boom"));

        await sut.ParseWorkspaceAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task ParseDocumentAsync_NullResult_CachesAFailedResult()
    {
        var (sut, parser, _) = Build();
        parser.SendRequestAsync<ParseDocumentParams, ModuleParseResult>(default!, default).ReturnsForAnyArgs((ModuleParseResult)null!);
        var uri = new Uri("file:///c:/ws/src/Mod1.bas");

        var result = await sut.ParseDocumentAsync(uri, ModuleType.StdModule, CancellationToken.None);

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
}
