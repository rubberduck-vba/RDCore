using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RDCore.LanguageServer;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Runtime;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Client;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// <c>rdcore/session/execute</c> as the language server handles it: a module with a syntax error goes to the host, which refuses to run it when it loads it, unless
/// the error is in a branch of a <c>#If</c> that is excluded, which only the host knows (<strong>RD-VBAL §2.6.1</strong>).
/// </summary>
[TestClass]
public sealed class SessionExecuteHandlerTests
{
    private readonly IPlatformOrchestrationService _orchestration = Substitute.For<IPlatformOrchestrationService>();
    private readonly IPlatformClientCapabilitiesService _capabilities = Substitute.For<IPlatformClientCapabilitiesService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();
    private readonly ISymbolSyncService _symbols = Substitute.For<ISymbolSyncService>();
    private readonly IRDCoreClientApp _host = Substitute.For<IRDCoreClientApp>();

    public SessionExecuteHandlerTests()
    {
        _capabilities.Expects(Arg.Any<Func<LanguageServerCapabilities, CorePlatformClientCapability>>()).Returns(true);
        _orchestration.RuntimeEnvironment.Returns(_host);
        _parsing.ParseFragmentAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new ModuleParser().Parse((Uri)call[0], (string)call[1]));
        _symbols.SyncModuleAsync(Arg.Any<string>(), Arg.Any<ModuleParseResult>(), Arg.Any<CancellationToken>())
            .Returns(new Uri("file:///ws/Prog.bas"));
        _host.SendRequestAsync<HostExecuteParams, ExecuteResult>(Arg.Any<HostExecuteParams>(), Arg.Any<CancellationToken>())
            .Returns(new ExecuteResult { Outcome = ExecutionOutcome.Completed });
    }

    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-session-execute-ws");

    private SessionExecuteHandler Handler()
        => new(_orchestration, _capabilities, _parsing, _symbols,
            Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } }), NullLogger<SessionExecuteHandler>.Instance);

    [TestMethod]
    [DataRow("#If False Then\r\nDebug.Print 1 +\r\n#End If", DisplayName = "in the branch of a #If False")]
    [DataRow("a = = 1", DisplayName = "an operator where an expression is")]
    public async Task AModuleWithASyntaxError_IsSentToTheHost(string statement)
    {
        var source = $"Attribute VB_Name = \"Prog\"\r\nPublic Sub Main()\r\n{statement}\r\nEnd Sub\r\n";

        var result = await Handler().Handle(new SessionExecuteRequest { ModuleName = "Prog", Source = source, EntryPoint = "Main" }, CancellationToken.None);

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, "the host's answer");
        await _host.Received(1).SendRequestAsync<HostExecuteParams, ExecuteResult>(Arg.Any<HostExecuteParams>(), Arg.Any<CancellationToken>());
    }
}
