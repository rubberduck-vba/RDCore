using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RDCore.SDK.Client;
using RDCore.SDK.Client.Connection;
using RDCore.SDK.Platform;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;

namespace RDCore.LanguageServer;

internal class RDCoreServerProxyFactory(IServiceProvider services) : IRDCoreServerProxyFactory
{
    public RDCoreServerProxy Create(CoreServerComponent platformComponent, CorePlatformClientCapabilities capabilities,
        Action<IRDCoreLSPHandlerConfigurationBuilder>? configureHandlers = default,
        Action<IServiceCollection>? configureServices = default)
        // IChildConnectionFactory resolves a fresh process + transport per proxy.
        => new(services.GetRequiredService<IOptions<SdkAppOptions>>(),
            platformComponent, capabilities,
            configureHandlers ?? (builder => { }), configureServices ?? (services => { }),
            services.GetRequiredService<IChildConnectionFactory>(),
            services.GetRequiredService<ILogger<RDCoreClientApp>>());
}
