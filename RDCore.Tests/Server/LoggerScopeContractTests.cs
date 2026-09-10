using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.SDK.Client;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Logging;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Server.Services.States;

namespace RDCore.Tests.Server;

/// <summary>
/// Both client-forwarding loggers replace OmniSharp's <c>AddLanguageProtocolLogging()</c> provider, so
/// their <see cref="ILogger.BeginScope{TState}"/> must honour the same contract OmniSharp relies on:
/// never return <c>null</c>. OmniSharp's <c>TimeLoggerExtensions</c> wraps request routing (including
/// <c>initialize</c>) in <c>using (logger.BeginScope(…))</c> and disposes the result with no null
/// check — a <c>null</c> scope throws out of the <c>initialize</c> route and the server never starts.
/// </summary>
[TestClass]
public sealed class LoggerScopeContractTests
{
    private sealed class CapturingServerApp() : RDCoreServerApp(
        Options.Create(new SdkAppOptions()),
        Substitute.For<IServerStateProvider>(),
        Substitute.For<IHealthCheckService<RDCoreServerApp>>(),
        Substitute.For<ILanguageServerProtocolTransportLayer>(),
        Substitute.For<ILogger<RDCoreServerApp>>())
    {
        public override CoreServerComponent PlatformComponent => CoreServerComponent.LanguageServer;
        internal override void SendClientTrace(LogLevel level, string message, string? verbose) { }
        protected override void Dispose(bool disposing) { }
        protected override void ConfigureHandlers(IRDCoreLSPHandlerConfigurationBuilder builder) { }
        protected override void RegisterServerCapabilities(ILanguageServer server, ClientCapabilities clientCapabilities) { }
    }

    private static ILogger ScrubbingLogger()
        => new ScrubbingLanguageServerLoggerProvider(
            Substitute.For<ILanguageServerFacade>(),
            Options.Create(new SdkServerOptions())).CreateLogger("cat");

    private static ILogger ClientTraceLogger()
        => new ClientTraceLoggerProvider(() => new CapturingServerApp()).CreateLogger("cat");

    private static IEnumerable<object[]> Loggers()
    {
        yield return [ScrubbingLogger()];
        yield return [ClientTraceLogger()];
    }

    [TestMethod]
    [DynamicData(nameof(Loggers))]
    public void BeginScope_NeverReturnsNull(ILogger logger)
        => Assert.IsNotNull(logger.BeginScope(new { }));

    [TestMethod]
    [DynamicData(nameof(Loggers))]
    public void BeginScope_ResultSurvivesTheOmniSharpTimeLoggerPattern(ILogger logger)
    {
        // this is exactly what OmniSharp.Extensions.JsonRpc.TimeLoggerExtensions does around every
        // routed request: take the scope and dispose it unconditionally.
        var scope = logger.BeginScope(new { });
        scope!.Dispose();
        scope.Dispose();
    }

    [TestMethod]
    public void NullLogScope_IsIdempotentlyDisposable()
    {
        var scope = NullLogScope.Instance;
        scope.Dispose();
        scope.Dispose();
    }
}
