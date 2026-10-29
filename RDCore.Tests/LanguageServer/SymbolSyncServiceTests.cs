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
using RDCore.Symbols;

namespace RDCore.Tests.LanguageServer;

[TestClass]
public sealed class SymbolSyncServiceTests
{
    // an absolute path the URI ctor accepts on both Windows and the Linux CI runner.
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-sync-ws");

    private static ModuleParseResult Parse(string source)
        => new ModuleParser().Parse(new Uri(Path.Combine(Root, "src", "Mod1.bas")), source);

    // a project that references no library but the standard library.
    private sealed class NoLibraries : IReferencedLibraryService
    {
        public RDCore.SDK.Runtime.Libraries.ReferencedLibraries Libraries => RDCore.SDK.Runtime.Libraries.ReferencedLibraries.None;

        public IReadOnlyList<RDCore.SDK.Model.Symbols.Abstract.Symbol> SymbolsUnder(Uri workspaceRoot) => [];

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
            logger ?? NullLogger<SymbolSyncService>.Instance, new NoLibraries());
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

        var sut = new SymbolSyncService(orchestration, parsing, documents, new IntrinsicSymbolResolver(), Options(), NullLogger<SymbolSyncService>.Instance, new NoLibraries());

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

        var sut = new SymbolSyncService(orchestration, parsing, documents, new IntrinsicSymbolResolver(), Options(), NullLogger<SymbolSyncService>.Instance, new NoLibraries());

        await sut.SyncWorkspaceAsync(CancellationToken.None);

        CollectionAssert.AreEqual(new[] { ("Mod1", false), ("Mod2", false), ("Mod1", true), ("Mod2", true) }, requests);
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

        return (new SymbolSyncService(orchestration, parsing, documents, new IntrinsicSymbolResolver(), Options(), NullLogger<SymbolSyncService>.Instance, new NoLibraries()), requests);
    }

    [TestMethod]
    public async Task ADocumentThatChanged_IsDefinedAgainAndItsCodeLoadedAgain_AndNoOtherModuleIs()
    {
        var (sut, requests) = TwoModuleWorkspace(changedParseIsCurrent: true);

        await sut.SyncDocumentAsync(new Uri(Path.Combine(Root, "src", "Mod1.bas")), CancellationToken.None);

        CollectionAssert.AreEqual(new[] { ("Mod1", false, true), ("Mod1", true, false) }, requests);
    }

    private static (SymbolSyncService Sut, IRDCoreClientApp Host) ForDiscard(bool providesDiscard = true, bool hasHost = true)
    {
        var host = Substitute.For<IRDCoreClientApp>();
        host.WaitForReadyAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        host.PlatformInfo.Returns(new PlatformInitializeResult { Provided = providesDiscard ? [nameof(SessionDiscard)] : [] });
        host.SendRequestAsync<HostDiscardParams, DiscardSessionResult>(Arg.Any<HostDiscardParams>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DiscardSessionResult { Discarded = 3 }));

        var orchestration = Substitute.For<IPlatformOrchestrationService>();
        orchestration.RuntimeEnvironment.Returns(hasHost ? host : null);
        var documents = Substitute.For<IWorkspaceDocumentService>();
        documents.WorkspaceRoot.Returns(Root);

        return (new SymbolSyncService(
            orchestration, Substitute.For<IParsingClientService>(), documents, new IntrinsicSymbolResolver(), Options(), NullLogger<SymbolSyncService>.Instance, new NoLibraries()), host);
    }

    [TestMethod]
    public async Task AModuleThatIsDiscarded_IsAddressedTheWayItWasDefined_AndTheHostSaysWhatItTookOut()
    {
        var (sut, host) = ForDiscard();

        var result = await sut.DiscardModuleAsync("Program", CancellationToken.None);

        Assert.AreEqual(3, result.Discarded);
        await host.Received(1).SendRequestAsync<HostDiscardParams, DiscardSessionResult>(
            Arg.Is<HostDiscardParams>(p => p.ModuleName == "Program" && p.ModuleUri == new UriBuilder(new Uri(Root)) { Fragment = "Program" }.Uri),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AHostThatCannotDiscard_IsNotAsked_AndNothingWasTakenOut()
    {
        var (sut, host) = ForDiscard(providesDiscard: false);

        var result = await sut.DiscardModuleAsync("Program", CancellationToken.None);

        Assert.AreEqual(0, result.Discarded);
        await host.DidNotReceiveWithAnyArgs().SendRequestAsync<HostDiscardParams, DiscardSessionResult>(default!, default);
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
    [Timeout(10_000, CooperativeCancellation = true)]
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
        var sut = new SymbolSyncService(orchestration, parsing, documents, new IntrinsicSymbolResolver(), Options(), NullLogger<SymbolSyncService>.Instance, new NoLibraries());

        var workspace = sut.SyncWorkspaceAsync(CancellationToken.None);
        await loading.Task;

        // a client's own text of the same module, defined and loaded to be asked about, while the workspace sync is not done.
        var fragment = Task.Run(async () =>
        {
            var analyzed = new ModuleParser().Parse(new Uri(Path.Combine(Root, "src", "Program.bas")), "Public Sub Main()\r\n    Dim Analyzed As Long\r\nEnd Sub");
            await using (await sut.HoldAsync(CancellationToken.None))
            {
                await sut.SyncModuleAsync("Program", analyzed, CancellationToken.None);
                await sut.LoadModuleCodeAsync("Program", analyzed, CancellationToken.None);
            }
        });

        // time enough for the fragment to be done, if nothing holds it back.
        await Task.WhenAny(fragment, Task.Delay(300));
        release.SetResult();
        await Task.WhenAll(workspace, fragment);

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
        // the host's session is another - the workspace was reloaded, or the host started again - and a failure it had been told of is news to whoever reads on.
        var logger = new RecordingLogger();
        var (sut, _) = Build(Parse("Public Sub Foo()\r\nEnd Sub"), response: new DefineSymbolsResult { CodeErrors = ["Invalid use of Me keyword"] }, logger: logger);

        await sut.SyncWorkspaceAsync(CancellationToken.None);
        await sut.SyncWorkspaceAsync(CancellationToken.None, replace: true);

        Assert.AreEqual(2, NotLoaded(logger), string.Join("\n", logger.Entries.Select(entry => entry.Message)));
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
}
