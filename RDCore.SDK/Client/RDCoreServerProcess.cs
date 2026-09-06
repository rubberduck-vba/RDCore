using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RDCore.SDK.Platform;
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
    /// <param name="hostMode">When <c>true</c>, sets <c>RDCORE_MODE=host</c> in the child environment (rdc.exe runs as the environment host).</param>
    Task StartAsync(string relativePath, string pipeName, CancellationTokenSource tokenSource, bool hostMode = false);
    /// <summary>
    /// Stops awaiting LSP server process exit to restart it.
    /// </summary>
    /// <remarks>
    /// This method should be invoked during the <c>Shutdown</c> LSP <em>server lifecycle</em> handler.
    /// </remarks>
    void Shutdown();

    /// <summary>
    /// The operating-system identifier of the server process.
    /// </summary>
    int ProcessId { get; }

    /// <summary>
    /// Whether the server process has exited (or was never started).
    /// </summary>
    bool HasExited { get; }

    /// <summary>
    /// The exit code of the process once it has exited; <c>0</c> otherwise.
    /// </summary>
    int ExitCode { get; }

    /// <summary>
    /// Completes when the server process exits.
    /// </summary>
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
    IPlatformEnvironment PlatformEnvironment,
    IOptions<SdkAppOptions> Options,
    ILogger<RDCoreServerProcess> Logger) : IRDCoreServerProcess
{
    private readonly CancellationTokenSource _tokenSource = new();
    private Process? _serverProcess = default;
    private Task? _waitForExit = default;
    private bool _disposed;

    public int ProcessId => _serverProcess?.Id ?? 0;
    public bool HasExited => _serverProcess?.HasExited ?? true;
    public int ExitCode => _serverProcess is { HasExited: true } process ? process.ExitCode : 0;
    public Task WaitForExitAsync() => _waitForExit ?? Task.CompletedTask;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        // completes the WaitForExitAsync task so nothing is left awaiting a dead process.
        if (!_tokenSource.IsCancellationRequested)
        {
            _tokenSource.Cancel();
        }
        _tokenSource.Dispose();

        // a Task is not disposed — that throws while it is still running and buys nothing.
        _waitForExit = default;

        _serverProcess?.Dispose();
        _serverProcess = default;

        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing) { }

    public void Shutdown() => _serverProcess?.Kill();

    public const string ModeEnvironmentVariable = "RDCORE_MODE";

    public Task StartAsync(string relativePath, string pipeName, CancellationTokenSource tokenSource, bool hostMode = false)
    {
        if (_serverProcess is Process running && !running.HasExited)
        {
            throw new ServerAlreadyRunningException(running.Id);
        }
        // a previous run has ended; allow a restart (see ChildConnection restart-with-backoff).
        _serverProcess?.Dispose();
        _serverProcess = null;

        var fullPath = PlatformEnvironment.Resolve(relativePath);
        var workspace = Options.Value.Workspace.WorkspaceUri;
        var trace = LogLevel.Trace; // Options.Value.Server.TraceLevel;
        var verbose = true; //Options.Value.Server.Verbose;

        var info = CreateProcessStartInfo(fullPath, $"-p {Environment.ProcessId} -n {pipeName} -w \"{workspace}\" -t {trace} {(verbose ? "-v" : null)}");
        if (hostMode)
        {
            info.Environment[ModeEnvironmentVariable] = "host";
        }
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
