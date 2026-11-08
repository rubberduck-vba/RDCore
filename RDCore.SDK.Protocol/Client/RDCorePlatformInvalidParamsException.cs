namespace RDCore.SDK.Client;

/// <summary>
/// The <c>params</c> of a request the server sent the client are not what the method takes: a channel that can answer with an error code answers
/// <see cref="ErrorCode"/> (JSON-RPC <c>InvalidParams</c>).
/// </summary>
/// <remarks>
/// Raised by the typed handlers of <see cref="RDCorePlatformChannelExtensions.OnRequest{TParams, TResult}"/> before the handler runs, so that a request the client cannot
/// read is told apart from one it failed to answer.
/// </remarks>
public sealed class RDCorePlatformInvalidParamsException : Exception
{
    /// <summary>JSON-RPC 2.0 <c>InvalidParams</c>: the parameters of the request are not valid for its method.</summary>
    public const int ErrorCode = -32602;

    /// <summary>
    /// Creates the exception.
    /// </summary>
    /// <param name="method">The method of the request.</param>
    /// <param name="innerException">Why the parameters could not be read.</param>
    public RDCorePlatformInvalidParamsException(string method, Exception innerException)
        : base($"The params of {method} are not what it takes: {innerException.Message}", innerException)
    {
        Method = method;
    }

    /// <summary>The method of the request.</summary>
    public string Method { get; }
}
