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
using RDCore.Runtime.LanguageCore.Errors;
using RDCore.SDK.Client;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace.Forms;
using RDCore.Symbols;
using RDCore.SDK.Platform.Components;

namespace RDCore.Tests.LanguageServer;

[TestClass]
public sealed class SymbolSyncServiceTests
{
    // an absolute path the URI ctor accepts on both Windows and the Linux CI runner.
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-sync-ws");

    private static ModuleParseResult Parse(string source)
        => new ModuleParser().Parse(new Uri(Path.Combine(Root, "src", "Mod1.bas")), source);

    // a workspace of one project.
    private static IWorkspaceProjects NoOtherProjects()
    {
        var projects = Substitute.For<IWorkspaceProjects>();
        projects.Projects.Returns([]);
        return projects;
    }

    // a project that references no library but the standard library.
    private sealed class NoLibraries : IReferencedLibraryService
    {
        public RDCore.SDK.Runtime.Libraries.ReferencedLibraries Libraries => RDCore.SDK.Runtime.Libraries.ReferencedLibraries.None;

        public IReadOnlyList<RDCore.SDK.Model.Symbols.Abstract.Symbol> SymbolsUnder(Uri workspaceRoot) => [];

        public IReadOnlyList<RDCore.SDK.Model.Symbols.Abstract.Symbol> SymbolsOf(Uri workspaceRoot, RDCore.SDK.Workspace.RDCoreProject project) => [];

        public IReadOnlyList<RDCore.SDK.Model.Symbols.Abstract.Symbol> DescribedSymbolsOf(Uri workspaceRoot, string library) => [];
    }

    private static (SymbolSyncService Sut, IRDCoreClientApp Host) Build(
        ModuleParseResult? cached, bool providesCapability = true, DefineSymbolsResult? response = null,
        ImplicitDeclarationScope implicitScope = ImplicitDeclarationScope.Procedure,
        Microsoft.Extensions.Logging.ILogger<SymbolSyncService>? logger = null, DocClassType? super = null)
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

