using System.Globalization;
using System.Runtime.ExceptionServices;

namespace RDCore.SDK.Platform;

/// <summary>
/// Runs work whose depth of recursion is the depth of the code it reads - the parse of a module, its load, the evaluation of its code - on a thread whose stack
/// has room for the deepest code RD-VBA reads.
/// </summary>
/// <remarks>
/// <para>
/// A parse, the tree it builds and the semantics of the tree recurse once or more for each level of an expression: a thread's default stack (1 MB) holds a few
/// hundred levels, and MS-VBA runs expressions 2,500 deep (a chain of 2,500 terms, measured in Excel). An expression nested deeper than RD-VBA reads is a syntax
/// error (<c>VBC00060</c>); one up to that depth is read on a thread of this size.
/// </para>
/// <para>
/// The stack is reserved, and committed only as it is used. On Linux a thread's stack counts toward the memory the system has committed to (<c>Committed_AS</c>):
/// under strict overcommit (<c>vm.overcommit_memory=2</c>), creating a thread of this size can fail.
/// </para>
/// <para>
/// The work runs in the caller's culture and UI culture, which say what its messages are written in. Work that is already running on such a thread and waited for
/// (<see cref="Run{T}"/>) runs where it is, so that a parse made by a load takes no second thread. Work that is not waited for (<see cref="Start"/>) runs on
/// at most <see cref="StartedAtOnce"/> threads at a time, and the rest of it waits for one of them to be done.
/// </para>
/// </remarks>
public static class LargeStackThread
{
    /// <summary>
    /// The size of the stack of a thread work is run on: 256 MB, as a program's own (<c>RuntimeCallStack.ProgramThreadStackSize</c>).
    /// </summary>
    public const int StackSize = 256 * 1024 * 1024;

    /// <summary>
    /// How many threads run the work <see cref="Start"/> is given at the same time: as many as there are processors, and at least 2.
    /// </summary>
    public static int StartedAtOnce { get; } = Math.Max(2, Environment.ProcessorCount);

    private static readonly SemaphoreSlim _started = new(StartedAtOnce, StartedAtOnce);

    [ThreadStatic]
    private static bool _isLargeStack;

    /// <summary>
    /// Runs <paramref name="work"/> on a thread of <see cref="StackSize"/>, and waits for it: what it returns is returned, and what it throws is thrown, as if the
    /// work had run on the caller's own thread.
    /// </summary>
    /// <typeparam name="T">What the work returns.</typeparam>
    /// <param name="work">The work.</param>
    /// <returns>What <paramref name="work"/> returned.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="work"/> is <see langword="null"/>.</exception>
    /// <remarks>An exception <paramref name="work"/> throws is rethrown with the stack trace it was thrown with.</remarks>
    public static T Run<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (_isLargeStack)
        {
            return work();
        }

        T result = default!;
        ExceptionDispatchInfo? failure = null;
        var thread = Create(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        });

        thread.Start();
        thread.Join();
        failure?.Throw();
        return result;
    }

    /// <summary>
    /// Starts <paramref name="work"/> on a thread of <see cref="StackSize"/>, and does not wait for it.
    /// </summary>
    /// <param name="work">The work.</param>
    /// <param name="token">Cancels the work while it waits for a thread: it is then never run. Once it runs, stopping it is the work's own business.</param>
    /// <returns>
    /// A task that completes when the work does: faulted with what it throws, and cancelled when what it throws is an <see cref="OperationCanceledException"/>,
    /// or when <paramref name="token"/> cancels it before it runs.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="work"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Work runs on at most <see cref="StartedAtOnce"/> threads at a time; work started when they are all busy waits, holding no thread, for one of them to be
    /// done. Work started this way therefore never waits for other work started this way: if every thread did, none would be left to run it.
    /// </remarks>
    public static Task Start(Action work, CancellationToken token = default) => Start(work, _started, token);

    // the work runs once one of the threads counted by the semaphore is free.
    internal static Task Start(Action work, SemaphoreSlim threads, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(work);
        var (culture, uiCulture) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        return StartWhenAThreadIsFree(work, threads, culture, uiCulture, token);
    }

    private static async Task StartWhenAThreadIsFree(Action work, SemaphoreSlim threads, CultureInfo culture, CultureInfo uiCulture, CancellationToken token)
    {
        await threads.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await StartNow(work, culture, uiCulture).ConfigureAwait(false);
        }
        finally
        {
            threads.Release();
        }
    }

    private static Task StartNow(Action work, CultureInfo culture, CultureInfo uiCulture)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Create(() =>
        {
            try
            {
                work();
                completion.SetResult();
            }
            catch (OperationCanceledException cancelled)
            {
                completion.SetCanceled(cancelled.CancellationToken);
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }, culture, uiCulture).Start();

        return completion.Task;
    }

    // a thread of StackSize that runs work in the culture of the thread that creates it.
    private static Thread Create(Action work) => Create(work, CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);

    // a thread of StackSize that runs work in a culture.
    private static Thread Create(Action work, CultureInfo culture, CultureInfo uiCulture)
    {
        return new Thread(() =>
        {
            _isLargeStack = true;
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (culture, uiCulture);
            work();
        }, StackSize) { IsBackground = true, Name = "RDCore large stack" };
    }
}
