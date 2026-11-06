using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RDCore.LanguageServer;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Runtime;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Client;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;
using System.IO.Abstractions;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// <strong>MS-VBAL §5.6.10</strong> a module is found before a referenced project or library: a module of the client's own named after one would hide every name of it,
/// and its run is refused, as not found, with the reason.
/// </summary>
[TestClass]
public sealed class SessionExecuteHandlerTests
{
    private sealed class Parsed : Exception;

    private static string Symbols
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RDCore.slnx")))
            {
                directory = directory.Parent;
            }

            Assert.IsNotNull(directory, "the repository root, where RDCore.slnx is");
            return Path.Combine(directory.FullName, "Symbols");
        }
    }

    // a workspace of project Project1, which references Scripting; a run that gets as far as its parse is told so by the parse, which throws.
    private static (SessionExecuteHandler Handler, IParsingClientService Parsing) NewHandler()
    {
        var orchestration = Substitute.For<IPlatformOrchestrationService>();
        orchestration.RuntimeEnvironment.Returns(Substitute.For<IRDCoreClientApp>());
        var capabilities = Substitute.For<IPlatformClientCapabilitiesService>();
        capabilities.Expects(Arg.Any<Func<LanguageServerCapabilities, CorePlatformClientCapability>>()).Returns(true);
        var parsing = Substitute.For<IParsingClientService>();
        parsing.ParseFragmentAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<ModuleParseResult>(new Parsed()));
        var root = Path.Combine(Path.GetTempPath(), "rdcore-session-execute");
        var documents = Substitute.For<IWorkspaceDocumentService>();
        documents.WorkspaceRoot.Returns(root);
        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.ProjectName.Returns("Project1");
        libraries.Libraries.Returns(LibrarySymbolProvider.Load(
            new DirectoryLibrarySource(new FileSystem(), Symbols), [new RDCore.SDK.Workspace.RDCoreReference { Name = "Scripting" }]));

        return (new SessionExecuteHandler(orchestration, capabilities, parsing, Substitute.For<ISymbolSyncService>(),
            Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = root } }),
            documents, libraries, NullLogger<SessionExecuteHandler>.Instance), parsing);
    }

    private static SessionExecuteRequest Run(string moduleName, bool sessionModule)
        => new() { Source = "Public Sub Main()\r\nEnd Sub\r\n", ModuleName = moduleName, EntryPoint = "Main", SessionModule = sessionModule };

    [TestMethod]
    [DataRow("VBA", DisplayName = "the standard library")]
    [DataRow("vba", DisplayName = "the standard library, in any case")]
    [DataRow("Scripting", DisplayName = "a library the project references")]
    [DataRow("Project1", DisplayName = "the project")]
    public async Task AModuleOfTheClientsOwn_NamedAfterALibraryOrTheProject_IsNotFound_AndTheResultSaysWhy(string moduleName)
    {
        var (handler, parsing) = NewHandler();

        var result = await handler.Handle(Run(moduleName, sessionModule: true), CancellationToken.None);

        Assert.AreEqual(ExecutionOutcome.NotFound, result.Outcome);
        StringAssert.Contains(result.ErrorMessage, $"'{moduleName}'");
        _ = parsing.DidNotReceiveWithAnyArgs().ParseFragmentAsync(default!, default!, default);
    }

    // the module of a name of no library is run, as is a module the client does not say is its own: whether the project has it is the host's to say.
    [TestMethod]
    [DataRow("Program", true, DisplayName = "the client's own module, named after nothing")]
    [DataRow("VBA", false, DisplayName = "a module the client does not say is its own")]
    public async Task AnyOtherRun_IsParsed(string moduleName, bool sessionModule)
    {
        var (handler, parsing) = NewHandler();

        // the parse throws, and the request answers with that error: what matters is that the run got as far as it.
        await Assert.ThrowsAsync<Exception>(() => handler.Handle(Run(moduleName, sessionModule), CancellationToken.None));

        _ = parsing.ReceivedWithAnyArgs(1).ParseFragmentAsync(default!, default!, default);
    }
}
