namespace RDCore.SDK.Client.Connection;

/// <summary>
/// A request to a child process is answered by that process or not at all: when the process exits first, the request fails, and a connection that restarts
/// does not send it again (what crashed one process is likely to crash the next one with it).
/// </summary>
internal static class RequestsInFlight
{
    /// <summary>
    /// The answer to a request, or a <see cref="ServerProtocolSdkException"/> once the process it was sent to has exited.
    /// </summary>
    /// <param name="answer">The request, sent.</param>
    /// <param name="lost">Completes when the process the request was sent to exits.</param>
    /// <param name="method">The method of the request, for the message.</param>
    public static async Task<T> AnsweredAsync<T>(Task<T> answer, Task lost, string method)
    {
        if (await Task.WhenAny(answer, lost) == answer)
        {
            return await answer;
        }

        // the request is abandoned: whatever becomes of it on the dead connection is observed here, and not left to the finalizer.
        _ = answer.ContinueWith(static abandoned => _ = abandoned.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        throw new ServerProtocolSdkException($"The child process exited before it answered '{method}'.");
    }
}
