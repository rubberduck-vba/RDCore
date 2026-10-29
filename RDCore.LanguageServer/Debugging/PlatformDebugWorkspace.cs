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
        _baseline = BaselineOf(Volatile.Read(ref _maps));
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

        _ = Interlocked.Exchange(ref _maps, new Maps(byPath, byModule));
    }

    // what the program had as it was started, or as it last went on. A file listed and missing, or a module that could not be loaded, as the program was started
    // is no edit - the program ran without it - and is not why it can't go on: what is refused is what is so since.
    private sealed record class Baseline(IReadOnlySet<string> Readable, IReadOnlySet<string> Unloaded, IReadOnlyDictionary<string, string> Names);

    private Baseline _baseline = new(new HashSet<string>(), new HashSet<string>(), new Dictionary<string, string>());

    // a load that failed or was stopped left the host with what nothing says: it is loaded again before the program goes on.
    private bool _loadFailed;

    // the files of the project the host runs: the modules of another project are no code of the program.
    private WorkspaceDocument[] ProgramFiles() => [.. documents.GetProjectDocuments(documents.WorkspaceRoot).Where(document => document.IsFile)];

    private static string FullPath(WorkspaceDocument file) => Path.GetFullPath(file.Id.Uri.GetFileSystemPath());

    private string? Unreadable(WorkspaceDocument file)
    {
        try
        {
            using var readable = fileSystem.File.OpenRead(file.Id.Uri.GetFileSystemPath());
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return exception.Message;
        }
    }

    // what the host does not have as it is now, and why: a module it could not define or load, and a file with no parse of its text - the parser gave none, or
    // was not asked again after it failed.
    private Dictionary<string, string> Unloaded(IEnumerable<WorkspaceDocument> files)
    {
        var unloaded = new Dictionary<string, string>(symbols.Unsynced, StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (!parsing.TryGetCached(file.Id.Uri.ToUri(), file.Version, out var parse) || parse.SyntaxTree is null)
            {
                var why = parse?.SyntaxErrors.FirstOrDefault()?.Description;
                unloaded.TryAdd(Path.GetFileName(file.Id.Uri.GetFileSystemPath()), string.IsNullOrEmpty(why) ? DebuggerMessages.ModuleNotParsed : why);
            }
        }

        return unloaded;
    }

    private Baseline BaselineOf(Maps maps, IReadOnlyDictionary<string, string>? names = null)
    {
        var files = ProgramFiles();
        return new Baseline(
            files.Where(file => Unreadable(file) is null).Select(FullPath).ToHashSet(PathComparer),
            Unloaded(files).Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            names ?? maps.ByPath.ToDictionary(entry => entry.Key, entry => entry.Value.Module, PathComparer));
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

        // a program run again has the modules as they are named now, and what is loaded now.
        Volatile.Write(ref _baseline, BaselineOf(maps));

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
        var baseline = Volatile.Read(ref _baseline);
        var files = new List<WorkspaceDocument>();

        // a file that is gone, locked or can't be read is no code to go on with: the program waits on the code it had, which it is told. One that could not be
        // read when the program started is not read now either: the program has none of its code.
        foreach (var file in ProgramFiles())
        {
            if (Unreadable(file) is not { } why)
            {
                files.Add(file);
            }
            else if (baseline.Readable.Contains(FullPath(file)))
            {
                return string.Format(DebuggerMessages.ResumeRefusedForUnreadable, Path.GetFileName(file.Id.Uri.GetFileSystemPath()), why);
            }
        }

        // the files are read and told apart from what was loaded by their text: one that was saved as it was is nothing changed. What another request had loaded,
        // and is loading still, is waited for too. A batch that loaded anything defined the whole workspace again.
        WorkspaceReloadResult? reloaded;
        try
        {
            reloaded = files.Count == 0 ? null : await reload.ReloadFilesAsync([.. files.Select(file => file.Id.Uri.ToUri())], token);
            await reload.WhenIdleAsync(token);
        }
        catch
        {
            _loadFailed = true;
            throw;
        }

        if (reloaded is { IsUnchanged: false })
        {
            _loadFailed = false;
        }

        // the documents as the load left them: a file that was saved changed is another version of its document.
        var read = files.Select(FullPath).ToHashSet(PathComparer);
        files = [.. ProgramFiles().Where(file => read.Contains(FullPath(file)))];

        // what the host could not define or load since, as its text is now, holds the code it had - and so does what a load that failed or was stopped did not
        // reach: it is loaded again, since nothing else would.
        var unloaded = Unloaded(files);
        if ((_loadFailed || unloaded.Keys.Any(name => !baseline.Unloaded.Contains(name))) && reloaded is not { IsUnchanged: false })
        {
            try
            {
                foreach (var file in files.Where(file => !parsing.TryGetCached(file.Id.Uri.ToUri(), file.Version, out var parse) || parse.SyntaxTree is null))
                {
                    _ = await parsing.ParseDocumentAsync(file.Id.Uri.ToUri(), token);
                }

                await symbols.SyncWorkspaceAsync(token, replace: true);
            }
            catch
            {
                _loadFailed = true;
                throw;
            }

            _loadFailed = false;
            unloaded = Unloaded(files);
        }

        MapDocuments();
        if (unloaded.Where(module => !baseline.Unloaded.Contains(module.Key)).ToList() is { Count: > 0 } since)
        {
            return string.Format(DebuggerMessages.ResumeRefusedForUnloaded,
                string.Join("\n", since.Select(module => "  " + string.Format(DebuggerMessages.EditNamed, module.Key, module.Value))));
        }

        // a module named otherwise by its source than it was when the program started is a module the program does not have - its breakpoints, its frames are
        // of the name it had - until it is run again, whatever is saved meanwhile; named back, it is the module the program has.
        var maps = Volatile.Read(ref _maps);
        foreach (var (path, was) in baseline.Names)
        {
            if (maps.ByPath.TryGetValue(path, out var now) && !string.Equals(was, now.Module, StringComparison.OrdinalIgnoreCase))
            {
                return string.Format(DebuggerMessages.ResumeRefusedForRenamed, was, now.Module);
            }
        }

        // the program goes on with what is loaded now: what is refused next is what changes from it.
        Volatile.Write(ref _baseline, BaselineOf(maps, baseline.Names));
        return null;
    }

    public async Task CloseAsync()
    {
        await _lifetime.CancelAsync();
        var components = new[] { orchestration.ParsingService, orchestration.RuntimeEnvironment }.Where(component => component is not null);
        await Task.WhenAll(components.Select(component => component.ShutdownAsync()));
    }
}
