using Microsoft.Extensions.Options;
using RDCore.SDK.Extensibility.Configuration;
using System.Diagnostics;

namespace RDCore.SDK.Extensibility.Client;

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
/// <param name="Options">The <c>LanguageClientSettings</c> encapsulating all the necessary parameters to correctly start and connect the LSP server process.</param>
public sealed class RDCoreLanguageServerProcess(
    IOptions<SdkAppOptions> Options) : IRDCoreLanguageServerProcess
{
    private readonly CancellationTokenSource _tokenSource = new();
    private Process? _serverProcess = default;
    private Task? _waitForExit = default;

    /// <summary>
    /// Disposes of any unmanaged resources.
    /// </summary>
    public void Dispose()
    {
        _tokenSource.Dispose();

        _serverProcess?.Dispose();
        _serverProcess = default;

        _waitForExit?.Dispose();
        _waitForExit = default;
    }

    /// <summary>
    /// Stops awaiting LSP server process exit to restart it.
    /// </summary>
    /// <remarks>
    /// This method should be invoked during the <c>Shutdown</c> LSP <em>server lifecycle</em> handler.
    /// </remarks>
    public void Shutdown()
    {
        if (_serverProcess is null)
        {
            return;
        }
    }

    /// <summary>
    /// Finds and runs the language server executable with command-line arguments mapping the specified <c>LanguageClientSettings</c>.
    /// </summary>
    public void Start()
    {
        if (_serverProcess is Process running)
        {
            // this should not be happening
            throw new LanguageServerAlreadyRunningException(running.Id);
        }

        var path = Options.Value.Platform.ServerExecutable;
        var args = CommandLine.UnParserExtensions.FormatCommandLine(CommandLine.Parser.Default, 
            new SdkAppCommandLineArgs
            {
                ClientProcessId = Options.Value.Server.ClientProcessId,
                PipeName = Options.Value.Platform.Transport.PipeConfig.PipeName,
                TraceLevel = Options.Value.Server.TraceLevel,
                Verbose = Options.Value.Server.Verbose,
                WorkspaceUri = Options.Value.Workspace.WorkspaceUri,
            });

        var info = CreateProcessStartInfo(path, args);
        var process = new Process { StartInfo = info };

        _waitForExit = process.WaitForExitAsync(_tokenSource.Token)
            .ContinueWith(t => Shutdown(), _tokenSource.Token, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private static ProcessStartInfo CreateProcessStartInfo(string validPath, string args) => new()
    {
        FileName = validPath,
        WorkingDirectory = Path.GetDirectoryName(validPath),
        Arguments = args,
        CreateNoWindow = true,
        UseShellExecute = false,

        RedirectStandardInput = false,
        RedirectStandardOutput = false,
        RedirectStandardError = false
    };
}
