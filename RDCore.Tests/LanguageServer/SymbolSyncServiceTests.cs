using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.LanguageServer;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Client;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.LanguageServer;

[TestClass]
public sealed class SymbolSyncServiceTests
{
    // an absolute path the URI ctor accepts on both Windows and the Linux CI runner.
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-sync-ws");

    private static ModuleParseResult Parse(string source)
        => new ModuleParser().Parse(new Uri(Path.Combine(Root, "src", "Mod1.bas")), source);

    private static (SymbolSyncService Sut, IRDCoreClientApp Host) Build(
        ModuleParseResult? cached, bool providesCapability = true, DefineSymbolsResult? response = null)
    {
        var host = Substitute.For<IRDCoreClientApp>();
        host.WaitForReadyAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        host.PlatformInfo.Returns(new PlatformInitializeResult
        {
            Provided = providesCapability ? [nameof(DefineSymbols)] : [],
        });
        host.SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(Arg.Any<DefineSymbolsParams>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response ?? new DefineSymbolsResult { Defined = 1 }));

        var orchestration = Substitute.For<IPlatformOrchestrationService>();
        orchestration.RuntimeEnvironment.Returns(host);

        var document = new WorkspaceDocument("src/Mod1.bas", Root, "content");
        var documents = Substitute.For<IWorkspaceDocumentService>();
        documents.GetAllDocuments().Returns([document]);

        var parsing = Substitute.For<IParsingClientService>();
        parsing.TryGetCached(Arg.Any<Uri>(), out Arg.Any<ModuleParseResult>())
            .Returns(call =>
            {
                if (cached is null)
                {
                    return false;
                }
                call[1] = cached;
                return true;
            });

        var sut = new SymbolSyncService(
            orchestration, parsing, documents, new IntrinsicSymbolResolver(), NullLogger<SymbolSyncService>.Instance);
        return (sut, host);
    }

    [TestMethod]
    public async Task SendsModuleDescriptors_ForEachCachedDocument()
    {
        var (sut, host) = Build(Parse("Public Sub Foo()\r\nEnd Sub\r\nPublic Function Bar() As Long\r\nEnd Function"));

        await sut.SyncWorkspaceAsync(CancellationToken.None);

        await host.Received(1).SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(
            Arg.Is<DefineSymbolsParams>(p =>
                p.ModuleName == "Mod1"
                && p.Symbols.Length == 2
                && p.WorkspaceRoot!.ToString() == new Uri(Root).ToString()),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task SkipsDocumentsWithNoCachedParseResult()
    {
        var (sut, host) = Build(cached: null);

        await sut.SyncWorkspaceAsync(CancellationToken.None);

        await host.DidNotReceive().SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(
            Arg.Any<DefineSymbolsParams>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task DoesNothing_WhenHostDoesNotProvideTheCapability()
    {
        var (sut, host) = Build(Parse("Public Sub Foo()\r\nEnd Sub"), providesCapability: false);

        await sut.SyncWorkspaceAsync(CancellationToken.None);

        await host.DidNotReceive().SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(
            Arg.Any<DefineSymbolsParams>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    // adversarial review PRs #208-224, item 7 / "if you only fix three things" #3: the per-module loop
    // had no try/catch of its own - the outer catch aborted the whole sync on the first module that
    // threw, permanently costing every module after it its symbols for the rest of the session (this
    // sync is one-shot, not re-run on didOpen/didChange). One module's own defined-symbols request
    // failing must not stop the loop from reaching the next module.
    public async Task OneModuleFailing_DoesNotPreventTheRemainingModulesFromSyncing()
    {
        var host = Substitute.For<IRDCoreClientApp>();
        host.WaitForReadyAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        host.PlatformInfo.Returns(new PlatformInitializeResult { Provided = [nameof(DefineSymbols)] });
        host.SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(
                Arg.Is<DefineSymbolsParams>(p => p.ModuleName == "Mod1"), Arg.Any<CancellationToken>())
            .Returns<Task<DefineSymbolsResult>>(_ => throw new InvalidOperationException("simulated failure"));
        host.SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(
                Arg.Is<DefineSymbolsParams>(p => p.ModuleName == "Mod2"), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DefineSymbolsResult { Defined = 1 }));

        var orchestration = Substitute.For<IPlatformOrchestrationService>();
        orchestration.RuntimeEnvironment.Returns(host);

        var doc1 = new WorkspaceDocument("src/Mod1.bas", Root, "content");
        var doc2 = new WorkspaceDocument("src/Mod2.bas", Root, "content");
        var documents = Substitute.For<IWorkspaceDocumentService>();
        documents.GetAllDocuments().Returns([doc1, doc2]);

        var parse1 = Parse("Public Sub Foo()\r\nEnd Sub");
        var parse2 = new ModuleParser().Parse(new Uri(Path.Combine(Root, "src", "Mod2.bas")), "Public Sub Bar()\r\nEnd Sub");
        var parsing = Substitute.For<IParsingClientService>();
        parsing.TryGetCached(Arg.Any<Uri>(), out Arg.Any<ModuleParseResult>())
            .Returns(call =>
            {
                var path = ((Uri)call[0]).AbsolutePath;
                if (path.EndsWith("Mod1.bas", StringComparison.OrdinalIgnoreCase)) { call[1] = parse1; return true; }
                if (path.EndsWith("Mod2.bas", StringComparison.OrdinalIgnoreCase)) { call[1] = parse2; return true; }
                return false;
            });

        var sut = new SymbolSyncService(orchestration, parsing, documents, new IntrinsicSymbolResolver(), NullLogger<SymbolSyncService>.Instance);

        await sut.SyncWorkspaceAsync(CancellationToken.None);

        await host.Received(1).SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(
            Arg.Is<DefineSymbolsParams>(p => p.ModuleName == "Mod2"), Arg.Any<CancellationToken>());
    }
}
