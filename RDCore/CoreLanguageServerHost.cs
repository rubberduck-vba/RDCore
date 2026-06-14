using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RDCore.SDK.Server;

namespace RDCore.LanguageServer;

/// <summary>
/// The RDCore <strong>RD-VBA Language Server</strong> application host.
/// </summary>
internal sealed class CoreLanguageServerHost() : RDCoreLanguageServerHost<CoreLanguageServerApp>()
{
    protected override void ConfigureAdditionalExternalServices(IServiceCollection services,IConfiguration configuration)
    {
        // TODO
    }
}
