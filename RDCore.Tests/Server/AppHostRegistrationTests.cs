using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RDCore.SDK;
using RDCore.SDK.Client;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Server.Services.States;

namespace RDCore.Tests.Server;

[TestClass]
public class AppHostRegistrationTests
{
    private static IServiceProvider BuildExternalServices()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);
        new TestAppHost().Configure(services, configuration);
        return services.BuildServiceProvider();
    }

    [TestMethod]
    public void ServerStateProvider_IsRegisteredAsASingleton()
    {
        var provider = BuildExternalServices();

        var first = provider.GetRequiredService<IServerStateProvider>();
        var second = provider.GetRequiredService<IServerStateProvider>();

        // the server app, the LSP lifecycle handlers and the health check coordinate through this
        // instance's state and token sources; separate instances break graceful shutdown.
        Assert.AreSame(first, second);
    }

    [TestMethod]
    public void ServerProcess_StaysTransient()
    {
        var provider = BuildExternalServices();

        Assert.AreNotSame(
            provider.GetRequiredService<IRDCoreServerProcess>(),
            provider.GetRequiredService<IRDCoreServerProcess>());
    }

    // exposes AppHost's protected external-service registration for assertions.
    private sealed class TestAppHost : AppHost<TestAppHost.FakeApp>
    {
        public void Configure(IServiceCollection services, IConfiguration configuration)
            => ConfigureExternalServices(services, configuration);

        protected override void Configure(IConfigurationBuilder configuration, IServiceCollection services, string[] args) { }

        internal sealed class FakeApp : IRDCoreApp
        {
            public CoreServerComponent PlatformComponent => CoreServerComponent.LanguageServer;
            public Task RunAsync(IServiceProvider provider, string[] args) => Task.CompletedTask;
            public void LogIfEnabled(LogLevel logLevel, string message) { }
            public void Dispose() { }
        }
    }
}
