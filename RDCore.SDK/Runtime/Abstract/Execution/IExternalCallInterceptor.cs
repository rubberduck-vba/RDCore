namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// What an <see cref="IExternalCallInterceptor"/> decided about a call.
/// </summary>
/// <param name="Reason">
/// Why the call was refused, in words a person reading a diagnostic needs — <see langword="null"/> when it
/// was not refused.
/// </param>
public readonly record struct ExternalCallDecision(string? Reason)
{
    /// <summary>
    /// Let the call through.
    /// </summary>
    public static ExternalCallDecision Allow { get; } = new((string?)null);

    /// <summary>
    /// Refuse the call.
    /// </summary>
    /// <param name="reason">
    /// Why — it reaches the workspace as the verbose half of a <c>Permission denied</c> run-time error, so
    /// it should say what was refused and on what grounds.
    /// </param>
    public static ExternalCallDecision Block(string reason) => new(reason);

    /// <summary>
    /// Whether the call was refused.
    /// </summary>
    public bool IsBlocked => Reason is not null;
}

/// <summary>
/// Sees every call to something outside the workspace before it happens, and may refuse it.
/// </summary>
/// <remarks>
/// 🎯 The reason this exists: a VBA environment that can call into native libraries is a way to run
/// arbitrary code, and the historical answer to that has been for administrators to disable the whole
/// language because nothing could see what a macro actually did. This is the seam that makes the calls
/// visible — every one of them, with its arguments, before it runs — and refusable one at a time.
/// <para>
/// Interceptors are ordered and every one of them sees the call; the first to refuse it stops it. An
/// interceptor is expected to be cheap and to have no opinion about most calls: it runs in front of
/// <em>every</em> external call, not only the dangerous ones.
/// </para>
/// <para>
/// 🚧 An interceptor cannot yet <em>answer</em> a call. That is the same seam a mock needs, and is where the
/// planned Unit Testing extension's <c>MsgBox</c>/<c>InputBox</c> substitution lands — TODO extend
/// <see cref="ExternalCallDecision"/> with a result rather than adding a second mechanism beside this one.
/// </para>
/// </remarks>
public interface IExternalCallInterceptor
{
    /// <summary>
    /// Decides whether <paramref name="request"/> may proceed.
    /// </summary>
    /// <remarks>
    /// This is also the point at which a call can be <em>logged</em>: the request describes itself
    /// (<see cref="ExternalCallRequest.Describe"/>), arguments included, and an interceptor that only wants a
    /// record of what a workspace did returns <see cref="ExternalCallDecision.Allow"/> having written one.
    /// </remarks>
    /// <param name="request">The call about to be made.</param>
    /// <param name="session">The session making it.</param>
    ExternalCallDecision Intercept(ExternalCallRequest request, IRuntimeSession session);
}

/// <summary>
/// Runs a call to one kind of external target.
/// </summary>
/// <remarks>
/// A provider says what it can reach and reaches it. Which providers exist is what makes a platform able to
/// do a thing at all: no COM provider on a machine means a workspace's Excel automation reports that it
/// cannot be run here, rather than failing in some way particular to how it was attempted.
/// </remarks>
public interface IExternalCallProvider
{
    /// <summary>
    /// Whether this provider is the one that runs <paramref name="request"/>.
    /// </summary>
    /// <param name="request">The call to consider.</param>
    bool CanDispatch(ExternalCallRequest request);

    /// <summary>
    /// Runs it, and returns what it yielded — an error in the result rather than thrown, as everywhere else
    /// in the semantics layer.
    /// </summary>
    /// <param name="request">The call to run. <see cref="CanDispatch"/> has already accepted it.</param>
    /// <param name="resolver">A read-only interface over the current execution context.</param>
    Shared.RuntimeSemanticsEvaluationResult Dispatch(ExternalCallRequest request, ISymbolResolver resolver);
}
