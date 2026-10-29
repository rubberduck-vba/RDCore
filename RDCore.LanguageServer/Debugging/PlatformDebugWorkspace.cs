using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Client;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services.States;
using RDCore.SDK.Workspace;
using RDCore.SDK.Model.Symbols;

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
    IWorkspaceReloadService reload,
    System.IO.Abstractions.IFileSystem fileSystem,
    IOptions<SdkAppOptions> options,
    ILogger<PlatformDebugWorkspace> logger) : IDebugWorkspace
{
    private readonly TaskCompletionSource<string> _lost = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _lifetime = new();

    // which file is which module, and which module is which file: made anew, and swapped whole, as the program goes on - the requests of a client that read them
    // meanwhile read one or the other, never half of each.
    private sealed record class Maps(IReadOnlyDictionary<string, (string Module, Uri Document)> ByPath, IReadOnlyDictionary<string, string> ByModule);

    private Maps _maps = new(new Dictionary<string, (string, Uri)>(), new Dictionary<string, string>());

    // the paths of the files of a Windows workspace are not case-sensitive, and a client is no more careful than the file system is about which case it uses.
    private static StringComparer PathComparer => WorkspacePaths.Comparer;

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

    // a module is named by its source (Attribute VB_Name), the name of the file when the source does not say - as the symbols of the workspace are defined. The
    // modules that are named otherwise than they were, by the file they are.
    private IReadOnlyList<(string Was, string Is)> MapDocuments()
    {
        var (byPath, byModule) = (new Dictionary<string, (string Module, Uri Document)>(PathComparer), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        foreach (var document in documents.GetAllDocuments())
        {
            var uri = document.Id.Uri.ToUri();
            if (!parsing.TryGetCached(uri, out var parse) || parse.SyntaxTree is null)
            {
                continue;
            }

            var module = parse.SyntaxTree.GetDeclaredName() ?? document.Name;
            var path = Path.GetFullPath(document.Id.Uri.GetFileSystemPath());
            byPath[path] = (module, uri);
            byModule[module] = path;
        }

        var was = Interlocked.Exchange(ref _maps, new Maps(byPath, byModule));
        return [.. byPath
            .Where(entry => was.ByPath.TryGetValue(entry.Key, out var before) && !string.Equals(before.Module, entry.Value.Module, StringComparison.OrdinalIgnoreCase))
            .Select(entry => (was.ByPath[entry.Key].Module, entry.Value.Module))];
    }

    public bool TryGetModule(string path, out string moduleName)
    {
        // a client writes a path as its platform does, and the workspace has them as the file system does.
        var found = Volatile.Read(ref _maps).ByPath.TryGetValue(Path.GetFullPath(path), out var entry);
        moduleName = entry.Module ?? string.Empty;
        return found;
    }

    public bool TryGetPath(string moduleName, out string path)
    {
        var found = Volatile.Read(ref _maps).ByModule.TryGetValue(moduleName, out var file);
        path = file ?? string.Empty;
        return found;
    }

    public async Task<ExecuteSessionResult> StartAsync(string moduleName, string entryPoint, CancellationToken token)
    {
        var maps = Volatile.Read(ref _maps);
        if (!maps.ByModule.TryGetValue(moduleName, out var path) || !maps.ByPath.TryGetValue(path, out var entry)
            || !parsing.TryGetCached(entry.Document, out var parse))
        {
            return new ExecuteSessionResult { Outcome = ExecutionOutcome.NotFound, ErrorMessage = string.Format(DebuggerMessages.NotAModuleOfTheWorkspace, moduleName) };
        }

        var environment = orchestration.RuntimeEnvironment;
        await environment.WaitForReadyAsync(token);

        // a program run again has the modules as they are named now.
        _renamed = null;

        // the symbols of the workspace were defined under this address; the same declaration addressed another way is another declaration.
        var moduleUri = SymbolUri.Module(documents.WorkspaceRoot, moduleName);
        return await environment.SendRequestAsync<HostExecuteParams, ExecuteSessionResult>(new HostExecuteParams
        {
            Json = PlatformJson.Serialize(new HostExecutePayload(moduleUri, parse)),
            ModuleName = moduleName,
            EntryPoint = entryPoint,
            Debug = true,
            StreamOutput = true,
        }, token);
    }

    public async Task<string?> ReloadChangedAsync(CancellationToken token)
    {
        WorkspaceDocument[] files = [.. documents.GetAllDocuments().Where(document => document.IsFile)];

        // a file that is gone, locked or can't be read is no code to go on with: the program waits on the code it had, which it is told.
        foreach (var file in files)
        {
            var path = file.Id.Uri.GetFileSystemPath();
            try
            {
                using var readable = fileSystem.File.OpenRead(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return string.Format(DebuggerMessages.ResumeRefusedForUnreadable, Path.GetFileName(path), exception.Message);
            }
        }

        // the files are read and told apart from what was loaded by their text: one that was saved as it was is nothing changed. What another request had loaded,
        // and is loading still, is waited for too.
        var reloaded = files.Length == 0 ? null : await reload.ReloadFilesAsync([.. files.Select(file => file.Id.Uri.ToUri())], token);
        await reload.WhenIdleAsync(token);

        // a module the host could not define or load, as its text is now, holds the code it had: it is loaded again, since nothing else would.
        if (symbols.Unsynced.Count > 0 && reloaded is not { IsUnchanged: false })
        {
            await symbols.SyncWorkspaceAsync(token, replace: true);
        }

        if (symbols.Unsynced is { Count: > 0 } unsynced)
        {
            return string.Format(DebuggerMessages.ResumeRefusedForUnloaded,
                string.Join("\n", unsynced.Select(module => "  " + string.Format(DebuggerMessages.EditNamed, module.Key, module.Value))));
        }

        // a module named otherwise by its source now is a module the program does not have - its breakpoints, its frames are of the name it had - until it is run
        // again, whatever is saved meanwhile.
        if (reloaded is { IsUnchanged: false } && MapDocuments() is [var first, ..])
        {
            _renamed ??= first;
        }

        return _renamed is var (was, now) ? string.Format(DebuggerMessages.ResumeRefusedForRenamed, was, now) : null;
    }

    private (string Was, string Is)? _renamed;

    public async Task CloseAsync()
    {
        await _lifetime.CancelAsync();
        var components = new[] { orchestration.ParsingService, orchestration.RuntimeEnvironment }.Where(component => component is not null);
        await Task.WhenAll(components.Select(component => component.ShutdownAsync()));
    }
}
