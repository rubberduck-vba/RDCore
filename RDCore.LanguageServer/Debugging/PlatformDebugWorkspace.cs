using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Client;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services.States;

namespace RDCore.LanguageServer.Debugging;

/// <summary>
/// The workspace of a debug adapter, on a platform of its own: the same components a language server brings up, owned by a process that is not a language server.
/// </summary>
internal sealed class PlatformDebugWorkspace(
    IServiceProvider services,
    IServerStateProvider serverState,
    IPlatformOrchestrationService orchestration,
    IWorkspaceService workspace,
    IWorkspaceDocumentService documents,
    IParsingClientService parsing,
    ISymbolSyncService symbols,
    IOptions<SdkAppOptions> options,
    ILogger<PlatformDebugWorkspace> logger) : IDebugWorkspace
{
    private readonly TaskCompletionSource<string> _lost = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, (string Module, Uri Document)> _byPath = new(PathComparer);
    private readonly Dictionary<string, string> _byModule = new(StringComparer.OrdinalIgnoreCase);

    // the paths of the files of a Windows workspace are not case-sensitive, and a client is no more careful than the file system is about which case it uses.
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public Task<string> Lost => _lost.Task;

    public async Task OpenAsync(CancellationToken token)
    {
        orchestration.RegisterCorePlatformComponents();

        var root = options.Value.Workspace.WorkspaceUri;
        try
        {
            // the workspace may only be loaded while the server initializes.
            serverState.OnInitialize();
            await workspace.LoadAsync(root);
            serverState.OnInitialized();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(string.Format(DebuggerMessages.WorkspaceCouldNotBeLoaded, root, exception.Message), exception);
        }

        var parsingServer = BringUpAsync(DebuggerMessages.ComponentParsingServer, orchestration.ParsingService);
        var environmentHost = BringUpAsync(DebuggerMessages.ComponentEnvironmentHost, orchestration.RuntimeEnvironment);
        await Task.WhenAll(parsingServer, environmentHost).WaitAsync(token);

        await parsing.ParseWorkspaceAsync(token);
        await symbols.SyncWorkspaceAsync(token);
        MapDocuments();
    }

    // the component is started and waited for; a component that is lost for good afterwards ends the debugging, since a program cannot be debugged without it.
    private async Task BringUpAsync(string label, IRDCoreClientApp component)
    {
        await component.RunAsync(services, []);
        await component.WaitForReadyAsync(_lifetime.Token);
        logger.LogInformation("✅ {label} is ready", label);

        _ = WatchAsync(label, component);
    }

    private async Task WatchAsync(string label, IRDCoreClientApp component)
    {
        try
        {
            await component.WaitForTerminalAsync().WaitAsync(_lifetime.Token);
            logger.LogCritical("❌ {label} is unrecoverable.", label);
            _ = _lost.TrySetResult(label);
        }
        catch (OperationCanceledException)
        {
        }
    }

    // a module is named by its source (Attribute VB_Name), the name of the file when the source does not say - as the symbols of the workspace are defined.
    private void MapDocuments()
    {
        _byPath.Clear();
        _byModule.Clear();
        foreach (var document in documents.GetAllDocuments())
        {
            var uri = document.Id.Uri.ToUri();
            if (!parsing.TryGetCached(uri, out var parse) || parse.SyntaxTree is null)
            {
                continue;
            }

            var module = parse.SyntaxTree.GetDeclaredName() ?? document.Name;
            var path = Path.GetFullPath(document.Id.Uri.GetFileSystemPath());
            _byPath[path] = (module, uri);
            _byModule[module] = path;
        }
    }

    public bool TryGetModule(string path, out string moduleName)
    {
        // a client writes a path as its platform does, and the workspace has them as the file system does.
        var found = _byPath.TryGetValue(Path.GetFullPath(path), out var entry);
        moduleName = entry.Module ?? string.Empty;
        return found;
    }

    public bool TryGetPath(string moduleName, out string path)
    {
        var found = _byModule.TryGetValue(moduleName, out var file);
        path = file ?? string.Empty;
        return found;
    }

    public async Task<ExecuteSessionResult> StartAsync(string moduleName, string entryPoint, CancellationToken token)
    {
        if (!_byModule.TryGetValue(moduleName, out var path) || !_byPath.TryGetValue(path, out var entry)
            || !parsing.TryGetCached(entry.Document, out var parse))
        {
            return new ExecuteSessionResult { Outcome = ExecutionOutcome.NotFound, ErrorMessage = string.Format(DebuggerMessages.NotAModuleOfTheWorkspace, moduleName) };
        }

        var environment = orchestration.RuntimeEnvironment;
        await environment.WaitForReadyAsync(token);

        // the symbols of the workspace were defined under this address; the same declaration addressed another way is another declaration.
        var moduleUri = new UriBuilder(new Uri(documents.WorkspaceRoot)) { Fragment = moduleName }.Uri;
        return await environment.SendRequestAsync<HostExecuteParams, ExecuteSessionResult>(new HostExecuteParams
        {
            Json = PlatformJson.Serialize(new HostExecutePayload(moduleUri, parse)),
            ModuleName = moduleName,
            EntryPoint = entryPoint,
            Debug = true,
            StreamOutput = true,
        }, token);
    }

    public async Task CloseAsync()
    {
        await _lifetime.CancelAsync();
        var components = new[] { orchestration.ParsingService, orchestration.RuntimeEnvironment }.Where(component => component is not null);
        await Task.WhenAll(components.Select(component => component.ShutdownAsync()));
    }
}
