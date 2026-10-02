using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RDCore.LanguageServer;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Symbols;
using RDCore.SDK.Workspace;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Client;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Configuration;

namespace RDCore.Tests.LanguageServer;

[TestClass]
public sealed class SymbolSyncServiceTests
{
    // an absolute path the URI ctor accepts on both Windows and the Linux CI runner.
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-sync-ws");

    private static ModuleParseResult Parse(string source)
        => new ModuleParser().Parse(new Uri(Path.Combine(Root, "src", "Mod1.bas")), source);

    private static (SymbolSyncService Sut, IRDCoreClientApp Host) Build(
        ModuleParseResult? cached, bool providesCapability = true, DefineSymbolsResult? response = null,
        ImplicitDeclarationScope implicitScope = ImplicitDeclarationScope.Procedure)
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
            orchestration, parsing, documents, new IntrinsicSymbolResolver(), Options(implicitScope),
            NullLogger<SymbolSyncService>.Instance);
        return (sut, host);
    }

    // where an undeclared name declares its variable is the language's to say: a BASIC's is the module, and the other languages' the procedure.
    private static IOptions<SdkAppOptions> Options(ImplicitDeclarationScope implicitScope = ImplicitDeclarationScope.Procedure)
        => Microsoft.Extensions.Options.Options.Create(new SdkAppOptions
        {
            Workspace = new SdkWorkspaceOptions
            {
                Language = SupportedLanguages.All.First(language => language.ImplicitDeclarationScope == implicitScope).Id,
            },
        });

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

    private const string UndeclaredName = "Public Sub Foo()\r\nA = 42\r\nEnd Sub";

    [TestMethod]
    public async Task AtModuleScope_AnUndeclaredName_IsSentAsAVariableOfTheModule_NotALocal()
    {
        // the symbols the host defines have to agree with the dial, however many extraction passes got them there:
        // the third pass runs over a resolver that already holds the first two's variable, and has to declare it anyway.
        var (sut, host) = Build(Parse(UndeclaredName), implicitScope: ImplicitDeclarationScope.Module);

        await sut.SyncWorkspaceAsync(CancellationToken.None);

        await host.Received(1).SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(
            Arg.Is<DefineSymbolsParams>(p =>
                p.Symbols.Any(symbol => symbol.Name == "A" && symbol.Kind == SymbolDescriptorKind.ModuleField)
                && p.Symbols.Single(symbol => symbol.Name == "Foo").Locals.IsDefaultOrEmpty),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ByDefault_AnUndeclaredName_IsSentAsALocalOfItsProcedure()
    {
        var (sut, host) = Build(Parse(UndeclaredName));

        await sut.SyncWorkspaceAsync(CancellationToken.None);

        await host.Received(1).SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(
            Arg.Is<DefineSymbolsParams>(p =>
                !p.Symbols.Any(symbol => symbol.Kind == SymbolDescriptorKind.ModuleField)
                && p.Symbols.Single(symbol => symbol.Name == "Foo").Locals.Any(local => local.Name == "A")),
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

        var sut = new SymbolSyncService(orchestration, parsing, documents, new IntrinsicSymbolResolver(), Options(), NullLogger<SymbolSyncService>.Instance);

        await sut.SyncWorkspaceAsync(CancellationToken.None);

        // its symbols, and then its code
        await host.Received(1).SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(
            Arg.Is<DefineSymbolsParams>(p => p.ModuleName == "Mod2" && !p.CodeOnly), Arg.Any<CancellationToken>());
        await host.Received(1).SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(
            Arg.Is<DefineSymbolsParams>(p => p.ModuleName == "Mod2" && p.CodeOnly), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task TheCodeOfEveryModule_IsSentOnlyOnceEveryModuleIsDefined()
    {
        // a module that names one defined after it is checked against what the workspace declares: which it cannot be until the other is defined.
        var requests = new List<(string Module, bool CodeOnly)>();
        var host = Substitute.For<IRDCoreClientApp>();
        host.WaitForReadyAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        host.PlatformInfo.Returns(new PlatformInitializeResult { Provided = [nameof(DefineSymbols)] });
        host.SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(Arg.Any<DefineSymbolsParams>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<DefineSymbolsParams>();
                requests.Add((request.ModuleName, request.CodeOnly));
                return Task.FromResult(new DefineSymbolsResult { Defined = 1 });
            });

        var orchestration = Substitute.For<IPlatformOrchestrationService>();
        orchestration.RuntimeEnvironment.Returns(host);

        var documents = Substitute.For<IWorkspaceDocumentService>();
        documents.GetAllDocuments().Returns([new WorkspaceDocument("src/Mod1.bas", Root, "content"), new WorkspaceDocument("src/Mod2.bas", Root, "content")]);

        var parsing = Substitute.For<IParsingClientService>();
        parsing.TryGetCached(Arg.Any<Uri>(), out Arg.Any<ModuleParseResult>())
            .Returns(call =>
            {
                var path = ((Uri)call[0]).AbsolutePath;
                var name = path.EndsWith("Mod1.bas", StringComparison.OrdinalIgnoreCase) ? "Mod1" : "Mod2";
                call[1] = new ModuleParser().Parse(new Uri(Path.Combine(Root, "src", $"{name}.bas")), "Public Sub Foo()\r\nEnd Sub");
                return true;
            });

        var sut = new SymbolSyncService(orchestration, parsing, documents, new IntrinsicSymbolResolver(), Options(), NullLogger<SymbolSyncService>.Instance);

        await sut.SyncWorkspaceAsync(CancellationToken.None);

        CollectionAssert.AreEqual(new[] { ("Mod1", false), ("Mod2", false), ("Mod1", true), ("Mod2", true) }, requests);
    }
}
