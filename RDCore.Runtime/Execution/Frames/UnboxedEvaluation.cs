namespace RDCore.Runtime.Execution.Frames;

/// <summary>
/// Whether variables of a numeric scalar type are held as payloads (<see cref="ScalarCell"/>), and expressions of them evaluated on payloads (IR
/// lowering S1.2), or every value is held and evaluated boxed, as it was.
/// </summary>
/// <remarks>
/// It is on, unless the environment variable <c>RDCORE_UNBOXED</c> is <c>0</c>: the test suite is run both ways, the boxed evaluation being the oracle
/// the unboxed one is held to (plan §8.2, the differential switch).
/// </remarks>
internal static class UnboxedEvaluation
{
    /// <summary>Whether payloads are used.</summary>
    public static bool IsEnabled { get; } = Environment.GetEnvironmentVariable("RDCORE_UNBOXED") != "0";
}
