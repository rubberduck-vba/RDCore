using Microsoft.Extensions.Options;
using NSubstitute;
using RDCore.LanguageServer;
using RDCore.LanguageServer.Debugging;
using RDCore.LanguageServer.Parsing;
using RDCore.Parsing;
using RDCore.SDK.Client;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Configuration;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The language server parses the condition of a breakpoint where the parser is, gives the host the expression, and refuses what is not one - and the host is asked
/// for every breakpoint all the same, so that the result has one entry per breakpoint asked for, in order.
/// </summary>
[TestClass]
public sealed class ProgramDebugServiceTests
{
    private readonly IRDCoreClientApp _environment = Substitute.For<IRDCoreClientApp>();
    private readonly ProgramDebugService _service;
    private HostDebugBreakpointsParams? _sent;

    public ProgramDebugServiceTests()
    {
        var orchestration = Substitute.For<IPlatformOrchestrationService>();
        orchestration.RuntimeEnvironment.Returns(_environment);

        // the parse server parses a fragment as the parser does.
        var parsing = Substitute.For<IParsingClientService>();
        parsing.ParseFragmentAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ModuleParser().Parse(call.Arg<Uri>(), call.Arg<string>())));

        _environment.SendRequestAsync<HostDebugBreakpointsParams, DebugBreakpointsResult>(Arg.Any<HostDebugBreakpointsParams>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _sent = call.Arg<HostDebugBreakpointsParams>();
                return Task.FromResult(new DebugBreakpointsResult { Breakpoints = [.. _sent.Breakpoints.Select(line => new DebugBreakpoint(line.Line, true))], Judged = true });
            });

        _service = new ProgramDebugService(orchestration, parsing, Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = "file:///c:/ws/" } }));
    }

    [TestMethod]
    public async Task ACondition_ReachesTheHost_AsTheExpressionItParsesTo()
    {
        var result = await _service.SetBreakpointsAsync("Prog", [new SessionBreakpointLine { Line = 4, Condition = "i > 5" }], CancellationToken.None);

        var sent = _sent!.Breakpoints.Single();
        Assert.IsTrue(sent.IsEnabled);
        Assert.IsNotNull(sent.ConditionJson);
        Assert.IsInstanceOfType<ExpressionNode>(PlatformJson.Deserialize<ExpressionNode>(sent.ConditionJson));
        Assert.IsNull(result.Breakpoints.Single().ConditionError);
    }

    [TestMethod]
    public async Task ACondition_ThatIsNotAnExpression_IsRefused_AndItsBreakpointGoesToTheHostDisabled()
    {
        var result = await _service.SetBreakpointsAsync("Prog",
            [new SessionBreakpointLine { Line = 2 }, new SessionBreakpointLine { Line = 4, Condition = "i >" }, new SessionBreakpointLine { Line = 6, Condition = "i = 1" }], CancellationToken.None);

        CollectionAssert.AreEqual(new[] { 2, 4, 6 }, _sent!.Breakpoints.Select(line => line.Line).ToArray(), "every breakpoint is asked for, in order");
        CollectionAssert.AreEqual(new[] { true, false, true }, _sent.Breakpoints.Select(line => line.IsEnabled).ToArray(), "the one refused is never waited at");
        Assert.IsNull(_sent.Breakpoints[1].ConditionJson);

        Assert.IsNull(result.Breakpoints[0].ConditionError);
        Assert.IsFalse(string.IsNullOrEmpty(result.Breakpoints[1].ConditionError), "the person is told why");
        Assert.IsNull(result.Breakpoints[2].ConditionError);
    }

    [TestMethod]
    public async Task ACondition_OnMoreThanOneLine_IsRefused()
    {
        var result = await _service.SetBreakpointsAsync("Prog", [new SessionBreakpointLine { Line = 4, Condition = "i > 5\r\nEnd Sub" }], CancellationToken.None);

        Assert.AreEqual(DebuggerMessages.ExpressionIsOneLine, result.Breakpoints.Single().ConditionError);
    }

    // the parse of a condition as deep as RD-VBA reads (RD-VBAL 2.6.1) is walked with a stack of the walk's own, on a thread no larger than one a request is
    // answered on.
    [TestMethod]
    public void ACondition_NestedAsDeepAsRDVBAReads_ReachesTheHost_OnASmallStack()
    {
        var condition = new string('(', ModuleParser.MaxExpressionNesting) + "1" + new string(')', ModuleParser.MaxExpressionNesting);

        DebugBreakpointsResult? result = null;
        var thrown = SmallStack.Run(() => result = _service.SetBreakpointsAsync("Prog", [new SessionBreakpointLine { Line = 4, Condition = condition }],
            CancellationToken.None).GetAwaiter().GetResult());

        Assert.IsNull(thrown, thrown?.ToString());
        Assert.IsNull(result!.Breakpoints.Single().ConditionError);
        Assert.IsNotNull(_sent!.Breakpoints.Single().ConditionJson);
    }

    [TestMethod]
    public async Task ADisabledBreakpoint_GoesToTheHost_Disabled()
    {
        _ = await _service.SetBreakpointsAsync("Prog", [new SessionBreakpointLine { Line = 4, IsEnabled = false, Condition = "i > 5" }], CancellationToken.None);

        var sent = _sent!.Breakpoints.Single();
        Assert.IsFalse(sent.IsEnabled);
        Assert.IsNotNull(sent.ConditionJson, "its condition is kept for when it is enabled again");
    }
}
