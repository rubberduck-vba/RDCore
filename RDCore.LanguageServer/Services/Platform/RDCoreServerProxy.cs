using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using RDCore.SDK.Client;
using RDCore.SDK.Client.Connection;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;

namespace RDCore.LanguageServer.Services.Platform;

public interface IRDCoreServerProxyFactory
{
    RDCoreServerProxy Create(CoreServerComponent platformComponent, CorePlatformClientCapabilities capabilities,
        Action<IRDCoreLSPHandlerConfigurationBuilder>? configureHandlers = default,
        Action<IServiceCollection>? configureServices = default,
        ExtensionInfo? extensionInfo = default);
}

public class RDCoreServerProxy : RDCoreClientApp
{
    private readonly CoreServerComponent _platformComponent;
    private readonly CorePlatformClientCapabilities _capabilities;
    private readonly Action<IRDCoreLSPHandlerConfigurationBuilder> _configureHandlers;
    private readonly Action<IServiceCollection> _configureServices;

    public RDCoreServerProxy(
        IOptions<SdkAppOptions> options,
        CoreServerComponent platformComponent,
        CorePlatformClientCapabilities capabilities,
        Action<IRDCoreLSPHandlerConfigurationBuilder> configureHandlers,
        Action<IServiceCollection> configureServices,
        IChildConnectionFactory connectionFactory,
        ILogger<RDCoreClientApp> logger,
        ExtensionInfo? extensionInfo = default)
        : base(options, connectionFactory, logger)
    {
        _platformComponent = platformComponent;
        _capabilities = capabilities;
        _configureHandlers = configureHandlers;
        _configureServices = configureServices;
        ExtensionInfo = extensionInfo;
    }

    public override CoreServerComponent PlatformComponent => _platformComponent;

    // platform capabilities travel over rdcore/platform/initialize, not the LSP Experimental node.
    protected override ClientCapabilities ConfigureClientCapabilities(ClientCapabilities capabilities) => capabilities;

    protected override CorePlatformClientCapabilities GetExpectedCapabilities() => _capabilities;

    protected override void ConfigureHandlers(IRDCoreLSPHandlerConfigurationBuilder builder) => _configureHandlers(builder);

    protected override void ConfigureServices(IServiceCollection services) => _configureServices(services);

    // the owning language server escalates a terminal fault through its own shutdown path
    // (CoreLanguageServerApp.BringUpCoreComponentAsync), so the proxy does nothing here.
    protected override void OnConnectionTerminated() { }

    protected override void Dispose(bool disposing) { }
}
