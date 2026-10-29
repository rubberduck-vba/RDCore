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
