using Microsoft.Extensions.Logging;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.LanguageServer.Debugging;

/// <summary>
/// The edits made while a program waits that are not applied to it, as the environment host last said (<c>rdcore/host/debug/editStatus</c>): asked after each
/// edit the host loads, and after each request that runs the program on or ends it.
/// </summary>
/// <remarks>
/// <para>
/// An edit reaches a program that waits as the analysis pass loads it, and most are applied when the program goes on. The host lists the others - picked up when
/// the program is run again, or not compiled - and the user is told of each as it is made: a diagnostic where the edited declaration is
/// (<see cref="SDK.Model.Diagnostics.RDCoreDiagnosticId.EditNotApplied"/>), and a debug adapter's <c>output</c> line.
/// </para>
/// <para>
/// A change of the list changes the findings of the modules it names, which did not change themselves: the client is asked to refresh what it shows of every
/// document (<see cref="IWorkspaceRefresh.Changed"/>), and what is pushed to it is pushed again by whoever listens (<see cref="Changed"/>). So does a program
/// that begins or ends waiting in break mode: what comes of evaluating the code is not found out while it waits, and is again once it goes on, ends or is reset.
/// </para>
/// </remarks>
internal interface IEditStatusMonitor
{
    /// <summary>What the host said last: no program waits, and nothing is listed, until it is first asked.</summary>
    HostDebugEditStatusResult Current { get; }

    /// <summary>
    /// The list changed, or the program began or ended waiting in break mode: what it was, and what it is now.
    /// </summary>
    event Action<HostDebugEditStatusResult, HostDebugEditStatusResult>? Changed;

    /// <summary>
    /// An edit was loaded while a program waited. Whether it is listed or not, the program goes on with code that may not be the code it waits in.
    /// </summary>
    event Action? LoadedWhileSuspended;

    /// <summary>
    /// Asks the host which edits are not applied to the program that waits, and says so when it is not what it said last.
    /// </summary>
    /// <param name="token">A token that cancels the request.</param>
    /// <param name="afterLoad">Whether it is asked because the host loaded an edit.</param>
    /// <returns>Whether the list changed.</returns>
    Task<bool> CheckAsync(CancellationToken token, bool afterLoad = false);
}

internal sealed class EditStatusMonitor(IProgramDebugService debugging, IWorkspaceRefresh refresh, ILogger<EditStatusMonitor> logger) : IEditStatusMonitor
{
    // one question at a time: the answers are compared in the order they were given.
    private readonly SemaphoreSlim _asking = new(1, 1);
    private HostDebugEditStatusResult _current = new();

    public HostDebugEditStatusResult Current => Volatile.Read(ref _current);

    public event Action<HostDebugEditStatusResult, HostDebugEditStatusResult>? Changed;

    public event Action? LoadedWhileSuspended;

    public async Task<bool> CheckAsync(CancellationToken token, bool afterLoad = false)
    {
        await _asking.WaitAsync(token);
        try
        {
            HostDebugEditStatusResult status;
            try
            {
                status = await debugging.EditStatusAsync(token);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // a host that cannot say is a host whose program is not known to wait: nothing is listed.
                logger.LogWarning(exception, "✏️ The environment host could not say which edits are not applied to the program that waits.");
                status = new HostDebugEditStatusResult();
            }

            if (status.Suspended && afterLoad)
            {
                LoadedWhileSuspended?.Invoke();
            }

            var previous = Current;
            if (previous.Suspended == status.Suspended && previous.Edits.SequenceEqual(status.Edits))
            {
                return false;
            }

            Volatile.Write(ref _current, status);
            if (previous.Edits.Length > 0 || status.Edits.Length > 0 || previous.Suspended != status.Suspended)
            {
                // the findings of the modules the list names, which did not change themselves, are not what they were; nor are any module's, once a program
                // waits in break mode or no longer does - what comes of evaluating the code is not found out while it waits.
                refresh.Changed();
                Changed?.Invoke(previous, status);
            }

            return true;
        }
        finally
        {
            _asking.Release();
        }
    }
}
