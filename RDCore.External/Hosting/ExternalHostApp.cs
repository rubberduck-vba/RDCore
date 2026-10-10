using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.External.Protocol;
using RDCore.SDK.Client;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Server.Services.States;

namespace RDCore.External.Hosting;

/// <summary>
/// The external host: the process that makes the calls an environment host's program makes to the outside world.
/// </summary>
/// <remarks>
/// <para>
/// What a program reaches outside the platform - the objects of an automation server, the functions of a native library - runs code the platform did not write, in the
/// process that calls it. A failure of that code takes the process down with it, so it is not the process that owns the session: the environment host starts this one
/// the first time its program reaches out, and a program whose call took it down is told so, as an error it can handle, by a session that is still there.
/// </para>
/// <para>
/// Nothing here is particular to a platform: the servers it reaches are the ones the process was composed with (<see cref="ExternalAutomationService"/>).
/// </para>
/// </remarks>
public sealed class ExternalHostApp(
    IOptions<SdkAppOptions> options,
    IServerStateProvider serverStateProvider,
    IHealthCheckService<ExternalHostApp> healthCheckService,
    ILanguageServerProtocolTransportLayer transportLayer,
    ExternalAutomationService automation,
    ILogger<ExternalHostApp> logger)
    : RDCoreServerApp(options, serverStateProvider, healthCheckService, transportLayer, logger)
{
    /// <inheritdoc/>
    public override CoreServerComponent PlatformComponent => CoreServerComponent.ExternalHost;

    // a call that is being made can be waiting for an event to be handled, and a handler's calls are requests too: they are answered while the call waits.
    /// <inheritdoc/>
    protected override bool HandlesRequestsConcurrently => true;

    /// <inheritdoc/>
    protected override void ConfigureHandlers(IRDCoreLSPHandlerConfigurationBuilder builder)
        => builder
            .WithHandler<AutomationStatusHandler>()
            .WithHandler<AutomationCreateHandler>()
            .WithHandler<AutomationInvokeHandler>()
            .WithHandler<AutomationClassNameHandler>()
            .WithHandler<AutomationMoveNextHandler>()
            .WithHandler<AutomationResetHandler>()
            .WithHandler<AutomationAdviseHandler>()
            .WithHandler<AutomationUnadviseHandler>()
            .WithHandler<AutomationReleaseHandler>();

    /// <inheritdoc/>
    protected override void ConfigureServices(IServiceCollection services) => services.AddSingleton(automation);

    /// <inheritdoc/>
    protected override void RegisterServerCapabilities(ILanguageServer server, ClientCapabilities clientCapabilities)
    {
    }

    // the events of the servers' objects are told to the environment host as they are raised.
    /// <inheritdoc/>
    protected override void OnLanguageServerStarted(ILanguageServer server)
    {
        automation.RaiseOnClient = raised => SendRequestAsync<AutomationEventParams, AutomationEventResult>(raised, CancellationToken.None);
        base.OnLanguageServerStarted(server);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
    }
}
