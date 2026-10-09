using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.LanguageServer.Debugging;

/// <summary>
/// Where the lines a program prints arrive from the environment host (<see cref="HostOutputNotification"/>), for whoever shows them.
/// </summary>
/// <remarks>
/// A notification and the response to the request that runs the program travel apart, and the response can be handled first. The relay counts the lines that arrived, so
/// that whoever got the response (<see cref="ExecuteSessionResult.StreamedLines"/>) can wait for the lines that came before it instead of saying what the program did
/// before what it printed.
/// </remarks>
internal interface IHostOutputRelay
{
    /// <summary>Raised for each notification, in the order they arrived, before the lines are counted as arrived.</summary>
    event Action<IReadOnlyList<string>>? Printed;

    /// <summary>Takes a notification from the environment host.</summary>
    /// <param name="notification">The lines, and how many the host has said in all.</param>
    void Publish(HostOutputNotification notification);

    /// <summary>
    /// Completes when the lines the host has said, up to <paramref name="total"/> of them, have all arrived.
    /// </summary>
    /// <param name="total">How many lines the host had said when it answered.</param>
    /// <param name="timeout">How long to wait for lines that do not come: a host that was lost does not say them.</param>
    /// <param name="token">A token that cancels the wait.</param>
    Task WaitForAsync(long total, TimeSpan timeout, CancellationToken token);
}

internal sealed class HostOutputRelay : IHostOutputRelay
{
    private readonly object _sync = new();
    private long _arrived;
    private TaskCompletionSource _advanced = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public event Action<IReadOnlyList<string>>? Printed;

    public void Publish(HostOutputNotification notification)
    {
        // said before it is counted: whoever waits for the count has the lines already.
        Printed?.Invoke(notification.Lines);

        TaskCompletionSource advanced;
        lock (_sync)
        {
            _arrived = Math.Max(_arrived, notification.Total);
            advanced = _advanced;
            _advanced = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        advanced.TrySetResult();
    }

    public async Task WaitForAsync(long total, TimeSpan timeout, CancellationToken token)
    {
        using var expiry = CancellationTokenSource.CreateLinkedTokenSource(token);
        expiry.CancelAfter(timeout);
        try
        {
            while (true)
            {
                Task advanced;
                lock (_sync)
                {
                    if (_arrived >= total)
                    {
                        return;
                    }

                    advanced = _advanced.Task;
                }

                await advanced.WaitAsync(expiry.Token);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            // the lines that were not said are not coming.
        }
    }
}

/// <summary>
/// Handles <c>rdcore/host/output</c>: the lines a program printed, from the environment host.
/// </summary>
internal sealed class HostOutputHandler(IHostOutputRelay relay) : IJsonRpcNotificationHandler<HostOutputNotification>
{
    public Task<MediatR.Unit> Handle(HostOutputNotification request, CancellationToken cancellationToken)
    {
        relay.Publish(request);
        return Task.FromResult(MediatR.Unit.Value);
    }
}
