using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RDCore.SDK.Platform;
using System.IO;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Services;

namespace RDCore.LanguageServer;

/// <summary>
/// The RDCore <strong>RD-VBA Language Server</strong> application host.
/// </summary>
internal sealed class CoreLanguageServerHost() : RDCorePlatformServerHost<CoreLanguageServerApp>()
{
    protected override void ConfigureAdditionalExternalServices(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddTransient<IHealthCheckService<RDCoreServerProxy>, HealthCheckService<RDCoreServerProxy>>()
            .AddSingleton<IRDCoreServerProxyFactory, RDCoreServerProxyFactory>()
            .AddSingleton<IPlatformCompositionService, PlatformCompositionService>()
            .AddSingleton<IPlatformOrchestrationService, PlatformOrchestrationService>();
    }

    protected override void ConfigureExternalLogging(IServiceCollection services, ILoggingBuilder builder, IConfiguration configuration)
    {
        builder.AddFile(Path.Combine(PlatformEnvironment.Default.LogsDirectory, "RDCore.LanguageServer.log"));
        base.ConfigureExternalLogging(services, builder, configuration);
    }
}
