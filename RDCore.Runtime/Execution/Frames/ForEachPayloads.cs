using RDCore.Runtime.LanguageCore.Binding;

namespace RDCore.Runtime.Execution.Frames;

/// <summary>
/// What a <c>For Each</c> over an array assigned on payloads keeps from one <c>Next</c> to the next
/// (<see cref="SDK.Runtime.Shared.ForEachState.RuntimeState"/>): the slot of its control variable, found once.
/// </summary>
/// <remarks>
/// Filled by the first <c>Next</c>. A control variable that is not a local of the activation - a field, a Static, a parameter bound to another variable -
/// has no slot, and is assigned the way it always is.
/// </remarks>
internal sealed class ForEachPayloads
{
    /// <summary>The slot of the control variable; <see langword="null"/> when it has none.</summary>
    public FrameSlot? Control;
}
