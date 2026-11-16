using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK;
using RDCore.SDK.Assistant;
using RDCore.SDK.Client;
using RDCore.SDK.Client.Connection;
using RDCore.SDK.Platform;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using System.Reflection;

namespace RDCore.CLI.App.Mcp;

// rdc.exe under `rdc mcp`: an LSP client of the platform, like the shell, whose user is an assistant's host. The MCP server answers initialize while the
// platform comes up, and the tools wait for it (McpToolServer); the session ends when the host closes standard input.
internal sealed class RDCoreMcpClientHost(string workspace, Stream input, Stream output) : RDCoreLanguageClientHost<RDCoreMcpClientApp>()
{
    private Task? _server;

    // the console's standard output is the MCP session's, and nothing is written to it but its messages.
    protected override bool WritesToConsole => false;

    protected override string? LogFileName => "RDCore.CLI-mcp.log";

    protected override Task BeforeAppStartAsync(IServiceProvider provider)
    {
        var tools = new ClientAppToolSource(provider.GetRequiredService<RDCoreMcpClientApp>());
        var server = new McpToolServer(tools, new McpServerInfo
        {
            Name = "rdcore",
            Title = "RDCore",
            Version = typeof(RDCoreMcpClientHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty,
            Instructions = string.Format(Resources.Mcp_Instructions, workspace),
        });

        var stopping = provider.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
        _server = Task.Run(() => server.RunAsync(input, output, stopping), CancellationToken.None);
        return Task.CompletedTask;
    }

    // the base waits for Ctrl+C; this session lasts as long as the host keeps standard input open, then shuts the platform down.
    protected override async Task AfterAppRunAsync(IServiceProvider provider)
    {
        try
        {
            await _server!;
        }
        finally
        {
            provider.GetRequiredService<IHostApplicationLifetime>().StopApplication();
            await provider.GetRequiredService<RDCoreMcpClientApp>().ShutdownAsync();
        }
    }
}

internal sealed class RDCoreMcpClientApp(IOptions<SdkAppOptions> options, IChildConnectionFactory connectionFactory, ILogger<RDCoreMcpClientApp> logger)
    : RDCoreClientApp(options, connectionFactory, logger)
{
    public override CoreServerComponent PlatformComponent => CoreServerComponent.ClientApp;

    // what an assistant's host is served: the tools. It runs nothing and shows no dialog.
    protected override CorePlatformClientCapabilities GetExpectedCapabilities() => new()
    {
        LanguageServer = new LanguageServerCapabilities
        {
            AssistantTools = new AssistantTools(true),
        },
    };

    // the edits the language server makes are written to the files on disk, in their own encoding.
    private readonly WorkspaceEditFiles _files = new(new System.IO.Abstractions.FileSystem(), options.Value.Environment.AnsiCodePage);

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton(_files);

    // edits of text only, in document changes; the documents stay closed, so the server reads what another editor saves, and watches the files itself.
    protected override ClientCapabilities ConfigureClientCapabilities(ClientCapabilities capabilities)
    {
        capabilities.Workspace ??= new();
        capabilities.Workspace.ApplyEdit = true;
        capabilities.Workspace.WorkspaceEdit = new WorkspaceEditCapability
        {
            DocumentChanges = true,
            ResourceOperations = new Container<ResourceOperationKind>(),
            FailureHandling = FailureHandlingKind.Abort,
        };
        return capabilities;
    }

    protected override void ConfigureHandlers(IRDCoreLSPHandlerConfigurationBuilder builder)
        => builder.WithHandler<McpApplyEditHandler>();

    protected override void Dispose(bool disposing)
    {
    }
}
