using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;

namespace RDCore.LanguageServer.Server.Handlers.Workspace;

/// <summary>
/// The server's own watch of the workspace's files, for a client that does not say it watches them: what changes outside the editor is made the workspace's
/// as when a client says so (<see cref="IWatchedFileChanges"/>).
/// </summary>
/// <remarks>
/// <para>
/// The client owns the file system. A client that says it watches files (<c>workspace.didChangeWatchedFiles</c>) is not watched for, and neither is any client
/// when <see cref="SdkWorkspaceOptions.EnableFileSystemWatchers"/> is <see langword="false"/>.
/// </para>
/// <para>
/// What is watched is the workspace's <c>.rdproj</c> and the source files of every language the platform serves, under the workspace root; what a folder whose
/// name starts with a dot holds (<c>.git</c>, <c>.vscode</c>) is not. A save is several events of a file system, and a <c>git checkout</c> many: the events
/// of a moment are gathered, and said together once none came for <see cref="Settle"/>.
/// </para>
/// </remarks>
internal interface IWorkspaceFileSystemWatcher : IDisposable
{
    /// <summary>
    /// Watches the workspace's files, unless the client watches them or the setting says never.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root.</param>
    /// <param name="clientWatches">Whether the client said it watches files.</param>
    /// <param name="server">The server, which tells its client what changed.</param>
    /// <returns>Whether the server watches.</returns>
    bool Start(string workspaceRoot, bool clientWatches, ILanguageServerFacade server);
}

internal sealed class WorkspaceFileSystemWatcher(
    IFileSystem fileSystem,
    IOptions<SdkAppOptions> options,
    IWatchedFileChanges changes,
    ILogger<WorkspaceFileSystemWatcher> logger) : IWorkspaceFileSystemWatcher
{
    /// <summary>The extensions of the source files of every language the platform serves, without their dot.</summary>
    public static IReadOnlyList<string> SourceExtensions { get; } = [.. SupportedLanguages.All
        .SelectMany(language => language.FileTypes)
        .Select(fileType => fileType.TrimStart('*', '.').ToLowerInvariant())
        .Distinct()];

    private static StringComparison PathComparison => WorkspacePaths.Comparison;

    private readonly object _gate = new();
    private readonly Dictionary<string, FileChangeType> _pending = new(WorkspacePaths.Comparer);
    private IFileSystemWatcher? _watcher;
    private ILanguageServerFacade? _server;
    private string _root = string.Empty;
    private CancellationTokenSource? _settling;

    /// <summary>How long no event comes before the events gathered are said.</summary>
    internal TimeSpan Settle { get; init; } = TimeSpan.FromMilliseconds(300);

    /// <summary>What waits for <see cref="Settle"/>: the clock, or what a test opens when it says the events settled.</summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    /// <summary>What was last said, for a test to wait on.</summary>
    internal Task Said { get; private set; } = Task.CompletedTask;

    public bool Start(string workspaceRoot, bool clientWatches, ILanguageServerFacade server)
    {
        if (clientWatches)
        {
            logger.LogInformation("👀 The client watches the workspace's files; the server watches nothing.");
            return false;
        }

        if (!options.Value.Workspace.EnableFileSystemWatchers)
        {
            logger.LogInformation("👀 The client does not watch the workspace's files, and EnableFileSystemWatchers is off: a file changed outside the editor is read when the server next starts.");
            return false;
        }

        _root = fileSystem.Path.GetFullPath(workspaceRoot);
        _server = server;
        try
        {
            var watcher = fileSystem.FileSystemWatcher.New(_root);
            watcher.IncludeSubdirectories = true;
            watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size;
            watcher.Created += (_, e) => OnFileSystemEvent(e.FullPath, FileChangeType.Created);
            watcher.Changed += (_, e) => OnFileSystemEvent(e.FullPath, FileChangeType.Changed);
            watcher.Deleted += (_, e) => OnFileSystemEvent(e.FullPath, FileChangeType.Deleted);
            watcher.Renamed += (_, e) =>
            {
                OnFileSystemEvent(e.OldFullPath, FileChangeType.Deleted);
                OnFileSystemEvent(e.FullPath, FileChangeType.Created);
            };
            watcher.Error += (_, e) => logger.LogWarning(e.GetException(), "⚠️ The watch of the workspace's files lost events; what changed then is read when the server next starts.");
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or PlatformNotSupportedException or NotImplementedException)
        {
            logger.LogWarning(exception, "⚠️ The workspace's files could not be watched; a file changed outside the editor is read when the server next starts.");
            return false;
        }

        logger.LogInformation("👀 The client does not watch the workspace's files: the server watches them.");
        return true;
    }

    /// <summary>
    /// An event of the file system: a file under the workspace root was created, changed or deleted.
    /// </summary>
    /// <remarks>
    /// What the watch is told, and what a test tells it. The events of a file are one change: the last says what the file is now.
    /// </remarks>
    internal void OnFileSystemEvent(string path, FileChangeType type)
    {
        if (_server is null || !IsWatched(path))
        {
            return;
        }

        CancellationToken settled;
        lock (_gate)
        {
            _pending[path] = _pending.TryGetValue(path, out var earlier) && earlier is FileChangeType.Created && type is FileChangeType.Changed ? earlier : type;
            _settling?.Cancel();
            _settling?.Dispose();
            _settling = new CancellationTokenSource();
            settled = _settling.Token;
        }

        Said = SayAsync(settled);
    }

    private async Task SayAsync(CancellationToken settled)
    {
        try
        {
            await Delay(Settle, settled);
        }
        catch (OperationCanceledException)
        {
            // another event came: it is said with this one, once the events settle.
            return;
        }

        List<FileEvent> said;
        lock (_gate)
        {
            if (settled.IsCancellationRequested)
            {
                return;
            }

            said = [.. _pending.Select(change => new FileEvent { Uri = DocumentUri.FromFileSystemPath(change.Key), Type = change.Value })];
            _pending.Clear();
        }

        await changes.ChangedAsync(said, _server!);
    }

    // the workspace's project file, and a source file under the root that no dot folder holds.
    private bool IsWatched(string path)
    {
        var relative = fileSystem.Path.GetRelativePath(_root, path).Replace('\\', '/');
        if (relative.StartsWith("../", StringComparison.Ordinal) || relative == ".." || fileSystem.Path.IsPathRooted(relative))
        {
            return false;
        }

        if (string.Equals(relative, ProjectFile.FileName, PathComparison))
        {
            return true;
        }

        return !relative.Split('/').Any(segment => segment.StartsWith('.'))
            && SourceExtensions.Any(extension => relative.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _settling?.Cancel();
            _pending.Clear();
        }

        _watcher?.Dispose();
        _watcher = null;
    }
}
