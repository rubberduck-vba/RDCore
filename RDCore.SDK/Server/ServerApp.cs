using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Server.Services.States;
using System.Reflection;

namespace RDCore.SDK.Server;

public abstract class ServerApp(IServerStateProvider serverStateProvider)
{
    private static readonly Lazy<AssemblyName> _info = new(() => typeof(ServerApp).Assembly.GetName(), LazyThreadSafetyMode.PublicationOnly);
    public static AssemblyName Info => _info.Value;

    public async Task RunAsync()
    {
        var services = new ServiceCollection();
        ConfigureCoreServices(services);
        ConfigureAppServices(services);

        using var provider = services.BuildServiceProvider();
        var app = provider.GetRequiredService<ILanguageServerApp>();
        await app.RunAsync(provider);
    }

    private void ConfigureCoreServices(IServiceCollection services)
    {
        services
            .AddSingleton(provider => Info.Version!) // TODO get rid of this
            .AddSingleton<IServerCommandProvider>(provider => new ServerCommandProvider(provider))

            .AddSingleton<ILanguageServerApp, LanguageServerApp>()
            .AddSingleton<IServerStateProvider>(serverStateProvider)
            .AddSingleton<IHealthCheckService, HealthCheckService>()

            .AddLogging(ConfigureLogging);
    }

    protected abstract void ConfigureAppServices(IServiceCollection services);

    protected virtual void ConfigureLogging(ILoggingBuilder builder)
    {
        if (serverStateProvider.Options.Verbose)
        {
            builder.SetMinimumLevel(LogLevel.Trace);
        }
        else
        {
            builder.SetMinimumLevel(LogLevel.Information);
        }

        builder.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "[HH:mm:ss.fff] ";
        });

#if DEBUG
        builder.AddDebug();
#endif
    }
}
