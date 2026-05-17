using Microsoft.Extensions.DependencyInjection;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Services.States;

namespace RDCore.Diagnostics;

internal class RDCoreExtensionServerApp(IServerStateProvider serverStateProvider) : ServerApp(serverStateProvider)
{
    protected override void ConfigureAppServices(IServiceCollection services)
    {
    }
}
