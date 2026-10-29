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

    // NSubstitute's out-parameter support: the callback writes the out value via the call's argument index.
    private static void StubDocument(IWorkspaceDocumentService documents, Uri uri, WorkspaceDocument document)
        => documents.TryGetDocument(uri, out Arg.Any<WorkspaceDocument>())
            .Returns(call => { call[1] = document; return true; });

    [TestMethod]
    public async Task ParseDocumentAsync_WaitsForReady_SendsRequest_AndCaches()
    {
        var (sut, parser, documents) = Build();
        var uri = new Uri("file:///c:/ws/src/Mod1.bas");
        var document = new WorkspaceDocument("Mod1.bas", Root, "Public Sub Foo()\r\nEnd Sub");
        StubDocument(documents, uri, document);

        var result = await sut.ParseDocumentAsync(uri, CancellationToken.None);

        await parser.Received(1).WaitForReadyAsync(Arg.Any<CancellationToken>());
        await parser.Received(1).SendRequestAsync<ParseDocumentParams, PlatformJsonEnvelope>(
            Arg.Is<ParseDocumentParams>(p => p.DocumentUri == uri && p.Fragment == document.Text),
            Arg.Any<CancellationToken>());

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(sut.TryGetCached(uri, out var cached));
        Assert.AreSame(result, cached);
    }

    [TestMethod]
    public async Task ParseDocumentAsync_NoWorkspaceDocumentLoaded_FailsWithoutContactingTheParser()
    {
        var (sut, parser, _) = Build();
        var uri = new Uri("file:///c:/ws/src/Ghost.bas");

        var result = await sut.ParseDocumentAsync(uri, CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(sut.TryGetCached(uri, out var cached));
        Assert.AreSame(result, cached);
        await parser.DidNotReceive().WaitForReadyAsync(Arg.Any<CancellationToken>());
        await parser.DidNotReceive().SendRequestAsync<ParseDocumentParams, PlatformJsonEnvelope>(
            Arg.Any<ParseDocumentParams>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ParseWorkspaceAsync_ParsesEachDocument()
    {
        var (sut, parser, documents) = Build();
        var module = new WorkspaceDocument("src/Mod1.bas", Root, "x");
        var klass = new WorkspaceDocument("src/Cls1.cls", Root, "y");
        documents.GetAllDocuments().Returns([module, klass]);
        StubDocument(documents, module.Id.Uri.ToUri(), module);
        StubDocument(documents, klass.Id.Uri.ToUri(), klass);

        await sut.ParseWorkspaceAsync(CancellationToken.None);

        await parser.Received(1).SendRequestAsync<ParseDocumentParams, PlatformJsonEnvelope>(
            Arg.Is<ParseDocumentParams>(p => p.DocumentUri == module.Id.Uri.ToUri() && p.Fragment == module.Text),
            Arg.Any<CancellationToken>());
        await parser.Received(1).SendRequestAsync<ParseDocumentParams, PlatformJsonEnvelope>(
            Arg.Is<ParseDocumentParams>(p => p.DocumentUri == klass.Id.Uri.ToUri() && p.Fragment == klass.Text),
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
        var module = new WorkspaceDocument("src/Mod1.bas", Root, "x");
        documents.GetAllDocuments().Returns([module]);
        StubDocument(documents, module.Id.Uri.ToUri(), module);
        parser.SendRequestAsync<ParseDocumentParams, PlatformJsonEnvelope>(default!, default)
            .ThrowsForAnyArgs(new InvalidOperationException("boom"));

        await sut.ParseWorkspaceAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task ParseDocumentAsync_NullEnvelope_CachesAFailedResult()
    {
        var (sut, parser, documents) = Build();
        parser.SendRequestAsync<ParseDocumentParams, PlatformJsonEnvelope>(default!, default).ReturnsForAnyArgs((PlatformJsonEnvelope)null!);
        var uri = new Uri("file:///c:/ws/src/Mod1.bas");
        StubDocument(documents, uri, new WorkspaceDocument("Mod1.bas", Root, "x"));

        var result = await sut.ParseDocumentAsync(uri, CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(sut.TryGetCached(uri, out var cached));
        Assert.AreSame(result, cached);
    }

    // ---- a parse is of one version of the text ----

    private static readonly Uri Mod1 = new("file:///c:/ws/src/Mod1.bas");

    [TestMethod]
    public async Task ParseDocumentAsync_OfTheSameVersion_IsNotParsedAgain()
    {
        var (sut, parser, documents) = Build();
        StubDocument(documents, Mod1, new WorkspaceDocument("Mod1.bas", Root, "x", version: 3));

        var first = await sut.ParseDocumentAsync(Mod1, CancellationToken.None);
        var second = await sut.ParseDocumentAsync(Mod1, CancellationToken.None);

        Assert.AreSame(first, second);
        await parser.Received(1).SendRequestAsync<ParseDocumentParams, PlatformJsonEnvelope>(Arg.Any<ParseDocumentParams>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ParseDocumentAsync_OfALaterVersion_IsParsedAgain()
    {
        var (sut, parser, documents) = Build();
        StubDocument(documents, Mod1, new WorkspaceDocument("Mod1.bas", Root, "x", version: 3));
        await sut.ParseDocumentAsync(Mod1, CancellationToken.None);
        StubDocument(documents, Mod1, new WorkspaceDocument("Mod1.bas", Root, "x y", version: 4));

        await sut.ParseDocumentAsync(Mod1, CancellationToken.None);

        await parser.Received(2).SendRequestAsync<ParseDocumentParams, PlatformJsonEnvelope>(Arg.Any<ParseDocumentParams>(), Arg.Any<CancellationToken>());
        Assert.IsTrue(sut.TryGetCached(Mod1, 4, out _));
        Assert.IsFalse(sut.TryGetCached(Mod1, 3, out _), "the parse of text that is not there any more is not the parse of anything");
    }

    [TestMethod]
    public async Task AFailureToParse_IsNotTakenForTheParseOfTheVersion_SoTheNextAskIsAnotherTry()
    {
        var (sut, parser, documents) = Build();
        parser.SendRequestAsync<ParseDocumentParams, PlatformJsonEnvelope>(default!, default).ReturnsForAnyArgs((PlatformJsonEnvelope)null!);
        StubDocument(documents, Mod1, new WorkspaceDocument("Mod1.bas", Root, "x", version: 3));

        await sut.ParseDocumentAsync(Mod1, CancellationToken.None);
        await sut.ParseDocumentAsync(Mod1, CancellationToken.None);

        Assert.IsFalse(sut.TryGetCached(Mod1, 3, out _));
        await parser.Received(2).SendRequestAsync<ParseDocumentParams, PlatformJsonEnvelope>(Arg.Any<ParseDocumentParams>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AnInvalidatedParse_IsParsedAgainWhateverTheVersion()
    {
        var (sut, parser, documents) = Build();
        StubDocument(documents, Mod1, new WorkspaceDocument("Mod1.bas", Root, "x", version: 3));
        await sut.ParseDocumentAsync(Mod1, CancellationToken.None);

        sut.Invalidate(Mod1);
        await sut.ParseDocumentAsync(Mod1, CancellationToken.None);

        Assert.IsTrue(sut.TryGetCached(Mod1, 3, out _));
        await parser.Received(2).SendRequestAsync<ParseDocumentParams, PlatformJsonEnvelope>(Arg.Any<ParseDocumentParams>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AProgramOfTheBasic_IsParsedAsTheModuleItIs()
    {
        var (sut, parser, documents) = Build();
        var uri = new Uri("file:///c:/ws/src/hello.rdc");
        StubDocument(documents, uri, new WorkspaceDocument("hello.rdc", Root, "100 X = 1\r\n"));

        await sut.ParseDocumentAsync(uri, CancellationToken.None);

        await parser.Received(1).SendRequestAsync<ParseDocumentParams, PlatformJsonEnvelope>(
            Arg.Is<ParseDocumentParams>(p => p.Fragment == "Public Sub Main()\r\n100 X = 1\r\nEnd Sub\r\n"), Arg.Any<CancellationToken>());
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
    public async Task AFragmentParsedAgain_IsTheParseItWas_WithoutAskingTheParser()
    {
        var (sut, parser, _) = Build();
        var uri = TestUri.TestModuleUri();

        var first = await sut.ParseFragmentAsync(uri, "Public Sub A()\r\nEnd Sub", CancellationToken.None);
        var again = await sut.ParseFragmentAsync(uri, "Public Sub A()\r\nEnd Sub", CancellationToken.None);
        var other = await sut.ParseFragmentAsync(uri, "Public Sub B()\r\nEnd Sub", CancellationToken.None);

        Assert.AreSame(first, again, "the very same parse: what is composed of it is reused too");
        Assert.AreNotSame(first, other);
        await parser.Received(2).SendRequestAsync<ParseDocumentParams, PlatformJsonEnvelope>(Arg.Any<ParseDocumentParams>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task OnlyTheLastFewFragments_AreKept()
    {
        var (sut, parser, _) = Build();
        var uri = TestUri.TestModuleUri();

        foreach (var name in new[] { "A", "B", "C", "D", "E", "A" })
        {
            await sut.ParseFragmentAsync(uri, $"Public Sub {name}()\r\nEnd Sub", CancellationToken.None);
        }

        await parser.Received(6).SendRequestAsync<ParseDocumentParams, PlatformJsonEnvelope>(Arg.Any<ParseDocumentParams>(), Arg.Any<CancellationToken>());
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

    // a parser whose answers are held until the test gives them, with the token each request was sent with.
    private static List<(TaskCompletionSource<PlatformJsonEnvelope> Answer, CancellationToken Token)> HeldAnswers(IRDCoreClientApp parser)
    {
        var requests = new List<(TaskCompletionSource<PlatformJsonEnvelope>, CancellationToken)>();
        parser.SendRequestAsync<ParseDocumentParams, PlatformJsonEnvelope>(default!, default).ReturnsForAnyArgs(call =>
        {
            var answer = new TaskCompletionSource<PlatformJsonEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (requests)
            {
                requests.Add((answer, call.ArgAt<CancellationToken>(1)));
            }

            return answer.Task;
        });
        return requests;
    }

    private static async Task WhenAsked(List<(TaskCompletionSource<PlatformJsonEnvelope> Answer, CancellationToken Token)> requests, int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            lock (requests)
            {
                if (requests.Count >= count)
                {
                    return;
                }
            }

            Assert.IsLessThan(deadline, DateTime.UtcNow, "the parser was not asked");
            await Task.Delay(5);
        }
    }

    [TestMethod]
    public async Task ParseDocumentAsync_OfAVersionAlreadyBeingParsed_WaitsForThatParse_AndAsksNoOther()
    {
        var (sut, parser, documents) = Build();
        var requests = HeldAnswers(parser);
        var uri = new Uri("file:///c:/ws/src/Mod1.bas");
        StubDocument(documents, uri, new WorkspaceDocument("Mod1.bas", Root, "Public Sub Foo()\r\nEnd Sub"));

        var first = sut.ParseDocumentAsync(uri, CancellationToken.None);
        var second = sut.ParseDocumentAsync(uri, CancellationToken.None);
        await WhenAsked(requests, 1);
        requests[0].Answer.SetResult(SampleEnvelope);

        // one tree, and so one set of node identities: the ones the host is defined from, whichever caller asked first.
        Assert.AreSame(await first, await second);
        Assert.AreSame((await first).SyntaxTree, (await second).SyntaxTree);
        await parser.Received(1).SendRequestAsync<ParseDocumentParams, PlatformJsonEnvelope>(Arg.Any<ParseDocumentParams>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ParseDocumentAsync_OfADocumentOpenedWithTheVersionItWasReadFromDiskWith_ParsesTheClientsText()
    {
        // the client opens a document with its own text - a buffer it restored - and the version the workspace read it from disk with, while the disk text's
        // parse is still out: what is parsed and kept for that version is the client's text.
        var (sut, parser, documents) = Build();
        var requests = HeldAnswers(parser);
        var uri = new Uri("file:///c:/ws/src/Mod1.bas");
        StubDocument(documents, uri, new WorkspaceDocument("Mod1.bas", Root, "Public Sub Disk()\r\nEnd Sub"));

        var fromDisk = sut.ParseDocumentAsync(uri, CancellationToken.None);
        await WhenAsked(requests, 1);
        StubDocument(documents, uri, new WorkspaceDocument("Mod1.bas", Root, "Public Sub Client()\r\nEnd Sub"));
        sut.Invalidate(uri);
        var opened = sut.ParseDocumentAsync(uri, CancellationToken.None);
        await WhenAsked(requests, 2);

        Assert.IsTrue(requests[0].Token.IsCancellationRequested, "the disk text's parse was told to stop");
        requests[0].Answer.TrySetCanceled(requests[0].Token);
        requests[1].Answer.SetResult(SampleEnvelope);
        Assert.AreSame(await opened, await fromDisk, "the caller of the disk text's parse is given the parse of the text the document reads now");
        await parser.Received(1).SendRequestAsync<ParseDocumentParams, PlatformJsonEnvelope>(
            Arg.Is<ParseDocumentParams>(request => request.Fragment!.Contains("Client")), Arg.Any<CancellationToken>());
        Assert.IsTrue(sut.TryGetCached(uri, 1, out var kept));
        Assert.AreSame(await opened, kept);
    }

    [TestMethod]
    public async Task ParseDocumentAsync_OfALaterVersion_TellsTheParserToStopTheEarlierOne_WhoseCallerIsGivenTheLaterParse()
    {
        var (sut, parser, documents) = Build();
        var requests = HeldAnswers(parser);
        var uri = new Uri("file:///c:/ws/src/Mod1.bas");
        var document = new WorkspaceDocument("Mod1.bas", Root, "Public Sub Foo()\r\nEnd Sub");
        StubDocument(documents, uri, document);

        var earlier = sut.ParseDocumentAsync(uri, CancellationToken.None);
        await WhenAsked(requests, 1);
        StubDocument(documents, uri, document.WithText("Public Sub Foo()\r\n    Debug.Print 1\r\nEnd Sub"));
        var later = sut.ParseDocumentAsync(uri, CancellationToken.None);
        await WhenAsked(requests, 2);

        Assert.IsTrue(requests[0].Token.IsCancellationRequested, "the earlier parse was told to stop");
        Assert.IsFalse(requests[1].Token.IsCancellationRequested);
        requests[0].Answer.SetCanceled(requests[0].Token);
        requests[1].Answer.SetResult(SampleEnvelope);
        Assert.IsTrue((await later).IsSuccess);

        // a superseded parse is no failure of its caller's: it is given the parse of what the document reads now, which it joins.
        Assert.AreSame(await later, await earlier);
        await parser.Received(2).SendRequestAsync<ParseDocumentParams, PlatformJsonEnvelope>(Arg.Any<ParseDocumentParams>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ParseDocumentAsync_ThatEveryCallerGaveUpOn_TellsTheParserToStop()
    {
        var (sut, parser, documents) = Build();
        var requests = HeldAnswers(parser);
        var uri = new Uri("file:///c:/ws/src/Mod1.bas");
        StubDocument(documents, uri, new WorkspaceDocument("Mod1.bas", Root, "Public Sub Foo()\r\nEnd Sub"));
        using var caller = new CancellationTokenSource();

        var parse = sut.ParseDocumentAsync(uri, caller.Token);
        await WhenAsked(requests, 1);
        await caller.CancelAsync();

        _ = await Assert.ThrowsAsync<OperationCanceledException>(() => parse);
        Assert.IsTrue(requests[0].Token.IsCancellationRequested);
    }
}
