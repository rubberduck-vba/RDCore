using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RDCore.SDK.Server.Configuration;
using System.Diagnostics;
using System.IO.Abstractions;

namespace RDCore.SDK.Client;

/// <summary>
/// Decouples the startup sequence from an actual <c>Process</c> boundary.
/// </summary>
public interface IRDCoreServerProcess : IDisposable
{
    /// <summary>
    /// Runs a server executable with command-line arguments mapping the specified <c>LanguageClientSettings</c>.
    /// </summary>
    Task StartAsync(string relativePath, string pipeName, CancellationTokenSource tokenSource);
    /// <summary>
    /// Stops awaiting LSP server process exit to restart it.
    /// </summary>
    /// <remarks>
    /// This method should be invoked during the <c>Shutdown</c> LSP <em>server lifecycle</em> handler.
    /// </remarks>
    void Shutdown();
    int ProcessId { get; }
    /// <summary>Whether the server process has exited (or was never started).</summary>
    bool HasExited { get; }
    /// <summary>The exit code of the process once it has exited; <c>0</c> otherwise.</summary>
    int ExitCode { get; }
    /// <summary>Completes when the server process exits.</summary>
    Task WaitForExitAsync();
}

public enum CoreServerComponent
{
    /// <summary>
    /// Application is a RD-VBA runtime environment host component.
    /// </summary>
    EnvironmentHost,
    /// <summary>
    /// Application is a RDCore platform orchestration and RD-VBA language server.
    /// </summary>
    LanguageServer,
    /// <summary>
    /// Application is a parsing server component.
    /// </summary>
    ParsingServer,
    /// <summary>
    /// Application is a platform extension server component.
    /// </summary>
    Extension,
    /// <summary>
    /// Application is a LSP client.
    /// </summary>
    /// <remarks>
    /// This application type cannot be started from a server app.
    /// </remarks>
    ClientApp,
}

/// <summary>
/// Represents a <c>RDCore.SDK</c> <em> server application</em>.
/// </summary>
/// <remarks>
/// A <em>language server application</em> is any <c>RDCore.SDK</c> server platform application that can run a sidecar LSP server.
/// </remarks>
/// <param name="FileSystem">Provides an abstraction over the file system.</param>
/// <param name="Configuration">The current <see cref="IConfiguration"/> .</param>
/// <param name="Logger">A standard <see cref="ILogger"/>.</param>
public class RDCoreServerProcess(
    IFileSystem FileSystem,
    IOptions<SdkAppOptions> Options,
    ILogger<RDCoreServerProcess> Logger) : IRDCoreServerProcess
{
    private readonly CancellationTokenSource _tokenSource = new();
    private Process? _serverProcess = default;
    private Task? _waitForExit = default;

    public int ProcessId => _serverProcess?.Id ?? 0;
    public bool HasExited => _serverProcess?.HasExited ?? true;
    public int ExitCode => _serverProcess is { HasExited: true } process ? process.ExitCode : 0;
    public Task WaitForExitAsync() => _waitForExit ?? Task.CompletedTask;

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

    protected virtual void Dispose(bool disposing) 
    {
        _serverProcess?.Dispose();
    }

    public void Shutdown() => _serverProcess?.Kill();

    public Task StartAsync(string relativePath, string pipeName, CancellationTokenSource tokenSource)
    {
        if (_serverProcess is Process running && !running.HasExited)
        {
            throw new ServerAlreadyRunningException(running.Id);
        }
        // a previous run has ended; allow a restart (see ChildConnection restart-with-backoff).
        _serverProcess?.Dispose();
        _serverProcess = null;

        var fullPath = FileSystem.Path.Combine(
            FileSystem.Directory.GetParent(FileSystem.Directory.GetCurrentDirectory())!.FullName, 
            relativePath.Replace('/', '\\'));
        var workspace = Options.Value.Workspace.WorkspaceUri;
        var trace = LogLevel.Trace; // Options.Value.Server.TraceLevel;
        var verbose = true; //Options.Value.Server.Verbose;

        var info = CreateProcessStartInfo(fullPath, $"-p {Environment.ProcessId} -n {pipeName} -w \"{workspace}\" -t {trace} {(verbose ? "-v" : null)}");
        if (Logger.IsEnabled(LogLevel.Debug))
        {
            Logger.LogDebug("[ProcessStartInfo]\n\tPath:'{path}'\n\tWorkingDirectory:'{workdir}'\n\tArguments:'{args}'", fullPath, info.WorkingDirectory, info.Arguments);
        }

        _serverProcess = Process.Start(info) ?? throw new ServerNotFoundException(fullPath);
        _waitForExit = _serverProcess.WaitForExitAsync(_tokenSource.Token);

        // no fixed start-up delay: the caller races the transport connect against WaitForExitAsync().
        // only guard against a process that fails before it is even scheduled.
        if (_serverProcess.HasExited)
        {
            throw new ServerProtocolSdkException($"Server process exited immediately with code {_serverProcess.ExitCode}.");
        }

        return Task.CompletedTask;
    }

    private ProcessStartInfo CreateProcessStartInfo(string validPath, string args) => new()
    {
        FileName = validPath,
        WorkingDirectory = FileSystem.Path.GetDirectoryName(validPath),
        Arguments = args,
        CreateNoWindow = true,
        UseShellExecute = false,

        RedirectStandardInput = false,
        RedirectStandardOutput = false,
        RedirectStandardError = false
    };
}
