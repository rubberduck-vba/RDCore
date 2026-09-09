using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.SDK.Client;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Logging;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Server.Services.States;

namespace RDCore.Tests.Server;

[TestClass]
public sealed class ClientTraceLoggerTests
{
    private sealed class CapturingServerApp() : RDCoreServerApp(
        Options.Create(new SdkAppOptions()),
        Substitute.For<IServerStateProvider>(),
        Substitute.For<IHealthCheckService<RDCoreServerApp>>(),
        Substitute.For<ILanguageServerProtocolTransportLayer>(),
        Substitute.For<ILogger<RDCoreServerApp>>())
    {
        public (LogLevel Level, string Message, string? Verbose)? Captured { get; private set; }

        public override CoreServerComponent PlatformComponent => CoreServerComponent.LanguageServer;

        internal override void SendClientTrace(LogLevel level, string message, string? verbose)
            => Captured = (level, message, verbose);

        protected override void Dispose(bool disposing) { }
        protected override void ConfigureHandlers(IRDCoreLSPHandlerConfigurationBuilder builder) { }
        protected override void RegisterServerCapabilities(ILanguageServer server, ClientCapabilities clientCapabilities) { }
    }

    private static ILogger NewLogger(CapturingServerApp app)
        => new ClientTraceLoggerProvider(() => app).CreateLogger("RDCore.LS");

    [TestMethod]
    public void Log_ComposesTheRecord_AndForwardsItToSendClientTrace()
    {
        var app = new CapturingServerApp();
        var boom = new InvalidOperationException("boom");

        NewLogger(app).Log(LogLevel.Information, default, "workspace loaded", boom, (state, _) => state);

        Assert.IsNotNull(app.Captured);
        Assert.AreEqual(LogLevel.Information, app.Captured!.Value.Level);
        StringAssert.Contains(app.Captured.Value.Message, "RDCore.LS: workspace loaded");
        StringAssert.Contains(app.Captured.Value.Message, nameof(InvalidOperationException));
        StringAssert.Contains(app.Captured.Value.Verbose!, "boom");
    }

    [TestMethod]
    public void Log_None_IsNotForwarded()
    {
        var app = new CapturingServerApp();

        NewLogger(app).Log(LogLevel.None, default, "x", null, (state, _) => state);

        Assert.IsNull(app.Captured);
    }

    [TestMethod]
    public void Log_WithNoException_PassesNullVerbose()
    {
        var app = new CapturingServerApp();

        NewLogger(app).Log(LogLevel.Warning, default, "heads up", null, (state, _) => state);

        Assert.IsNull(app.Captured!.Value.Verbose);
    }
}
