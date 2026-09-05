using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RDCore.SDK.Client;
using RDCore.SDK.Platform;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;
using System.IO.Abstractions;

namespace RDCore.LanguageServer;

internal class RDCoreServerProxyFactory(IServiceProvider services) : IRDCoreServerProxyFactory
{
    public RDCoreServerProxy Create(CoreServerComponent platformComponent, CorePlatformClientCapabilities capabilities,
        Action<IRDCoreLSPHandlerConfigurationBuilder>? configureHandlers = default,
        Action<IServiceCollection>? configureServices = default)
        // resolve a fresh process + health check per proxy: one instance cannot supervise several children.
        => new(services.GetRequiredService<IOptions<SdkAppOptions>>(),
            platformComponent, capabilities,
            configureHandlers ?? (builder => { }), configureServices ?? (services => { }),
            services.GetRequiredService<IRDCoreServerProcess>(),
            services.GetRequiredService<IFileSystem>(),
            services.GetRequiredService<IHealthCheckService<RDCoreServerProxy>>(),
            services.GetRequiredService<ILanguageServerProtocolTransportLayer>(),
            services.GetRequiredService<ILogger<RDCoreServerProxy>>());
}
