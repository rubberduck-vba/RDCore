using RDCore.Runtime.Support.Host;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.StdLib;

namespace RDCore.Tests.Cli;

/// <summary>
/// A client that answers the program's <c>MsgBox</c> with OK, and says that it was asked (<see cref="Asked"/>): the answer is made before anything listening
/// hears of it, so a break asked for then reaches a program that is past its <c>MsgBox</c>, and not a dialog the break would cancel.
/// </summary>
internal sealed class AnswersOk : IHostInteractionChannel
{
    private readonly TaskCompletionSource _asked = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Completes when the program has asked its first <c>MsgBox</c>, and has its answer.
    /// </summary>
    public Task Asked => _asked.Task;

    public Task<MessageBoxResult> MessageBoxAsync(HostMessageBoxParams request, CancellationToken token)
    {
        var answer = Task.FromResult(new MessageBoxResult { Handled = true, Result = (int)VBMsgBoxResult.VBOK });
        _asked.TrySetResult();
        return answer;
    }

    public Task<InputBoxResult> InputBoxAsync(HostInputBoxParams request, CancellationToken token)
        => throw new AssertFailedException("the program asks for no InputBox");
}
