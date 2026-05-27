using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.SDK;
using RDCore.SDK.Client;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Server.Services.States;
using RDCore.Workspace.Services;
using System.Reflection;

namespace RDCore.Server;

internal sealed class CoreLanguageServerApp(
    IOptions<LanguageServerAppOptions> options,
    IServerStateProvider serverStateProvider,
    IHealthCheckService healthCheckService,
    IWorkspaceService workspaceService,
    ILogger<CoreLanguageServerApp> logger) : LanguageServerApp(options, serverStateProvider, healthCheckService, logger)
{
    protected override ServerInfo GetServerInfo()
    {
        if (Assembly.GetExecutingAssembly()?.GetName() is AssemblyName assemblyName)
            return new()
            {
                Name = assemblyName?.Name ?? "RDCore.SDK",
                Version = assemblyName?.Version?.ToString(3)
            };
        throw new BadConfigurationException("Could be retrieve the executing assembly name (name/version).", Assembly.GetExecutingAssembly().FullName);
    }

    protected override void ConfigureHandlers(IRDCoreLSPHandlerConfigurationBuilder builder)
    {
        // TODO
    }

    protected override async Task RegisterServerCapabilitiesAsync(ILanguageServer server, ClientCapabilities clientCapabilities, CancellationToken token)
    {
        // TODO register the server capabilities that the client can handle beyond core LSP.
    }

    protected override async Task OnLanguageServerInitializeCompletedAsync(Uri workspaceRoot, CancellationToken token)
    {
        await workspaceService.LoadAsync(workspaceRoot.AbsoluteUri);
    }

    protected override async Task OnLanguageServerStartedAsync(CancellationToken token)
    {
        // server is ready. what will it do?
        // TODO start parser pipeline
    }

    protected sealed override void Dispose(bool disposing)
    {
        // nothing to do
    }
}
