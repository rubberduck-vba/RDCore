namespace RDCore.Runtime.Execution.Frames;

/// <summary>
/// Whether variables of a numeric scalar type are held as payloads (<see cref="ScalarCell"/>), and expressions of them evaluated on payloads (IR
/// lowering S1.2), or every value is held and evaluated boxed, as it was.
/// </summary>
/// <remarks>
/// It is on, unless the environment variable <c>RDCORE_UNBOXED</c> is <c>0</c>: the test suite is run both ways, the boxed evaluation being the oracle
/// the unboxed one is held to (plan §8.2, the differential switch). Compiled code works on payloads, and so is compiled only when they are used: a test of
/// it says so with <see cref="Using"/>, whatever the suite is run with.
/// </remarks>
internal static class UnboxedEvaluation
{
    private static readonly bool _configured = Environment.GetEnvironmentVariable("RDCORE_UNBOXED") != "0";
    private static readonly AsyncLocal<bool?> _scoped = new();

    // how many scopes are open, in any flow: with none, what the environment says is read without a look at the flow's.
    private static int _scopes;

    /// <summary>Whether payloads are used: what a scope says (<see cref="Using"/>), then what the environment says.</summary>
    public static bool IsEnabled => _scopes == 0 ? _configured : _scoped.Value ?? _configured;

    /// <summary>
    /// Runs what follows, in this asynchronous flow, with payloads used or not as <paramref name="enabled"/> says, until the scope is disposed of. A
    /// session holds its values one way: the scope is opened before the session runs, and stays open until it has.
    /// </summary>
    /// <param name="enabled">Whether payloads are used.</param>
    public static IDisposable Using(bool enabled)
    {
        var previous = _scoped.Value;
        _scoped.Value = enabled;
        _ = Interlocked.Increment(ref _scopes);
        return new Scope(previous);
    }

    private sealed class Scope(bool? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _scoped.Value = previous;
            _ = Interlocked.Decrement(ref _scopes);
        }
    }
}
