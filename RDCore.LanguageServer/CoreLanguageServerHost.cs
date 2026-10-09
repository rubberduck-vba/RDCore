using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RDCore.LanguageServer.Server;
using RDCore.SDK.Platform;
using System.IO;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Logging;

namespace RDCore.LanguageServer;

/// <summary>
/// The RDCore <strong>RD-VBA Language Server</strong> application host.
/// </summary>
internal sealed class CoreLanguageServerHost() : RDCorePlatformServerHost<CoreLanguageServerApp>()
{
    protected override void ConfigureAdditionalExternalServices(IServiceCollection services, IConfiguration configuration)
        => services.AddPlatformCoordination(Info.Version ?? new Version(0, 0, 0));

    protected override void ConfigureExternalLogging(IServiceCollection services, ILoggingBuilder builder, IConfiguration configuration)
    {
        builder.AddFile(
            Path.Combine(PlatformEnvironment.Default.LogsDirectory, "RDCore.LanguageServer.log"),
            ResolveTraceLevel(configuration));

        // forward the same narration to the connected client ($/logTrace + window/logMessage,
        // scrubbed); the file sink above keeps the unredacted copy.
        builder.Services.AddSingleton<ILoggerProvider>(sp =>
            new ClientTraceLoggerProvider(() => sp.GetRequiredService<CoreLanguageServerApp>()));

        base.ConfigureExternalLogging(services, builder, configuration);
    }
}
