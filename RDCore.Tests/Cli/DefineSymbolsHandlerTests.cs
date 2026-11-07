using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.SDK.Services.VerboseMessages;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Workspace;

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

        return new DefineSymbolsHandler(provider, Substitute.For<IVerboseMessageBuilder>(), logger ?? NullLogger<DefineSymbolsHandler>.Instance);
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
        var handler = new DefineSymbolsHandler(provider, Substitute.For<IVerboseMessageBuilder>(), NullLogger<DefineSymbolsHandler>.Instance);

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
        var handler = new DefineSymbolsHandler(provider, Substitute.For<IVerboseMessageBuilder>(), NullLogger<DefineSymbolsHandler>.Instance);
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
        var handler = new DefineSymbolsHandler(provider, Substitute.For<IVerboseMessageBuilder>(), NullLogger<DefineSymbolsHandler>.Instance);

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
