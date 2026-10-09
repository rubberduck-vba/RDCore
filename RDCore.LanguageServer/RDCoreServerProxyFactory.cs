using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RDCore.LanguageServer.Debugging;
using RDCore.SDK.Client;
using RDCore.SDK.Client.Connection;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Platform;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;

namespace RDCore.LanguageServer;

internal class RDCoreServerProxyFactory(IServiceProvider services) : IRDCoreServerProxyFactory
{
    public RDCoreServerProxy Create(CoreServerComponent platformComponent, CorePlatformClientCapabilities capabilities,
        Action<IRDCoreLSPHandlerConfigurationBuilder>? configureHandlers = default,
        Action<IServiceCollection>? configureServices = default,
        ExtensionInfo? extensionInfo = default)
        // IChildConnectionFactory resolves a fresh process + transport per proxy.
        => new(services.GetRequiredService<IOptions<SdkAppOptions>>(),
            platformComponent, capabilities,
            Handlers(platformComponent, configureHandlers),
            Services(platformComponent, configureServices),
            services.GetRequiredService<IChildConnectionFactory>(),
            services.GetRequiredService<ILogger<RDCoreClientApp>>(),
            extensionInfo);

    // what the environment host says of its own accord - the lines a program prints - is heard by the language server, whoever configured the proxy.
    private Action<IRDCoreLSPHandlerConfigurationBuilder> Handlers(CoreServerComponent component, Action<IRDCoreLSPHandlerConfigurationBuilder>? configure)
        => builder =>
        {
            configure?.Invoke(builder);
            if (component is CoreServerComponent.EnvironmentHost)
            {
                builder.WithHandler<HostOutputHandler>();
            }
        };

    private Action<IServiceCollection> Services(CoreServerComponent component, Action<IServiceCollection>? configure)
        => collection =>
        {
            configure?.Invoke(collection);
            if (component is CoreServerComponent.EnvironmentHost)
            {
                collection.AddSingleton(_ => services.GetRequiredService<IHostOutputRelay>());
            }
        };
}