        var document = new WorkspaceDocument("src/Mod1.bas", Root, "content") { Super = super };
        var documents = Substitute.For<IWorkspaceDocumentService>();
        documents.GetAllDocuments().Returns([document]);
        documents.WorkspaceRoot.Returns(Root);
        documents.TryGetDocument(Arg.Any<Uri>(), out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = document; return true; });

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
        parsing.TryGetCached(Arg.Any<Uri>(), Arg.Any<int>(), out Arg.Any<ModuleParseResult>())
            .Returns(call =>
            {
                if (cached is null)
                {
                    return false;
                }
                call[2] = cached;
                return true;
            });

        var sut = new SymbolSyncService(
            orchestration, parsing, documents, new IntrinsicSymbolResolver(), Options(implicitScope),
            logger ?? NullLogger<SymbolSyncService>.Instance, new NoLibraries(), NoOtherProjects());
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

    // the parser's JSON of a parse, told apart from what writing the same parse again gives by the space it starts with, which a reader skips.
    private static PlatformJsonEnvelope Answered(ModuleParseResult parse) => new() { Json = " " + PlatformJson.Serialize(parse) };

    [TestMethod]
    public async Task AParseTheParserAnswered_GoesToTheHost_AsTheJsonItCameIn()
    {
        var envelope = Answered(Parse("Public Sub Foo()\r\nEnd Sub"));
        var (sut, host) = Build(ParserJson.Unwrap(envelope));

        await sut.SyncWorkspaceAsync(CancellationToken.None);

        await host.Received(1).SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(
            Arg.Is<DefineSymbolsParams>(p => p.CodeOnly && p.ParseResultJson == envelope.Json), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AParseTheLanguageServerChanged_GoesToTheHost_WrittenAgain()
    {
        var changed = ParserJson.Unwrap(Answered(Parse("Public Sub Foo()\r\nEnd Sub"))) with
        {
            Designer = new FormDescriptor("UserForm1", FormDesignerKind.MSForms, new FormControlClass("MSForms", "UserForm"),
                new Dictionary<string, string>(), []),
        };
        var (sut, host) = Build(changed);

        await sut.SyncWorkspaceAsync(CancellationToken.None);

        await host.Received(1).SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(
            Arg.Is<DefineSymbolsParams>(p => p.CodeOnly && p.ParseResultJson == PlatformJson.Serialize(changed)), Arg.Any<CancellationToken>());
        Assert.IsNotNull(PlatformJson.Deserialize<ModuleParseResult>(PlatformJson.Serialize(changed)).Designer);
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

        var sut = new SymbolSyncService(orchestration, parsing, documents, new IntrinsicSymbolResolver(), Options(), NullLogger<SymbolSyncService>.Instance, new NoLibraries(), NoOtherProjects());

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

        var sut = new SymbolSyncService(orchestration, parsing, documents, new IntrinsicSymbolResolver(), Options(), NullLogger<SymbolSyncService>.Instance, new NoLibraries(), NoOtherProjects());

        await sut.SyncWorkspaceAsync(CancellationToken.None);

        CollectionAssert.AreEqual(new[] { ("Mod1", false), ("Mod2", false), ("Mod1", true), ("Mod2", true) }, requests);
    }

    // a module the host could not be given is told, with why: the host has it as it was, and a program that waits on it does not have its code.
    [TestMethod]
    public async Task AModuleTheHostCouldNotBeGiven_IsUnsynced_AndTheOthersAreNot()
    {
        var host = Substitute.For<IRDCoreClientApp>();
        host.WaitForReadyAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        host.PlatformInfo.Returns(new PlatformInitializeResult { Provided = [nameof(DefineSymbols)] });
        host.SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(Arg.Any<DefineSymbolsParams>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<DefineSymbolsParams>() is { ModuleName: "Mod2", CodeOnly: true }
                ? Task.FromException<DefineSymbolsResult>(new InvalidOperationException("the host refused it"))
                : Task.FromResult(new DefineSymbolsResult { Defined = 1 }));
        var orchestration = Substitute.For<IPlatformOrchestrationService>();
        orchestration.RuntimeEnvironment.Returns(host);
        var documents = Substitute.For<IWorkspaceDocumentService>();
        documents.GetAllDocuments().Returns([new WorkspaceDocument("src/Mod1.bas", Root, "content"), new WorkspaceDocument("src/Mod2.bas", Root, "content")]);
        var parsing = Substitute.For<IParsingClientService>();
        parsing.TryGetCached(Arg.Any<Uri>(), out Arg.Any<ModuleParseResult>())
            .Returns(call =>
            {
                var name = ((Uri)call[0]).AbsolutePath.EndsWith("Mod1.bas", StringComparison.OrdinalIgnoreCase) ? "Mod1" : "Mod2";
                call[1] = new ModuleParser().Parse(new Uri(Path.Combine(Root, "src", $"{name}.bas")), "Public Sub Foo()\r\nEnd Sub");
                return true;
            });
        var sut = new SymbolSyncService(orchestration, parsing, documents, new IntrinsicSymbolResolver(), Options(), NullLogger<SymbolSyncService>.Instance, new NoLibraries(), NoOtherProjects());

        await sut.SyncWorkspaceAsync(CancellationToken.None);

        Assert.AreEqual("the host refused it", sut.Unsynced.Single(module => module.Key == "Mod2").Value);
        Assert.IsFalse(sut.Unsynced.ContainsKey("Mod1"));

        // the next sync that loads it says so.
        host.SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(Arg.Any<DefineSymbolsParams>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DefineSymbolsResult { Defined = 1 }));
        await sut.SyncWorkspaceAsync(CancellationToken.None);

        Assert.IsEmpty(sut.Unsynced);
    }

    // a sync stopped before it reached the modules loaded none of them: what the last one did not load is not loaded still.
    [TestMethod]
    public async Task ASyncStoppedBeforeItReachedTheModules_LeavesWhatWasNotLoaded_NotLoaded()
    {
        var host = Substitute.For<IRDCoreClientApp>();
        host.WaitForReadyAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        host.PlatformInfo.Returns(new PlatformInitializeResult { Provided = [nameof(DefineSymbols)] });
        host.SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(Arg.Any<DefineSymbolsParams>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<DefineSymbolsParams>() is { ModuleName: "Mod2", CodeOnly: true }
                ? Task.FromException<DefineSymbolsResult>(new InvalidOperationException("the host refused it"))
                : Task.FromResult(new DefineSymbolsResult { Defined = 1 }));
        var orchestration = Substitute.For<IPlatformOrchestrationService>();
        orchestration.RuntimeEnvironment.Returns(host);
        var documents = Substitute.For<IWorkspaceDocumentService>();
        documents.GetAllDocuments().Returns([new WorkspaceDocument("src/Mod1.bas", Root, "content"), new WorkspaceDocument("src/Mod2.bas", Root, "content")]);
        var parsing = Substitute.For<IParsingClientService>();
        parsing.TryGetCached(Arg.Any<Uri>(), out Arg.Any<ModuleParseResult>())
            .Returns(call =>
            {
                var name = ((Uri)call[0]).AbsolutePath.EndsWith("Mod1.bas", StringComparison.OrdinalIgnoreCase) ? "Mod1" : "Mod2";
                call[1] = new ModuleParser().Parse(new Uri(Path.Combine(Root, "src", $"{name}.bas")), "Public Sub Foo()\r\nEnd Sub");
                return true;
            });
        var sut = new SymbolSyncService(orchestration, parsing, documents, new IntrinsicSymbolResolver(), Options(), NullLogger<SymbolSyncService>.Instance, new NoLibraries(), NoOtherProjects());
        await sut.SyncWorkspaceAsync(CancellationToken.None);

        using var stopped = new CancellationTokenSource();
        await stopped.CancelAsync();
        host.WaitForReadyAsync(stopped.Token).Returns(Task.FromCanceled(stopped.Token));
        await sut.SyncWorkspaceAsync(stopped.Token, replace: true);

        Assert.AreEqual("the host refused it", sut.Unsynced.Single().Value);
        Assert.AreEqual("Mod2", sut.Unsynced.Single().Key);
    }

    private (SymbolSyncService Sut, List<(string Module, bool CodeOnly, bool Replace)> Requests) TwoModuleWorkspace(bool changedParseIsCurrent)
    {
        var requests = new List<(string Module, bool CodeOnly, bool Replace)>();
        var host = Substitute.For<IRDCoreClientApp>();
        host.WaitForReadyAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        host.PlatformInfo.Returns(new PlatformInitializeResult { Provided = [nameof(DefineSymbols)] });
        host.SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(Arg.Any<DefineSymbolsParams>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<DefineSymbolsParams>();
                requests.Add((request.ModuleName, request.CodeOnly, request.Replace));
                return Task.FromResult(new DefineSymbolsResult { Defined = 1 });
            });

        var orchestration = Substitute.For<IPlatformOrchestrationService>();
        orchestration.RuntimeEnvironment.Returns(host);

        var mod1 = new WorkspaceDocument("src/Mod1.bas", Root, "Public Sub Foo()\r\nEnd Sub", version: 5);
        var mod2 = new WorkspaceDocument("src/Mod2.bas", Root, "Public Sub Bar()\r\nEnd Sub", version: 1);
        var documents = Substitute.For<IWorkspaceDocumentService>();
        documents.GetAllDocuments().Returns([mod1, mod2]);
        documents.TryGetDocument(mod1.Id.Uri.ToUri(), out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = mod1; return true; });

        var parse = (WorkspaceDocument document, string name) => new ModuleParser().Parse(new Uri(Path.Combine(Root, "src", $"{name}.bas")), document.Text);
        var parse1 = parse(mod1, "Mod1");
        var parse2 = parse(mod2, "Mod2");
        var parsing = Substitute.For<IParsingClientService>();
        parsing.TryGetCached(Arg.Any<Uri>(), out Arg.Any<ModuleParseResult>())
            .Returns(call =>
            {
                var path = ((Uri)call[0]).AbsolutePath;
                call[1] = path.EndsWith("Mod1.bas", StringComparison.OrdinalIgnoreCase) ? parse1 : parse2;
                return true;
            });
        parsing.TryGetCached(Arg.Any<Uri>(), Arg.Any<int>(), out Arg.Any<ModuleParseResult>())
            .Returns(call => { call[2] = parse1; return changedParseIsCurrent; });

        return (new SymbolSyncService(orchestration, parsing, documents, new IntrinsicSymbolResolver(), Options(), NullLogger<SymbolSyncService>.Instance, new NoLibraries(), NoOtherProjects()), requests);
    }

    [TestMethod]
    public async Task ADocumentThatChanged_IsDefinedAgainAndItsCodeLoadedAgain_AndNoOtherModuleIs()
    {
        var (sut, requests) = TwoModuleWorkspace(changedParseIsCurrent: true);

        await sut.SyncDocumentAsync(new Uri(Path.Combine(Root, "src", "Mod1.bas")), CancellationToken.None);

        CollectionAssert.AreEqual(new[] { ("Mod1", false, true), ("Mod1", true, false) }, requests);
    }

    // a workspace of Backup.bas, whose text is given, and Other.bas: what each is parsed to now is the text the test last gave it, and what the server has of
    // it at the moment (a document being parsed again has nothing). What the host is sent is recorded: each definition (C for a code load) and each discard.
    // The workspace allows blocks in a module file.
    private sealed class BlocksWorkspace
    {
        public readonly Dictionary<string, string?> Texts = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Backup"] = null,
            ["Other"] = "Public Sub Bar()\r\nEnd Sub",
        };
        public readonly List<string> Sent = [];
        public readonly SymbolSyncService Sut;
        public readonly Uri Backup = new(Path.Combine(Root, "src", "Backup.bas"));
        public readonly Uri Other = new(Path.Combine(Root, "src", "Other.bas"));

        public BlocksWorkspace(string backup)
        {
            Texts["Backup"] = backup;
            var host = Substitute.For<IRDCoreClientApp>();
            host.WaitForReadyAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            host.PlatformInfo.Returns(new PlatformInitializeResult { Provided = [nameof(DefineSymbols), nameof(SessionDiscard)] });
            host.SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(Arg.Any<DefineSymbolsParams>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var request = call.Arg<DefineSymbolsParams>();
                    Sent.Add(request.CodeOnly ? $"C {request.ModuleName}" : request.ModuleName);
                    return Task.FromResult(new DefineSymbolsResult { Defined = 1 });
                });
            host.SendRequestAsync<HostDiscardParams, DiscardResult>(Arg.Any<HostDiscardParams>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    Sent.Add($"discard {call.Arg<HostDiscardParams>().ModuleName}");
                    return Task.FromResult(new DiscardResult());
                });

            var orchestration = Substitute.For<IPlatformOrchestrationService>();
            orchestration.RuntimeEnvironment.Returns(host);

            var backupDocument = new WorkspaceDocument("src/Backup.bas", Root, backup, version: 2);
            var documents = Substitute.For<IWorkspaceDocumentService>();
            var otherDocument = new WorkspaceDocument("src/Other.bas", Root, "", version: 1);
            documents.GetAllDocuments().Returns([backupDocument, otherDocument]);
            documents.WorkspaceRoot.Returns(Root);
            documents.TryGetDocument(Backup, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = backupDocument; return true; });
            documents.TryGetDocument(Other, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = otherDocument; return true; });

            bool Parsed(Uri uri, out ModuleParseResult parse)
            {
                var name = Path.GetFileNameWithoutExtension(uri.LocalPath);
                parse = Texts[name] is { } text ? FileLevelStatementsOn.Parser().Parse(uri, text) : null!;
                return parse is not null;
            }

            var parsing = Substitute.For<IParsingClientService>();
            parsing.TryGetCached(Arg.Any<Uri>(), out Arg.Any<ModuleParseResult>())
                .Returns(call => { var found = Parsed((Uri)call[0], out var parse); call[1] = parse; return found; });
            parsing.TryGetCached(Arg.Any<Uri>(), Arg.Any<int>(), out Arg.Any<ModuleParseResult>())
                .Returns(call => { var found = Parsed((Uri)call[0], out var parse); call[2] = parse; return found; });

            Sut = new SymbolSyncService(
                orchestration, parsing, documents, new IntrinsicSymbolResolver(), Options(), NullLogger<SymbolSyncService>.Instance, new NoLibraries(),
                NoOtherProjects());
        }

        // what was sent since the last time it was asked.
        public string[] Taken()
        {
            var sent = Sent.ToArray();
            Sent.Clear();
            return sent;
        }
    }

    private static string BackupOf(params string[] blocks)
        => "Attribute VB_Name = \"Backup\"\r\nPublic Sub Run()\r\nEnd Sub\r\n"
            + string.Concat(blocks.Select(block => $"Module {block}\r\nPublic Sub Go{block}()\r\nEnd Sub\r\nEnd Module\r\n"));

    [TestMethod]
    public async Task EachModuleOfAFile_IsDefinedInTheHost_ItsOwnThenEachBlocks_ThenTheCodeOfEach()
    {
        var workspace = new BlocksWorkspace(BackupOf("Helpers", "Tools"));

        await workspace.Sut.SyncWorkspaceAsync(CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "Backup", "Helpers", "Tools", "Other", "C Backup", "C Helpers", "C Tools", "C Other" }, workspace.Taken());
    }

    // an edit that renames a block takes the old one out of the host and defines the new one, with the document's other modules; no other document's is sent.
    [TestMethod]
    public async Task ADocumentThatChanged_IsEachOfItsModulesDefinedAgain_AndABlockItNoLongerHasDiscarded()
    {
        var workspace = new BlocksWorkspace(BackupOf("Helpers", "Tools"));
        await workspace.Sut.SyncWorkspaceAsync(CancellationToken.None);
        workspace.Taken();

        workspace.Texts["Backup"] = BackupOf("Helpers", "Gadgets");
        await workspace.Sut.SyncDocumentAsync(workspace.Backup, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "discard Tools", "Backup", "Helpers", "Gadgets", "C Backup", "C Helpers", "C Gadgets" }, workspace.Taken());
    }

    // a block that moved to another document is still a module of the workspace: the host keeps it, and the other document's sync defines it again.
    [TestMethod]
    public async Task ABlockThatMovedToAnotherDocument_IsNotDiscarded()
    {
        var workspace = new BlocksWorkspace(BackupOf("Helpers", "Tools"));
        await workspace.Sut.SyncWorkspaceAsync(CancellationToken.None);
        workspace.Taken();

        workspace.Texts["Backup"] = BackupOf("Helpers");
        workspace.Texts["Other"] = "Public Sub Bar()\r\nEnd Sub\r\nModule Tools\r\nEnd Module\r\n";
        await workspace.Sut.SyncDocumentAsync(workspace.Backup, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "Backup", "Helpers", "C Backup", "C Helpers" }, workspace.Taken());
    }

    // a block that moved is the other document's from then on: that document losing it takes it out of the host.
    [TestMethod]
    public async Task ABlockThatMovedToAnotherDocument_IsDiscarded_OnceThatDocumentLosesIt()
    {
        var workspace = new BlocksWorkspace(BackupOf("Helpers", "Tools"));
        await workspace.Sut.SyncWorkspaceAsync(CancellationToken.None);
        workspace.Texts["Backup"] = BackupOf("Helpers");
        workspace.Texts["Other"] = "Public Sub Bar()\r\nEnd Sub\r\nModule Tools\r\nEnd Module\r\n";
        await workspace.Sut.SyncDocumentAsync(workspace.Backup, CancellationToken.None);
        workspace.Taken();

        workspace.Texts["Other"] = "Public Sub Bar()\r\nEnd Sub\r\n";
        await workspace.Sut.SyncDocumentAsync(workspace.Other, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "discard Tools", "Other", "C Other" }, workspace.Taken());
    }

    // what a document declares is said by its parse: one the server has none of at the moment says nothing, and the host keeps its modules. A file that
    // changed outside the editor and lost a block has it taken out by the workspace's sync.
    [TestMethod]
    [DataRow(true, DisplayName = "a document being parsed again")]
    [DataRow(false, DisplayName = "a file that lost a block")]
    public async Task TheWorkspacesSync_DiscardsOnlyWhatADocumentsParseNoLongerDeclares(bool beingParsed)
    {
        var workspace = new BlocksWorkspace(BackupOf("Helpers", "Tools"));
        await workspace.Sut.SyncWorkspaceAsync(CancellationToken.None);
        workspace.Taken();

        workspace.Texts["Backup"] = beingParsed ? null : BackupOf("Helpers");
        await workspace.Sut.SyncWorkspaceAsync(CancellationToken.None, replace: true);

        CollectionAssert.AreEqual(
            beingParsed ? new[] { "Other", "C Other" } : new[] { "discard Tools", "Backup", "Helpers", "Other", "C Backup", "C Helpers", "C Other" },
            workspace.Taken());
    }

    // a session composed anew holds none of what it was given: nothing of it is taken out.
    [TestMethod]
    public async Task ASessionComposedAnew_HasNothingDiscarded_ThatItWasNotGiven()
    {
        var workspace = new BlocksWorkspace(BackupOf("Helpers", "Tools"));
        await workspace.Sut.SyncWorkspaceAsync(CancellationToken.None);
        workspace.Taken();

        workspace.Sut.SessionComposedAnew();
        workspace.Texts["Backup"] = BackupOf("Helpers");
        await workspace.Sut.SyncWorkspaceAsync(CancellationToken.None, replace: true);

        CollectionAssert.AreEqual(new[] { "Backup", "Helpers", "Other", "C Backup", "C Helpers", "C Other" }, workspace.Taken());
    }

    private static (SymbolSyncService Sut, IRDCoreClientApp Host) ForDiscard(bool providesDiscard = true, bool hasHost = true)
    {
        var host = Substitute.For<IRDCoreClientApp>();
        host.WaitForReadyAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        host.PlatformInfo.Returns(new PlatformInitializeResult { Provided = providesDiscard ? [nameof(SessionDiscard)] : [] });
        host.SendRequestAsync<HostDiscardParams, DiscardResult>(Arg.Any<HostDiscardParams>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DiscardResult { Discarded = 3 }));

        var orchestration = Substitute.For<IPlatformOrchestrationService>();
        orchestration.RuntimeEnvironment.Returns(hasHost ? host : null);
        var documents = Substitute.For<IWorkspaceDocumentService>();
        documents.WorkspaceRoot.Returns(Root);

        return (new SymbolSyncService(
            orchestration, Substitute.For<IParsingClientService>(), documents, new IntrinsicSymbolResolver(), Options(), NullLogger<SymbolSyncService>.Instance, new NoLibraries(), NoOtherProjects()), host);
    }

    [TestMethod]
    public async Task AModuleThatIsDiscarded_IsAddressedTheWayItWasDefined_AndTheHostSaysWhatItTookOut()
    {
        var (sut, host) = ForDiscard();

        var result = await sut.DiscardModuleAsync("Program", CancellationToken.None);

        Assert.AreEqual(3, result.Discarded);
        await host.Received(1).SendRequestAsync<HostDiscardParams, DiscardResult>(
            Arg.Is<HostDiscardParams>(p => p.ModuleName == "Program" && p.ModuleUri == new UriBuilder(new Uri(Root)) { Fragment = "Program" }.Uri),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AHostThatCannotDiscard_IsNotAsked_AndNothingWasTakenOut()
    {
        var (sut, host) = ForDiscard(providesDiscard: false);

        var result = await sut.DiscardModuleAsync("Program", CancellationToken.None);

        Assert.AreEqual(0, result.Discarded);
        await host.DidNotReceiveWithAnyArgs().SendRequestAsync<HostDiscardParams, DiscardResult>(default!, default);
    }

    [TestMethod]
    public async Task APlatformWithNoHost_TakesNothingOut()
    {
        var (sut, _) = ForDiscard(hasHost: false);

        Assert.AreEqual(0, (await sut.DiscardModuleAsync("Program", CancellationToken.None)).Discarded);
    }

    [TestMethod]
    public async Task ADocumentWhoseParseIsNotOfItsText_IsNotSentToTheHost()
    {
        var (sut, requests) = TwoModuleWorkspace(changedParseIsCurrent: false);

        await sut.SyncDocumentAsync(new Uri(Path.Combine(Root, "src", "Mod1.bas")), CancellationToken.None);

        Assert.IsEmpty(requests);
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task WhatIsDoneUnderAHold_IsNotInterleavedWithTheWorkspaceSync()
    {
        // the workspace sync defines every module, then loads the code of each: the host takes its time over the code of the scaffold. What is recorded is
        // what the host has done, in the order it is done.
        var done = new List<string>();
        var loading = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var host = Substitute.For<IRDCoreClientApp>();
        host.WaitForReadyAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        host.PlatformInfo.Returns(new PlatformInitializeResult { Provided = [nameof(DefineSymbols)] });
        host.SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(Arg.Any<DefineSymbolsParams>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<DefineSymbolsParams>();
                var what = $"{(request.Replace || request.ParseResultJson.Contains("Analyzed") ? "fragment" : "workspace")} {(request.CodeOnly ? "code" : "symbols")}";
                if (what == "workspace code")
                {
                    loading.TrySetResult();
                    return release.Task.ContinueWith(_ => Done(what), TaskScheduler.Default);
                }

                return Task.FromResult(Done(what));
            });

        DefineSymbolsResult Done(string what)
        {
            lock (done)
            {
                done.Add(what);
            }

            return new DefineSymbolsResult { Defined = 1 };
        }

        var orchestration = Substitute.For<IPlatformOrchestrationService>();
        orchestration.RuntimeEnvironment.Returns(host);
        var documents = Substitute.For<IWorkspaceDocumentService>();
        documents.GetAllDocuments().Returns([new WorkspaceDocument("src/Program.bas", Root, "Public Sub Main()\r\nEnd Sub")]);
        documents.WorkspaceRoot.Returns(Root);
        var scaffold = new ModuleParser().Parse(new Uri(Path.Combine(Root, "src", "Program.bas")), "Public Sub Main()\r\nEnd Sub");
        var parsing = Substitute.For<IParsingClientService>();
        parsing.TryGetCached(Arg.Any<Uri>(), out Arg.Any<ModuleParseResult>()).Returns(call => { call[1] = scaffold; return true; });
        var sut = new SymbolSyncService(orchestration, parsing, documents, new IntrinsicSymbolResolver(), Options(), NullLogger<SymbolSyncService>.Instance, new NoLibraries(), NoOtherProjects());

        var workspace = sut.SyncWorkspaceAsync(CancellationToken.None);
        await loading.Task.WaitAsync(Patience);

        // a client's own text of the same module, defined and loaded to be asked about, while the workspace sync is not done. The hold is asked for on the
        // test's own thread: whether it is given at once is decided before HoldAsync returns, by whether the workspace sync has it.
        var holding = sut.HoldAsync(CancellationToken.None);
        Assert.IsFalse(holding.IsCompleted, "the workspace sync has the hold while the host loads the scaffold's code");

        release.SetResult();
        var analyzed = new ModuleParser().Parse(new Uri(Path.Combine(Root, "src", "Program.bas")), "Public Sub Main()\r\n    Dim Analyzed As Long\r\nEnd Sub");
        await using (await holding.WaitAsync(Patience))
        {
            await sut.SyncModuleAsync("Program", analyzed, CancellationToken.None);
            await sut.LoadModuleCodeAsync("Program", analyzed, CancellationToken.None);
        }

        await workspace.WaitAsync(Patience);

        CollectionAssert.AreEqual(
            new[] { "workspace symbols", "workspace code", "fragment symbols", "fragment code" },
            done,
            string.Join(" | ", done) + ": the scaffold's code is not loaded over the client's text while the client is between defining it and asking about it");
    }

    // ---- what a load failed with is said once ----

    private sealed class RecordingLogger : Microsoft.Extensions.Logging.ILogger<SymbolSyncService>
    {
        public List<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private static int NotLoaded(RecordingLogger logger)
        => logger.Entries.Count(entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning && entry.Message.Contains("was not loaded"));

    [TestMethod]
    public async Task AModuleTheHostRefusesTheSameWay_EachTimeItIsSent_IsLoggedOnce()
    {
        // an editor sends the module it edits at every keystroke, and the host refuses it the same way each time.
        var logger = new RecordingLogger();
        var (sut, _) = Build(Parse("Public Sub Foo()\r\nEnd Sub"), response: new DefineSymbolsResult { CodeErrors = ["Invalid use of Me keyword"] }, logger: logger);
        await sut.SyncWorkspaceAsync(CancellationToken.None);

        for (var edit = 0; edit < 3; edit++)
        {
            await sut.SyncDocumentAsync(new Uri(Path.Combine(Root, "src", "Mod1.bas")), CancellationToken.None);
        }

        Assert.AreEqual(1, NotLoaded(logger), string.Join("\n", logger.Entries.Select(entry => entry.Message)));
    }

    [TestMethod]
    public async Task AWorkspaceComposedAnew_SaysAgainWhatAModuleFailsWith()
    {
        // the host's session is another - the workspace was reloaded into it - and a failure it had been told of is news to whoever reads on.
        var logger = new RecordingLogger();
        var (sut, _) = Build(Parse("Public Sub Foo()\r\nEnd Sub"), response: new DefineSymbolsResult { CodeErrors = ["Invalid use of Me keyword"] }, logger: logger);

        await sut.SyncWorkspaceAsync(CancellationToken.None);
        sut.SessionComposedAnew();
        await sut.SyncWorkspaceAsync(CancellationToken.None, replace: true);

        Assert.AreEqual(2, NotLoaded(logger), string.Join("\n", logger.Entries.Select(entry => entry.Message)));
    }

    [TestMethod]
    public async Task AWorkspaceDefinedAgainForAnEdit_SaysNothingNewOfWhatAModuleFailsWith()
    {
        // an edit that changes what a module declares has the whole workspace defined again in the same session, at each refresh while the user types.
        var logger = new RecordingLogger();
        var (sut, _) = Build(Parse("Public Sub Foo()\r\nEnd Sub"), response: new DefineSymbolsResult { CodeErrors = ["Invalid use of Me keyword"] }, logger: logger);
        await sut.SyncWorkspaceAsync(CancellationToken.None);

        for (var edit = 0; edit < 3; edit++)
        {
            await sut.SyncWorkspaceAsync(CancellationToken.None, replace: true);
        }

        Assert.AreEqual(1, NotLoaded(logger), string.Join("\n", logger.Entries.Select(entry => entry.Message)));
    }

    [TestMethod]
    public async Task AShellsProgram_AndAModuleOfTheWorkspaceOfTheSameName_AreEachKnownOnTheirOwn()
    {
        var logger = new RecordingLogger();
        var (sut, _) = Build(Parse("Public Sub Foo()\r\nEnd Sub"), response: new DefineSymbolsResult { CodeErrors = ["Invalid use of Me keyword"] }, logger: logger);
        await sut.SyncWorkspaceAsync(CancellationToken.None);

        await sut.LoadModuleCodeAsync("Mod1", Parse("Public Sub Foo()\r\nEnd Sub"), CancellationToken.None);

        Assert.AreEqual(2, NotLoaded(logger), "the shell's program is not the workspace's module of its name: " + string.Join("\n", logger.Entries.Select(entry => entry.Message)));
    }

    [TestMethod]
    public async Task ADocumentModule_IsSentAsAClassModule_ThatNoneCreates_AndThatHasNoDefaultInstance()
    {
        // what legacy Rubberduck exported of a worksheet: the code pane's text, no header and no attribute. Me is valid in it, for it is a class module.
        var (sut, host) = Build(Parse("Option Explicit\r\nPublic Function Hello() As String\r\n    Hello = TypeName(Me)\r\nEnd Function"), super: DocClassType.ExcelWorksheet);

        await sut.SyncWorkspaceAsync(CancellationToken.None);

        await host.Received().SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(
            Arg.Is<DefineSymbolsParams>(p => p.ModuleName == "Mod1" && p.CodeOnly
                && p.Directives.Explicit && !p.Directives.Creatable && p.Directives.Extensible && !p.Directives.PredeclaredId),
            Arg.Any<CancellationToken>());
        await host.Received().SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(
            Arg.Is<DefineSymbolsParams>(p => p.ModuleName == "Mod1" && !p.CodeOnly && p.Symbols.Any(symbol => symbol.Name == "Hello"
                && symbol.Scope == RDCore.SDK.Model.Symbols.Abstract.ScopeKind.Instance)),
            Arg.Any<CancellationToken>());
    }

    // ---- a shell's program is a module of the project ----

    [TestMethod]
    [DataRow(ImplicitDeclarationScope.Procedure)]
    [DataRow(ImplicitDeclarationScope.Module)]
    public async Task AShellsProgram_CallsASubOfAModuleOfTheWorkspace_QualifiedOrNot_AndDeclaresNoVariableByNamingIt(ImplicitDeclarationScope implicitScope)
    {
        var sent = new List<DefineSymbolsParams>();
        var host = Substitute.For<IRDCoreClientApp>();
        host.WaitForReadyAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        host.PlatformInfo.Returns(new PlatformInitializeResult { Provided = [nameof(DefineSymbols)] });
        host.SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(Arg.Do<DefineSymbolsParams>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DefineSymbolsResult { Defined = 1 }));
        var orchestration = Substitute.For<IPlatformOrchestrationService>();
        orchestration.RuntimeEnvironment.Returns(host);

        // the workspace has a module the program calls into, which the language server has parsed.
        var module = "Attribute VB_Name = \"Module1\"\r\nOption Explicit\r\nPublic Sub Helper()\r\nEnd Sub\r\n";
        var documents = Substitute.For<IWorkspaceDocumentService>();
        documents.GetAllDocuments().Returns([new WorkspaceDocument("Module1.bas", Root, module)]);
        documents.WorkspaceRoot.Returns(Root);
        var parsed = new ModuleParser().Parse(new Uri(Path.Combine(Root, "Module1.bas")), module);
        var parsing = Substitute.For<IParsingClientService>();
        parsing.TryGetCached(Arg.Any<Uri>(), out Arg.Any<ModuleParseResult>()).Returns(call => { call[1] = parsed; return true; });
        var sut = new SymbolSyncService(orchestration, parsing, documents, new IntrinsicSymbolResolver(), Options(implicitScope),
            NullLogger<SymbolSyncService>.Instance, new NoLibraries(), NoOtherProjects());

        var program = new ModuleParser().Parse(new Uri(Path.Combine(Root, "Program.bas")), "Public Sub Main()\r\n    Helper\r\n    Module1.Helper\r\nEnd Sub\r\n");
        await sut.SyncModuleAsync("Program", program, CancellationToken.None);

        var defined = sent.Single(request => request.ModuleName == "Program");
        var main = defined.Symbols.Single(symbol => symbol.Name == "Main");
        Assert.IsFalse(defined.Symbols.Any(symbol => symbol.Name is "Helper" or "Module1"),
            "a name of the workspace is no variable of the program: " + string.Join(", ", defined.Symbols.Select(symbol => symbol.Name)));
        Assert.IsTrue(main.Locals.IsDefaultOrEmpty, "nor a local of Main: " + string.Join(", ", main.Locals.Select(local => local.Name)));
    }

    // ---- what the client supplies waits for the workspace's modules ----

    // a cap on a hang, not a budget: each wait is on the event itself, which a loaded machine brings as late as it does.
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

    // a workspace with Module1, parsed, and a host that records what it is sent, in order.
    private static (SymbolSyncService Sut, List<DefineSymbolsParams> Sent) WorkspaceWithModule1()
    {
        var sent = new List<DefineSymbolsParams>();
        var host = Substitute.For<IRDCoreClientApp>();
        host.WaitForReadyAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        host.PlatformInfo.Returns(new PlatformInitializeResult { Provided = [nameof(DefineSymbols)] });
        host.SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(Arg.Any<DefineSymbolsParams>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            lock (sent)
            {
                sent.Add(call.Arg<DefineSymbolsParams>());
            }

            return Task.FromResult(new DefineSymbolsResult { Defined = 1 });
        });
        var orchestration = Substitute.For<IPlatformOrchestrationService>();
        orchestration.RuntimeEnvironment.Returns(host);

        var module = "Attribute VB_Name = \"Module1\"\r\nOption Explicit\r\nPublic Sub Helper()\r\nEnd Sub\r\n";
        var documents = Substitute.For<IWorkspaceDocumentService>();
        documents.GetAllDocuments().Returns([new WorkspaceDocument("Module1.bas", Root, module)]);
        documents.WorkspaceRoot.Returns(Root);
        var parsed = new ModuleParser().Parse(new Uri(Path.Combine(Root, "Module1.bas")), module);
        var parsing = Substitute.For<IParsingClientService>();
        parsing.TryGetCached(Arg.Any<Uri>(), out Arg.Any<ModuleParseResult>()).Returns(call => { call[1] = parsed; return true; });
        var sut = new SymbolSyncService(orchestration, parsing, documents, new IntrinsicSymbolResolver(), Options(ImplicitDeclarationScope.Module),
            NullLogger<SymbolSyncService>.Instance, new NoLibraries(), NoOtherProjects());
        return (sut, sent);
    }

    // a shell that runs its program while the workspace is brought up has it given the host once the workspace's modules are: a name of one of them is bound to
    // it there, and is no variable of the program's.
    [TestMethod]
    public async Task AProgramRunWhileTheWorkspaceIsBroughtUp_IsGivenTheHost_OnceTheWorkspacesModulesAre()
    {
        var (sut, sent) = WorkspaceWithModule1();
        var program = new ModuleParser().Parse(new Uri(Path.Combine(Root, "Program.bas")), "Public Sub Main()\r\n    Module1.Helper\r\nEnd Sub\r\n");
        var expected = sut.ExpectWorkspace();

        var running = sut.SyncModuleAsync("Program", program, CancellationToken.None);
        var heldBack = !running.IsCompleted;
        bool sentBefore;
        lock (sent)
        {
            sentBefore = sent.Count > 0;
        }

        // the workspace's own sync does not wait for itself.
        await sut.SyncWorkspaceAsync(CancellationToken.None).WaitAsync(Patience);
        expected.Dispose();
        await running.WaitAsync(Patience);

        Assert.IsTrue(heldBack, "the program waits for the workspace");
        Assert.IsFalse(sentBefore, "nothing of it is sent before");
        lock (sent)
        {
            var modules = sent.Select(request => request.ModuleName).ToList();
            Assert.IsLessThan(modules.IndexOf("Program"), modules.LastIndexOf("Module1"), "Module1 is defined, and loaded, before the program");
        }
    }

    // a hold - what a client's own module is analyzed under - is taken once the workspace's modules are defined; and nothing waits once the bring-up is over.
    [TestMethod]
    public async Task AHold_IsTakenOnceTheWorkspaceIsBroughtUp()
    {
        var (sut, _) = WorkspaceWithModule1();
        var expected = sut.ExpectWorkspace();

        var holding = sut.HoldAsync(CancellationToken.None);
        var heldBack = !holding.IsCompleted;
        expected.Dispose();
        await (await holding.WaitAsync(Patience)).DisposeAsync();
        var again = sut.HoldAsync(CancellationToken.None);
        var atOnce = again.IsCompletedSuccessfully;
        await (await again).DisposeAsync();

        Assert.IsTrue(heldBack, "the hold waits for the workspace");
        Assert.IsTrue(atOnce, "and nothing waits once it is brought up");
    }

    // a workspace of standard modules, each parsed as itself, in a workspace that allows blocks in a module file; and the names of the modules the host is
    // sent.
    private static (SymbolSyncService Sut, List<string> Sent) WorkspaceOf(params (string File, string Source)[] modules) => WorkspaceOf(null, modules);

    // requests, when given, gets every DefineSymbols request the host is sent.
    private static (SymbolSyncService Sut, List<string> Sent) WorkspaceOf(List<DefineSymbolsParams>? requests, params (string File, string Source)[] modules)
    {
        var sent = new List<string>();
        var host = Substitute.For<IRDCoreClientApp>();
        host.WaitForReadyAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        host.PlatformInfo.Returns(new PlatformInitializeResult { Provided = [nameof(DefineSymbols)] });
        host.SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(Arg.Any<DefineSymbolsParams>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                sent.Add(call.Arg<DefineSymbolsParams>().ModuleName);
                requests?.Add(call.Arg<DefineSymbolsParams>());
                return Task.FromResult(new DefineSymbolsResult { Defined = 1 });
            });
        var orchestration = Substitute.For<IPlatformOrchestrationService>();
        orchestration.RuntimeEnvironment.Returns(host);

        WorkspaceDocument[] files = [.. modules.Select(module => new WorkspaceDocument($"src/{module.File}.bas", Root, module.Source))];
        var documents = Substitute.For<IWorkspaceDocumentService>();
        documents.GetAllDocuments().Returns(files);
        documents.WorkspaceRoot.Returns(Root);
        var parses = files.ToDictionary(file => file.Id.Uri.ToUri().AbsoluteUri, file => FileLevelStatementsOn.Parser().Parse(file.Id.Uri.ToUri(), file.Text));
        var parsing = Substitute.For<IParsingClientService>();
        parsing.TryGetCached(Arg.Any<Uri>(), out Arg.Any<ModuleParseResult>())
            .Returns(call =>
            {
                var found = parses.TryGetValue(((Uri)call[0]).AbsoluteUri, out var parse);
                call[1] = parse;
                return found;
            });

        return (new SymbolSyncService(orchestration, parsing, documents, new IntrinsicSymbolResolver(), Options(), NullLogger<SymbolSyncService>.Instance,
            new NoLibraries(), NoOtherProjects()), sent);
    }

    private static string Named(string name) => $"Attribute VB_Name = \"{name}\"\r\nPublic Sub Work()\r\nEnd Sub\r\n";

    // MS-VBAL §4.2: the modules of a project have distinct names. Of two of one name, neither is the module of that name, and neither is the host's: which one
    // would be is the order the project lists them in. A run is refused, and says why. A module of another name is the host's as any is.
    [TestMethod]
    [DataRow("A", "Dup", "B", "Dup", DisplayName = "two files, one VB_Name")]
    [DataRow("Helpers", null, "B", "Helpers", DisplayName = "a file named as another module's VB_Name")]
    [DataRow("A", "dup", "B", "DUP", DisplayName = "two names that differ in case")]
    public async Task TwoModulesOfOneName_AreNeitherSentToTheHost_AndARunOfTheProgramIsRefused(
        string firstFile, string? firstName, string secondFile, string secondName)
    {
        var (sut, sent) = WorkspaceOf(
            (firstFile, firstName is null ? "Public Sub Work()\r\nEnd Sub\r\n" : Named(firstName)), (secondFile, Named(secondName)), ("C", Named("Other")));

        await sut.SyncWorkspaceAsync(CancellationToken.None);

        Assert.Contains("Other", sent);
        Assert.IsFalse(sent.Any(name => string.Equals(name, secondName, StringComparison.OrdinalIgnoreCase)), string.Join(", ", sent));
        var refused = Assert.ContainsSingle(sut.Unsynced);
        Assert.AreEqual(secondName, refused.Key, ignoreCase: true);
        Assert.StartsWith(VBCompileErrorInfo.GetErrorString(VBCompileErrorId.NameConflictsWithModuleProjectOrLibrary), refused.Value);
    }

    // RD-VBAL §5.1: a Partial block of a module's name is a part of that module: the host is sent one module with every part's declarations, and a run is
    // not refused.
    [TestMethod]
    public async Task APartialBlockOfAModulesName_IsAPartOfIt_AndTheHostIsSentOneModuleWithBothParts()
    {
        var requests = new List<DefineSymbolsParams>();
        var (sut, _) = WorkspaceOf(requests,
            ("A", Named("Helpers")), ("B", "Attribute VB_Name = \"B\"\r\nPartial Module Helpers\r\nPublic Sub More()\r\nEnd Sub\r\nEnd Module\r\n"));

        await sut.SyncWorkspaceAsync(CancellationToken.None);

        Assert.IsEmpty(sut.Unsynced);
        var helpers = requests.Where(request => request.ModuleName == "Helpers").ToArray();
        Assert.IsNotEmpty(helpers);
        Assert.IsTrue(helpers.Any(request => request.Symbols.Any(symbol => symbol.Name == "Work") && request.Symbols.Any(symbol => symbol.Name == "More")),
            string.Join(" | ", helpers.Select(request => string.Join(", ", request.Symbols.Select(symbol => symbol.Name)))));
    }

    // a module its parts make invalid is none of the host's either, and a run says why: a Partial Class block of a standard module's name (VBC09388).
    [TestMethod]
    public async Task APartOfAnotherKind_RefusesTheModule_AndARunOfTheProgramSaysWhy()
    {
        var (sut, sent) = WorkspaceOf(
            ("A", Named("Helpers")), ("B", "Attribute VB_Name = \"B\"\r\nPartial Class Helpers\r\nPublic Sub More()\r\nEnd Sub\r\nEnd Class\r\n"));

        await sut.SyncWorkspaceAsync(CancellationToken.None);

        Assert.DoesNotContain("Helpers", sent);
        var refused = Assert.ContainsSingle(sut.Unsynced);
        Assert.AreEqual("Helpers", refused.Key);
        Assert.StartsWith(VBCompileErrorInfo.GetErrorString(VBCompileErrorId.ModulePartOfAnotherKind), refused.Value);
    }

    // a block's module is a module of the project as a file's is: of two blocks of one name, neither is the host's, and a run is refused, though no file's
    // own module has the name. The file that has the blocks is the host's.
    [TestMethod]
    public async Task TwoBlocksOfOneName_AreNeitherSentToTheHost_AndARunOfTheProgramIsRefused()
    {
        var (sut, sent) = WorkspaceOf(("Backup", Named("Backup") + "Module Dup\r\nEnd Module\r\nClass Dup\r\nEnd Class\r\n"), ("B", Named("Other")));

        await sut.SyncWorkspaceAsync(CancellationToken.None);

        CollectionAssert.IsSubsetOf(new[] { "Backup", "Other" }, sent);
        Assert.DoesNotContain("Dup", sent);
        var refused = Assert.ContainsSingle(sut.Unsynced);
        Assert.AreEqual("Dup", refused.Key);
        Assert.StartsWith(VBCompileErrorInfo.GetErrorString(VBCompileErrorId.NameConflictsWithModuleProjectOrLibrary), refused.Value);
    }
}
