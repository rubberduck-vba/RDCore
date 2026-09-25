using Microsoft.Extensions.Hosting;
using System.Runtime.InteropServices;

namespace RDCore.CLI.App.Repl;

/// <summary>
/// The host lifetime an interactive shell needs: one that leaves the break keys alone.
/// </summary>
/// <remarks>
/// The generic host's default <c>ConsoleLifetime</c> stops the application on <c>SIGINT</c> and
/// <c>SIGQUIT</c> — <kbd>Ctrl</kbd>+<kbd>C</kbd> and <kbd>Ctrl</kbd>+<kbd>Break</kbd> on Windows. In
/// a shell those are the RUN/STOP key: they interrupt what is running and return to the prompt, and
/// <c>EXIT</c> is how a session ends.
/// <para>
/// <c>SIGTERM</c> is still honoured, and must be: it is how the process is asked to go away (a
/// parent tearing the tree down, a shutting-down machine), and answering it is what gives the
/// language server — and every process it owns — a graceful shutdown instead of an abrupt orphaning.
/// </para>
/// </remarks>
/// <param name="applicationLifetime">The application lifetime a terminate signal stops.</param>
internal sealed class ReplHostLifetime(IHostApplicationLifetime applicationLifetime) : IHostLifetime, IDisposable
{
    private PosixSignalRegistration? _terminate;

    public Task WaitForStartAsync(CancellationToken cancellationToken)
    {
        _terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnTerminate);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void OnTerminate(PosixSignalContext context)
    {
        // shut down in order rather than dying where we stand; the host's own shutdown timeout bounds it.
        context.Cancel = true;
        applicationLifetime.StopApplication();
    }

    public void Dispose() => _terminate?.Dispose();
}
