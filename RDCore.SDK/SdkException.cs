namespace RDCore.SDK
{
    /// <summary>
    /// The base abstract class for all <c>RDCore.SDK</c> exception types.
    /// </summary>
    /// <param name="message"></param>
    /// <param name="verbose"></param>
    /// <param name="inner"></param>
    public abstract class SdkException(string message, string? verbose = default, Exception? inner = default) : Exception(message, inner)
    {
        /// <summary>
        /// Extended error details (may include information such as paths, file, or identifier names, line and column numbers, stack traces, semantic flags, and other useful debugging metadata).
        /// </summary>
        public string? Verbose { get; init; } = verbose;
    }
}
