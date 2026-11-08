namespace RDCore.SDK.Semantics.Flow;

/// <summary>
/// The error-handling policy of a procedure activation (<strong>MS-VBAL §5.4.4</strong>): what happens to an error raised in it.
/// </summary>
public enum ErrorHandlingPolicy
{
    /// <summary>
    /// No <c>On Error</c> statement is in effect, or <c>On Error GoTo 0</c> is: an error is raised to the caller.
    /// </summary>
    Default,

    /// <summary>
    /// <c>On Error Resume Next</c>: execution continues with the statement after the one that raised the error.
    /// </summary>
    ResumeNext,

    /// <summary>
    /// <c>On Error GoTo label</c>: execution continues at the handler, unless an error is already being handled.
    /// </summary>
    GoTo,
}

/// <summary>
/// How an error raised by an instruction would be handled, as one code path to it has it.
/// </summary>
/// <param name="Policy">The policy in effect.</param>
/// <param name="Handler">For <see cref="ErrorHandlingPolicy.GoTo"/>: the offset of the handler, or <see langword="null"/> when its label did not resolve.</param>
/// <param name="IsHandling">
/// Whether an error is being handled: control reached the instruction from a handler that was taken and has not resumed yet (the <em>active error</em> of
/// <strong>MS-VBAL §5.4.4</strong>). While it is, an error is raised to the caller whatever the policy says.
/// </param>
public readonly record struct ErrorHandlingState(ErrorHandlingPolicy Policy, int? Handler, bool IsHandling)
{
    /// <summary>
    /// The state an activation starts in: no policy, and no error.
    /// </summary>
    public static ErrorHandlingState Initial { get; } = new(ErrorHandlingPolicy.Default, null, false);

    /// <summary>
    /// The handler an error raised in this state goes to; <see langword="null"/> when it is raised to the caller, or resumed at the next statement.
    /// </summary>
    public int? CatchingHandler => Policy is ErrorHandlingPolicy.GoTo && !IsHandling ? Handler : null;

    /// <summary>
    /// Whether an error raised in this state is raised to the caller.
    /// </summary>
    public bool RaisesToCaller => IsHandling || Policy is ErrorHandlingPolicy.Default || (Policy is ErrorHandlingPolicy.GoTo && Handler is null);
}
