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
        => new ModuleParser().Parse(new Uri(Path.Combine(Root, "src", "Mod1.bas")), ModuleType.StdModule, source);

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
}
