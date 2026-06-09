using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.IO.Abstractions;

namespace RDCore.SDK.Client;

/// <summary>
/// Decouples the startup sequence from an actual <c>Process</c> boundary.
/// </summary>
public interface IRDCoreLanguageServerProcess : IDisposable
{
    /// <summary>
    /// Finds and runs the language server executable with command-line arguments mapping the specified <c>LanguageClientSettings</c>.
    /// </summary>
    void Start();
    /// <summary>
    /// Stops awaiting LSP server process exit to restart it.
    /// </summary>
    /// <remarks>
    /// This method should be invoked during the <c>Shutdown</c> LSP <em>server lifecycle</em> handler.
    /// </remarks>
    void Shutdown();
}

/// <summary>
/// Represents a <c>RDCore.SDK</c> <em>language server application</em>.
/// </summary>
/// <remarks>
/// A <em>language server application</em> is any <c>RDCore.SDK</c> server platform application that can run a sidecar LSP server.
/// </remarks>
/// <param name="FileSystem">Provides an abstraction over the file system.</param>
/// <param name="Options">The <c>LanguageClientSettings</c> encapsulating all the necessary parameters to correctly start and connect the LSP server process.</param>
public abstract class RDCoreLanguageServerProcess(IFileSystem FileSystem, IOptions<LanguageClientSettings> Options) : IRDCoreLanguageServerProcess
{
    private readonly CancellationTokenSource _tokenSource = new();
    private Process? _serverProcess = default;
    private Task? _waitForExit = default;

    /// <summary>
    /// An event that fires when the LSP language server process exits, signaling that the server app should be restarted.
    /// </summary>
    /// <remarks>
    /// This event is de-registered on <c>Shutdown</c>.
    /// </remarks>
    public event EventHandler? LanguageServerProcessDidExit;
    protected void OnLanguageServerProcessDidExit() => LanguageServerProcessDidExit?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        _tokenSource.Dispose();

        _serverProcess?.Dispose();
        _serverProcess = default;

        _waitForExit?.Dispose();
        _waitForExit = default;

        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing) { }

    public void Shutdown()
    {
        if (_serverProcess is null)
        {
            return;
        }

        LanguageServerProcessDidExit = null;
    }

    public void Start()
    {
        if (_serverProcess is Process running)
        {
            // this should not be happening
            throw new LanguageServerAlreadyRunningException(running.Id);
        }

        var path = Options.Value.ServerStartupSettings.LanguageServer;
        if (!FileSystem.File.Exists(path))
        {
            throw new LanguageServerNotFoundException(path);
        }

        var info = CreateProcessStartInfo(path, [.. Options.Value.ToServerStartupArgs().Select(arg => $"{arg}")]);
        var process = new Process { StartInfo = info };

        _waitForExit = process.WaitForExitAsync(_tokenSource.Token)
            .ContinueWith(t => Shutdown(), _tokenSource.Token, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private ProcessStartInfo CreateProcessStartInfo(string validPath, params string[] args) => new()
    {
        FileName = validPath,
        WorkingDirectory = Path.GetDirectoryName(validPath),
        Arguments = string.Join(' ', args),
        CreateNoWindow = Options.Value.ServerStartupSettings.CreateNoWindow,
        UseShellExecute = false,

        RedirectStandardInput = false,
        RedirectStandardOutput = Options.Value.ServerStartupSettings.RedirectStdOut,
        RedirectStandardError = Options.Value.ServerStartupSettings.RedirectStdErr
    };
}
