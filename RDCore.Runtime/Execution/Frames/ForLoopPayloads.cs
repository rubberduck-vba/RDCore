using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.Runtime.LanguageCore.Binding;
using RDCore.Runtime.LanguageCore.Plans;

namespace RDCore.Runtime.Execution.Frames;

/// <summary>
/// What a <c>For</c> loop stepped on payloads keeps from one <c>Next</c> to the next (<see cref="SDK.Runtime.Shared.ForLoopState.RuntimeState"/>):
/// the counter as it is bound, the step and the end as payloads - which do not change while the loop runs - and, for a <c>Variant</c> counter, the plans of
/// its sum and of its comparison with the end, for the subtype it last held.
/// </summary>
/// <remarks>
/// Filled by the first <c>Next</c> that steps on payloads. The counter is bound again when the session's scopes are not the ones it was bound in (a module
/// loaded again while the program waits).
/// </remarks>
internal sealed class ForLoopPayloads
{
    /// <summary>Whether the step and the end are payloads of the loop's type, and the counter is bound: what stepping on payloads needs.</summary>
    public bool IsReady;

    /// <summary>Whether the loop was found not to be one stepped on payloads: the step or the end is not a payload.</summary>
    public bool IsDeclined;

    /// <summary>The scopes <see cref="Counter"/> was bound in.</summary>
    public ScopeTree? Scopes;

    /// <summary>The counter, as it is bound.</summary>
    public BoundVariable? Counter;

    /// <summary>The step, as a payload.</summary>
    public ScalarPayload Step;

    /// <summary>The end, as a payload.</summary>
    public ScalarPayload End;

    /// <summary>The subtype of a <c>Variant</c> counter the plans are of.</summary>
    public VBType? PlannedFor;

    /// <summary>The plan of <c>counter + step</c>, for <see cref="PlannedFor"/>.</summary>
    public OperationPlan? Sum;

    /// <summary>The plan of the comparison of the sum with the end.</summary>
    public OperationPlan? Comparison;
}
