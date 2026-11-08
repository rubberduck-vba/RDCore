namespace RDCore.Tests;

/// <summary>
/// Runs work on a thread with a 256 KB stack: a quarter of a thread's default, and as small as a thread the language server or an extension may answer a request
/// on. What a module past the limit of nesting (VBC00060) is still given - its symbols, its tokens, its indentation - is given there without a crash: a stack
/// overflow would end the test host.
/// </summary>
internal static class SmallStack
{
    public const int Size = 256 * 1024;

    /// <summary>
    /// Runs <paramref name="work"/> on a thread with a stack of <see cref="Size"/>, and waits for it.
    /// </summary>
    /// <returns>What <paramref name="work"/> threw, or <see langword="null"/>.</returns>
    public static Exception? Run(Action work)
    {
        Exception? thrown = null;
        var thread = new Thread(() =>
        {
            try
            {
                work();
            }
            catch (Exception exception)
            {
                thrown = exception;
            }
        }, Size);
        thread.Start();
        thread.Join();
        return thrown;
    }
}
