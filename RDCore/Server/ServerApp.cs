using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Configuration;
using RDCore.Server.Commands;
using RDCore.Server.Services;
using RDCore.Server.States;
using RDCore.Workspace.Services;
using RDCore.Workspace.States;
using System.IO.Abstractions;
using System.Reflection;

namespace RDCore.Server;

internal class ServerApp(ServerOptions options) : IDisposable
{
    public static AssemblyName Info { get; } = typeof(ServerApp).Assembly.GetName();

    private ServerOptions Options { get; } = options;
    private CancellationTokenSource CancellationTokenSource { get; } = new CancellationTokenSource();

    public async Task RunAsync()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var app = provider.GetRequiredService<ILanguageServerApp>();
        await app.RunAsync(provider);
    }

    public void Dispose()
    {
        CancellationTokenSource.Dispose();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services
            .AddSingleton<System.IO.Abstractions.IPath, PathWrapper>()
            .AddSingleton<System.IO.Abstractions.IFile, FileWrapper>()
            .AddSingleton<System.IO.Abstractions.IDirectory, DirectoryWrapper>()
            .AddSingleton(provider => CancellationTokenSource)
            .AddSingleton(provider => Options)
            .AddSingleton(provider => Info.Version!)
            .AddSingleton<ILanguageServerApp, LanguageServerApp>()
            .AddSingleton<IServerStateProvider, ServerStateProvider>()
            .AddSingleton<IDocumentStateProvider, DocumentStateProvider>()
            .AddSingleton<IHealthCheckService, HealthCheckService>()
            .AddSingleton<IWorkspaceService, WorkspaceService>()
            .AddSingleton<IProjectFileService, ProjectFileService>()
            .AddSingleton<IWorkspaceDocumentService, WorkspaceDocumentService>()

            .AddSingleton<AddReferenceCommand>()
            .AddSingleton<RemoveReferenceCommand>()

            .AddSingleton<IEnumerable<ServerCommand>>(provider =>
            [
                provider.GetRequiredService<AddReferenceCommand>(),
                provider.GetRequiredService<RemoveReferenceCommand>(),
            ])

            .AddSingleton<IEnumerable<SupportedLanguage>>(provider => [SupportedLanguage.VBA])
            .AddSingleton<TextDocumentSelector>(provider => SupportedLanguage.VBA.ToTextDocumentSelector())

            .AddLogging(ConfigureLogging);
    }

    private void ConfigureLogging(ILoggingBuilder builder)
    {
        if (Options.Verbose)
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
