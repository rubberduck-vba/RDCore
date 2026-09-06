namespace RDCore.SDK.Server;

/// <summary>
/// A last-resort guard that hard-terminates the current process if a clean shutdown does not finish
/// unwinding in time — for example, a non-cooperative background thread or finalizer from a
/// third-party library keeping the process alive after <c>Main</c> has returned.
/// </summary>
public static class ProcessWatchdog
{
    /// <summary>
    /// Arms a background thread that calls <see cref="Environment.Exit(int)"/> with
    /// <paramref name="exitCode"/> after <paramref name="graceSeconds"/> seconds. A process that unwinds
    /// cleanly first races ahead of it and the thread is torn down with the process, so this only ever
    /// fires when something is genuinely wedged. Arm it once the shutdown sequence is underway.
    /// </summary>
    /// <param name="exitCode">The exit code to terminate with if the watchdog fires.</param>
    /// <param name="graceSeconds">Seconds to wait for a clean exit before terminating (minimum 1).</param>
    public static void Arm(int exitCode, int graceSeconds = 10) => Arm(exitCode, graceSeconds, Environment.Exit);

    internal static void Arm(int exitCode, int graceSeconds, Action<int> terminate)
    {
        var watchdog = new Thread(() =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(Math.Max(1, graceSeconds)));
            terminate(exitCode);
        })
        {
            IsBackground = true,
            Name = "rdcore-exit-watchdog",
        };
        watchdog.Start();
    }
}
