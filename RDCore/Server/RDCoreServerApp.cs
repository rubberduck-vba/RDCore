using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Services.States;
using RDCore.Server.Commands;
using RDCore.Workspace.Services;
using RDCore.Workspace.States;
using System.IO.Abstractions;

using IFile = System.IO.Abstractions.IFile;

namespace RDCore.Server;

internal class RDCoreServerApp(IServerStateProvider serverStateProvider) : ServerApp(serverStateProvider)
{
    protected override void ConfigureAppServices(IServiceCollection services)
    {
        services
            .AddSingleton<IFileSystem, FileSystem>()
            .AddSingleton<IPath, PathWrapper>()
            .AddSingleton<IFile, FileWrapper>()
            .AddSingleton<IDirectory, DirectoryWrapper>()

            .AddSingleton(provider => Info.Version!)

            .AddSingleton<IDocumentStateProvider, DocumentStateProvider>()
            .AddSingleton<IWorkspaceService, WorkspaceService>()
            .AddSingleton<IProjectFileService, ProjectFileService>()
            .AddSingleton<IWorkspaceDocumentService, WorkspaceDocumentService>()

            .AddSingleton<IEnumerable<SupportedLanguage>>(provider => [ProtocolSupportedLanguage.VBA])
            .AddSingleton<TextDocumentSelector>(provider => ProtocolSupportedLanguage.VBA.ToTextDocumentSelector())

            // register each command so the provider can resolve their dependencies:
            .AddSingleton<AddReferenceCommand>()
            .AddSingleton<RemoveReferenceCommand>()
        ;
    }
}
