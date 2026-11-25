using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.Runtime.Support.Host.Handlers;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Support.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Workspace;
using RDCore.SDK.Platform.Components;

namespace RDCore.Tests.Cli;

[TestClass]
public sealed class DefineSymbolsHandlerTests
{
    private static readonly Uri WorkspaceRoot = new("file:///c:/ws/");

    private static DefineSymbolsHandler NewHandler(bool compose = true, Microsoft.Extensions.Logging.ILogger<DefineSymbolsHandler>? logger = null)
    {
        var provider = new EnvironmentSessionProvider(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false),
            new MockFileSystem(),
            NullLogger<EnvironmentSessionProvider>.Instance);

        if (compose)
        {
            provider.Compose(new RDCoreProject { Modules = [new RDCoreModule { RelativeUri = "src/Mod1.bas" }] }, WorkspaceRoot);
        }

        return new DefineSymbolsHandler(provider, NSubstitute.Substitute.For<RDCore.Runtime.Support.Host.IProjectAnalysisSessions>(), Substitute.For<IErrorDetailsRecorder>(), logger ?? NullLogger<DefineSymbolsHandler>.Instance);
    }

    private static DefineSymbolsParams Request(params SymbolDescriptor[] symbols) => new()
    {
        WorkspaceRoot = WorkspaceRoot,
        ModuleName = "Mod1",
        Symbols = [.. symbols],
    };

    private static SymbolDescriptor Procedure(string name) =>
        new() { Name = name, Kind = SymbolDescriptorKind.Procedure, Scope = ScopeKind.Module, AccessModifier = AccessModifier.Public };

    private static SymbolDescriptor Field(string name, string type) =>
        new() { Name = name, Kind = SymbolDescriptorKind.ModuleField, Scope = ScopeKind.Module, DeclaredTypeName = type };

    [TestMethod]
    public async Task DefinesSymbols_AndReportsUnresolvedTypeNames()
    {
        var handler = NewHandler();

        var result = await handler.Handle(
            Request(Procedure("DoWork"), Field("Total", "Long"), Field("Widget", "CWidget")),
            CancellationToken.None);

        Assert.AreEqual(3, result.Defined);
        Assert.AreEqual(0, result.Skipped.Count);
        CollectionAssert.AreEqual(new[] { "CWidget" }, result.UnresolvedTypeNames.ToArray());

        // the symbols really landed in the table: re-sending the same request now skips them all.
        var again = await handler.Handle(Request(Procedure("DoWork"), Field("Total", "Long")), CancellationToken.None);
        Assert.AreEqual(0, again.Defined);
        Assert.AreEqual(2, again.Skipped.Count);
    }

    [TestMethod]
    public async Task DuplicateSymbol_IsSkipped()
    {
        var handler = NewHandler();
        var request = Request(Procedure("DoWork"));

        await handler.Handle(request, CancellationToken.None);
        var second = await handler.Handle(request, CancellationToken.None);

        Assert.AreEqual(0, second.Defined);
        CollectionAssert.AreEqual(new[] { "DoWork" }, second.Skipped.ToArray());
    }

    [TestMethod]
    public async Task DuplicateDescriptors_MergeIntoOneSymbol_AndAreCounted()
    {
        var handler = NewHandler();

        // two descriptors for the same identity (a name declared in both #If branches) reach the
        // session as a single define; the collapsed one is reported in MergedDefinitions.
        var result = await handler.Handle(
            Request(Field("Flags", "Long"), Field("Flags", "Double")),
            CancellationToken.None);

        Assert.AreEqual(1, result.Defined);
        Assert.AreEqual(1, result.MergedDefinitions);
        Assert.AreEqual(0, result.Skipped.Count);
    }

    // the client's own module (DefineSymbolsParams.SessionModule) is a standard module of the session when the project does not list it; a module the project
    // does not list that is not the client's own is no module of the session, and its run is not found.
    [TestMethod]
    [DataRow(true, true, DisplayName = "the client's own module")]
    [DataRow(false, false, DisplayName = "a module the client does not say is its own")]
    public async Task AModuleTheProjectDoesNotList_IsAModuleOfTheSession_OnlyWhenItIsTheClientsOwn(bool sessionModule, bool expected)
    {
        var provider = new EnvironmentSessionProvider(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false),
            new MockFileSystem(),
            NullLogger<EnvironmentSessionProvider>.Instance);
        provider.Compose(new RDCoreProject { Modules = [new RDCoreModule { RelativeUri = "src/Mod1.bas" }] }, WorkspaceRoot);
        var handler = new DefineSymbolsHandler(provider, NSubstitute.Substitute.For<RDCore.Runtime.Support.Host.IProjectAnalysisSessions>(), Substitute.For<IErrorDetailsRecorder>(), NullLogger<DefineSymbolsHandler>.Instance);

        await handler.Handle(Request(Procedure("Main")) with { ModuleName = "Program", SessionModule = sessionModule }, CancellationToken.None);

        Assert.AreEqual(expected, provider.Session.Symbols.TryGetModule("Program", out var module));
        if (expected)
        {
            Assert.IsInstanceOfType<RDCore.SDK.Model.Symbols.VBStandardModuleSymbol>(module);
            Assert.IsTrue(provider.Session.Symbols.DeclaredIn(module!.Uri).Any(symbol => symbol.Name == "Main"), "Main is a procedure of the module that was defined");
            Assert.IsTrue(provider.Session.Symbols.TryGetModule("Mod1", out _), "the project's own module is still the session's");
        }
    }

    // a module the project does list is the project's, the client's own or not: nothing is defined beside it, and its members are its own.
    [TestMethod]
    [DataRow("src/Program.bas", DisplayName = "a standard module of the project")]
    [DataRow("src/Program.cls", DisplayName = "a class module of the project")]
    public async Task AModuleTheProjectLists_IsTheProjects_WhenTheClientSaysItIsItsOwn(string relativeUri)
    {
        var provider = new EnvironmentSessionProvider(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false),
            new MockFileSystem(),
            NullLogger<EnvironmentSessionProvider>.Instance);
        provider.Compose(new RDCoreProject { Modules = [new RDCoreModule { RelativeUri = relativeUri }] }, WorkspaceRoot);
        var handler = new DefineSymbolsHandler(provider, NSubstitute.Substitute.For<RDCore.Runtime.Support.Host.IProjectAnalysisSessions>(), Substitute.For<IErrorDetailsRecorder>(), NullLogger<DefineSymbolsHandler>.Instance);
        Assert.IsTrue(provider.Session.Symbols.TryGetModule("Program", out var projects));

        await handler.Handle(Request(Procedure("Main")) with { ModuleName = "Program", SessionModule = true }, CancellationToken.None);

        Assert.IsTrue(provider.Session.Symbols.TryGetModule("Program", out var module));
        Assert.AreSame(projects, module, "the project's module, and no other");
        Assert.AreEqual(projects.GetType(), module.GetType());
        Assert.IsTrue(provider.Session.Symbols.DeclaredIn(module.Uri).Any(symbol => symbol.Name == "Main"), "Main is a procedure of the project's module");
    }

    // the project, the libraries it references and its procedural modules are matched in one tier (MS-VBAL §5.6.10): a module of the client's own named after one
    // would make the name match twice.
    [TestMethod]
    [DataRow("VBA", DisplayName = "the standard library")]
    [DataRow("Scripting", DisplayName = "a library the project references")]
    [DataRow("Project1", DisplayName = "the project")]
    public async Task AModuleOfTheClientsOwn_NamedAfterALibraryOrTheProject_IsNotDefined(string name)
    {
        var provider = new EnvironmentSessionProvider(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false),
            new MockFileSystem(),
            NullLogger<EnvironmentSessionProvider>.Instance);
        provider.Compose(new RDCoreProject
        {
            Name = "Project1",
            Modules = [new RDCoreModule { RelativeUri = "src/Mod1.bas" }],
            References = [new RDCoreReference { Name = "Scripting" }],
        }, WorkspaceRoot);
        var handler = new DefineSymbolsHandler(provider, NSubstitute.Substitute.For<RDCore.Runtime.Support.Host.IProjectAnalysisSessions>(), Substitute.For<IErrorDetailsRecorder>(), NullLogger<DefineSymbolsHandler>.Instance);

        await handler.Handle(Request(Procedure("Main")) with { ModuleName = name, SessionModule = true }, CancellationToken.None);

        Assert.IsFalse(provider.Session.Symbols.TryGetModule(name, out _));
    }

    [TestMethod]
    public async Task BeforeSessionComposed_IsANoOp()
    {
        var result = await NewHandler(compose: false).Handle(Request(Procedure("DoWork")), CancellationToken.None);

        Assert.AreEqual(0, result.Defined);
        Assert.AreEqual(0, result.Skipped.Count);
    }

    private static readonly Uri NavalLibRoot = new("file:///c:/ws/NavalLib/");

    private static DefineSymbolsParams NavalLibRequest(string moduleName, ModuleType kind, ModuleDirectives directives, params SymbolDescriptor[] symbols)
        => Request(symbols) with
        {
            ModuleName = moduleName,
            ModuleUri = SymbolUri.Member(NavalLibRoot, moduleName),
            Library = "NavalLib",
            LibraryPriority = 1,
            ModuleType = kind,
            Directives = directives,
        };

    // a module of a referenced project named as one of the session's own is another module: each name resolves to its own module's member, and the
    // session's own module keeps the name.
    [TestMethod]
    public async Task AModuleOfAReferencedProject_StandsBesideTheSessionsOwnModuleOfTheSameName()
    {
        var provider = new EnvironmentSessionProvider(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false),
            new MockFileSystem(),
            NullLogger<EnvironmentSessionProvider>.Instance);
        provider.Compose(new RDCoreProject { Modules = [new RDCoreModule { RelativeUri = "src/Mod1.bas" }] }, WorkspaceRoot);
        var handler = new DefineSymbolsHandler(provider, Substitute.For<IProjectAnalysisSessions>(), Substitute.For<IErrorDetailsRecorder>(),
            NullLogger<DefineSymbolsHandler>.Instance);
        var symbols = provider.Session.Symbols;
        Assert.IsTrue(symbols.TryGetModule("Mod1", out var own));

        var library = await handler.Handle(
            NavalLibRequest("Mod1", ModuleType.StdModule, ModuleDirectives.None, Procedure("PlaceShip")), CancellationToken.None);
        await handler.Handle(Request(Procedure("DoWork")), CancellationToken.None);

        Assert.AreEqual(3, library.Defined, "the project, its module and its procedure");
        Assert.IsTrue(symbols.TryGetModule("Mod1", out var named));
        Assert.AreSame(own, named, "the session's own module keeps its name");
        Assert.AreEqual(own!.Uri, symbols.Resolver.ResolveValue("DoWork", ScopeKind.Module, own.Uri).Symbol!.ParentUri);

        var placeShip = symbols.Resolver.ResolveValue("PlaceShip", ScopeKind.Module, own.Uri).Symbol;
        Assert.IsNotNull(placeShip, "a public procedure of a module of the referenced project is a name of the session's");
        Assert.AreEqual(SymbolUri.Member(NavalLibRoot, "Mod1"), placeShip.ParentUri);
        Assert.AreEqual("NavalLib", placeShip.GetProperty(SymbolProperties.Library));
        Assert.IsFalse(symbols.DeclaredIn(own.Uri).Any(symbol => symbol.Name == "PlaceShip"),
            "the referenced project's procedure is not a member of the session's module");
    }

    // what the referenced project keeps to itself is defined, since its own code runs in the session, and the project's code does not see it by name: a
    // class it does not expose (MS-VBAL §5.2.4.1.1), and a private procedure (MS-VBAL §5.3.1.1).
    [TestMethod]
    public async Task WhatAReferencedProjectDoesNotExpose_IsDefined_AndIsNoNameOfTheProject()
    {
        var provider = new EnvironmentSessionProvider(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false),
            new MockFileSystem(),
            NullLogger<EnvironmentSessionProvider>.Instance);
        provider.Compose(new RDCoreProject { Modules = [new RDCoreModule { RelativeUri = "src/Mod1.bas" }] }, WorkspaceRoot);
        var handler = new DefineSymbolsHandler(provider, Substitute.For<IProjectAnalysisSessions>(), Substitute.For<IErrorDetailsRecorder>(),
            NullLogger<DefineSymbolsHandler>.Instance);
        var privateProcedure = Procedure("Clear") with { AccessModifier = AccessModifier.Private };

        var hidden = await handler.Handle(
            NavalLibRequest("Board", ModuleType.ClassModule, ModuleDirectives.None, Procedure("Reset")), CancellationToken.None);
        var exposed = await handler.Handle(
            NavalLibRequest("Grid", ModuleType.ClassModule, new ModuleDirectives(Exposed: true), Procedure("Fire"), privateProcedure),
            CancellationToken.None);

        var symbols = provider.Session.Symbols;
        Assert.IsTrue(symbols.TryGetModule("Mod1", out var mod1));
        Assert.IsTrue(hidden.Defined > 0 && exposed.Defined > 0);
        Assert.IsTrue(symbols.TryGetModule(SymbolUri.Member(NavalLibRoot, "Board"), out var board), "a class the project does not expose is defined");
        Assert.IsTrue(symbols.TryGetModule(SymbolUri.Member(NavalLibRoot, "Grid"), out var grid));
        Assert.IsNull(symbols.Resolver.ResolveType("Board", ScopeKind.Module, mod1.Uri).Symbol, "and is no type name of the project's");
        Assert.AreSame(grid, symbols.Resolver.ResolveType("Grid", ScopeKind.Module, mod1.Uri).Symbol, "an exposed class is");
        Assert.AreEqual(grid.Uri, symbols.Resolver.ResolveValue("Clear", ScopeKind.Module, grid.Uri).Symbol?.ParentUri, "a private procedure is defined");
        Assert.AreEqual(board.Uri, symbols.Resolver.ResolveValue("Reset", ScopeKind.Module, board.Uri).Symbol?.ParentUri);
    }

    private static readonly (string File, string Name, ModuleType Kind) OwnMod1 = ("src/Mod1.bas", "Mod1", ModuleType.StdModule);

    // the session's own modules, of the kinds given: their source is not read.
    private static (EnvironmentSessionProvider Provider, DefineSymbolsHandler Handler) WithOwnModules(
        params (string File, string Name, ModuleType Kind)[] modules)
    {
        var provider = new EnvironmentSessionProvider(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false),
            new MockFileSystem(),
            NullLogger<EnvironmentSessionProvider>.Instance);
        provider.Compose(
            new RDCoreProject { Modules = [.. modules.Select(module => new RDCoreModule { RelativeUri = module.File })] },
            WorkspaceRoot,
            modules.ToDictionary(module => module.File,
                IReadOnlyList<DeclaredModule> (module) => [new DeclaredModule(module.Name, module.Kind, IsBlock: false)]));
        var handler = new DefineSymbolsHandler(provider, Substitute.For<IProjectAnalysisSessions>(), Substitute.For<IErrorDetailsRecorder>(),
            NullLogger<DefineSymbolsHandler>.Instance);
        return (provider, handler);
    }

    // a predeclared class the referenced project exposes has its default instance, a name of the session's: the class's name binds to it, and the members
    // of the class are reached through it.
    [TestMethod]
    public async Task TheDefaultInstanceOfAnExposedPredeclaredClassOfAReferencedProject_IsANameOfTheSession()
    {
        var (provider, handler) = WithOwnModules(OwnMod1);
        var mod1 = provider.Session.Symbols.TryGetModule("Mod1", out var own) ? own! : throw new AssertFailedException("Mod1");

        var fleet = await handler.Handle(
            NavalLibRequest("Fleet", ModuleType.ClassModule, new ModuleDirectives(PredeclaredId: true, Exposed: true), Procedure("Reset")),
            CancellationToken.None);

        Assert.AreEqual(4, fleet.Defined, "the project, the class, its default instance and its procedure");
        var instance = provider.Session.Symbols.Resolver.ResolveValue("Fleet", ScopeKind.Module, mod1.Uri).Symbol as VBPredeclaredInstanceSymbol;
        Assert.IsNotNull(instance, "the class's name binds to its default instance");
        Assert.AreEqual("NavalLib", instance.GetProperty(SymbolProperties.Library));
        Assert.AreEqual(SymbolUri.Member(NavalLibRoot, "Fleet"), instance.ClassModule.Uri);
    }

    // the session's own predeclared class of the same name keeps the name, as the session's own module does; the referenced one's default instance is
    // another variable beside it.
    [TestMethod]
    public async Task TheSessionsOwnDefaultInstance_ShadowsAReferencedProjectsOfTheSameName()
    {
        var (provider, handler) = WithOwnModules(OwnMod1, ("src/Board.cls", "Board", ModuleType.ClassModule));
        var predeclared = new ModuleDirectives(PredeclaredId: true, Exposed: true);
        await handler.Handle(Request() with { ModuleName = "Board", Directives = predeclared }, CancellationToken.None);
        var mod1 = provider.Session.Symbols.TryGetModule("Mod1", out var own) ? own! : throw new AssertFailedException("Mod1");

        var library = await handler.Handle(
            NavalLibRequest("Board", ModuleType.ClassModule, predeclared, Procedure("Reset")), CancellationToken.None);

        Assert.AreEqual(4, library.Defined, "the project, the class, its default instance and its procedure: none takes the place of the session's own");
        var resolution = provider.Session.Symbols.Resolver.ResolveValue("Board", ScopeKind.Module, mod1.Uri);
        var bound = resolution.Symbol as VBPredeclaredInstanceSymbol;
        Assert.IsNotNull(bound, $"{resolution.Symbol?.Uri} {resolution.ErrorCode}");
        Assert.IsNull(bound.GetProperty(SymbolProperties.Library), "the session's own default instance");
        Assert.AreEqual(SymbolUri.Member(WorkspaceRoot, "Board"), bound.ClassModule.Uri);
    }

    // a class sent again as no longer predeclared, or no longer exposed, has no default instance in the session; one that still is keeps its own.
    [TestMethod]
    [DataRow(false, true, DisplayName = "no longer predeclared")]
    [DataRow(true, false, DisplayName = "no longer exposed")]
    public async Task AReferencedClassThatNoLongerHasADefaultInstance_LeavesNoneInTheSession(bool predeclared, bool exposed)
    {
        var (provider, handler) = WithOwnModules(OwnMod1);
        var mod1 = provider.Session.Symbols.TryGetModule("Mod1", out var own) ? own! : throw new AssertFailedException("Mod1");
        await handler.Handle(
            NavalLibRequest("Fleet", ModuleType.ClassModule, new ModuleDirectives(PredeclaredId: true, Exposed: true)), CancellationToken.None);
        var before = provider.Session.Symbols.Resolver.ResolveValue("Fleet", ScopeKind.Module, mod1.Uri).Symbol;
        await handler.Handle(
            NavalLibRequest("Fleet", ModuleType.ClassModule, new ModuleDirectives(PredeclaredId: true, Exposed: true)) with { Replace = true },
            CancellationToken.None);
        Assert.AreSame(before, provider.Session.Symbols.Resolver.ResolveValue("Fleet", ScopeKind.Module, mod1.Uri).Symbol, "sent again, it stays");

        await handler.Handle(
            NavalLibRequest("Fleet", ModuleType.ClassModule, new ModuleDirectives(PredeclaredId: predeclared, Exposed: exposed)) with { Replace = true },
            CancellationToken.None);

        Assert.IsNotInstanceOfType<VBPredeclaredInstanceSymbol>(provider.Session.Symbols.Resolver.ResolveValue("Fleet", ScopeKind.Module, mod1.Uri).Symbol);
    }

    private sealed class RecordingLogger : Microsoft.Extensions.Logging.ILogger<DefineSymbolsHandler>
    {
        public List<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    [TestMethod]
    public async Task AModuleItRefusesTheSameWay_EachTimeItIsSent_IsLoggedOnce_AndOnceAgainWhenItLoads()
    {
        // a standard module that names Me, sent as an editor sends it - at every edit.
        var logger = new RecordingLogger();
        var handler = NewHandler(logger: logger);
        await handler.Handle(Request(Procedure("Foo")), CancellationToken.None);

        DefineSymbolsParams Code(string body) => Request() with
        {
            CodeOnly = true,
            ParseResultJson = PlatformJson.Serialize(new RDCore.Parsing.ModuleParser().Parse(new Uri("file:///c:/ws/src/Mod1.bas"), $"Public Sub Foo()\r\n{body}\r\nEnd Sub\r\n")),
        };

        for (var edit = 0; edit < 3; edit++)
        {
            Assert.AreNotEqual(0, (await handler.Handle(Code("    Debug.Print TypeName(Me)"), CancellationToken.None)).CodeErrors.Count);
        }

        Assert.AreEqual(1, logger.Entries.Count(entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning && entry.Message.Contains("was not loaded")),
            string.Join("\n", logger.Entries.Select(entry => entry.Message)));

        Assert.AreEqual(0, (await handler.Handle(Code("    Debug.Print 1"), CancellationToken.None)).CodeErrors.Count);
        Assert.AreEqual(1, logger.Entries.Count(entry => entry.Message.Contains("loads again")));
    }
}
