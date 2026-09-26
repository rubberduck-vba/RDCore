using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Errors;

namespace RDCore.Runtime.Execution;

/// <summary>
/// What executing one <see cref="RDCore.SDK.Semantics.Instructions.Instruction"/> did to control flow —
/// the executor loop's fetch/decode step reacts to this, deciding the next program-counter value.
/// </summary>
public enum RuntimeExecutionOutcomeKind
{
    /// <summary>Fall through to the next offset.</summary>
    Next,
    /// <summary>Branch to <see cref="RuntimeExecutionOutcome.Target"/>.</summary>
    Branch,
    /// <summary><c>Exit Sub</c>/<c>Exit Function</c>/<c>Exit Property</c>, or falling off the end of the list.</summary>
    ExitProcedure,
    /// <summary><c>End</c>.</summary>
    Halt,
    /// <summary><c>Stop</c>.</summary>
    Break,
    /// <summary>A real MS-VBA runtime error was raised.</summary>
    Error,
    /// <summary>
    /// The instruction, or the statement it carries, is not wired yet — deferred rather than silently
    /// mishandled, the same convention <c>RuntimeSemanticsEvaluationResult.InternalError</c> already
    /// uses for expressions.
    /// </summary>
    InternalError,
}

/// <summary>
/// The result of executing one instruction — see <see cref="RuntimeExecutionOutcomeKind"/> for what
/// each <see cref="Kind"/> means to the executor loop.
/// </summary>
public readonly record struct RuntimeExecutionOutcome(RuntimeExecutionOutcomeKind Kind, int? Target = null, IVBRaisableError? ErrorInfo = null)
{
    /// <summary>Fall through to the next offset.</summary>
    public static readonly RuntimeExecutionOutcome Next = new(RuntimeExecutionOutcomeKind.Next);

    /// <summary>Branch to <paramref name="target"/>.</summary>
    public static RuntimeExecutionOutcome Branch(int target) => new(RuntimeExecutionOutcomeKind.Branch, target);

    /// <summary><c>Exit Sub</c>/<c>Exit Function</c>/<c>Exit Property</c>, or falling off the end of the list.</summary>
    public static readonly RuntimeExecutionOutcome ExitProcedure = new(RuntimeExecutionOutcomeKind.ExitProcedure);

    /// <summary><c>End</c>.</summary>
    public static readonly RuntimeExecutionOutcome Halt = new(RuntimeExecutionOutcomeKind.Halt);

    /// <summary><c>Stop</c>.</summary>
    public static readonly RuntimeExecutionOutcome Break = new(RuntimeExecutionOutcomeKind.Break);

    /// <summary>A real MS-VBA runtime error was raised.</summary>
    public static RuntimeExecutionOutcome Error(IVBRaisableError errorInfo) => new(RuntimeExecutionOutcomeKind.Error, ErrorInfo: errorInfo);

    /// <summary>The instruction, or the statement it carries, is not wired yet.</summary>
    public static readonly RuntimeExecutionOutcome InternalError = new(RuntimeExecutionOutcomeKind.InternalError);
}
